using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace xLozerOptimizer.Core
{
    public class MemoryCleanResult
    {
        public bool Success { get; set; }
        public double FreedMegabytes { get; set; }
        public double BeforeAvailableMb { get; set; }
        public double AfterAvailableMb { get; set; }
        public bool IsAdmin { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public static class MemoryManager
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern uint NtSetSystemInformation(int InfoClass, IntPtr Info, int Length);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static (double TotalGb, double AvailableMb, double UsedPercent) GetMemoryMetrics()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availMb = memStatus.ullAvailPhys / (1024.0 * 1024.0);
                double usedPercent = memStatus.dwMemoryLoad;
                return (totalGb, availMb, usedPercent);
            }
            return (16.0, 4096.0, 50.0);
        }

        public static MemoryCleanResult ClearStandbyList()
        {
            var before = GetMemoryMetrics();
            bool isAdmin = IsRunningAsAdmin();
            int trimmedProcesses = 0;

            // ETAPA 1: Esvazia o Working Set de todos os processos acessíveis (Libera RAM ativa imediatamente)
            Process[] processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (!proc.HasExited && proc.Handle != IntPtr.Zero)
                    {
                        if (EmptyWorkingSet(proc.Handle))
                            trimmedProcesses++;
                    }
                }
                catch
                {
                    // Ignora processos protegidos do sistema
                }
                finally
                {
                    proc.Dispose();
                }
            }

            // ETAPA 2: Se estiver como Administrador, limpa a Modified Page List e a Standby List no Kernel (ntdll)
            bool standbyPurged = false;
            if (isAdmin)
            {
                try
                {
                    if (EnablePrivilege("SeProfileSingleProcessPrivilege"))
                    {
                        // 3 = MemoryFlushModifiedList, 4 = MemoryPurgeStandbyList
                        ExecuteNtMemoryCommand(3);
                        uint status = ExecuteNtMemoryCommand(4);
                        standbyPurged = (status == 0);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro Kernel Standby: {ex.Message}");
                }
            }

            // Aguarda 150ms para o Windows atualizar as tabelas de páginas físicas
            System.Threading.Thread.Sleep(150);

            var after = GetMemoryMetrics();
            double freedMb = Math.Max(0, after.AvailableMb - before.AvailableMb);

            return new MemoryCleanResult
            {
                Success = trimmedProcesses > 0 || standbyPurged,
                IsAdmin = isAdmin,
                BeforeAvailableMb = before.AvailableMb,
                AfterAvailableMb = after.AvailableMb,
                FreedMegabytes = freedMb,
                Message = isAdmin
                    ? $"Cache Standby + Working Set limpos ({trimmedProcesses} processos otimizados). Liberados: {freedMb:F0} MB."
                    : $"Working Set limpo ({trimmedProcesses} processos). Liberados: {freedMb:F0} MB. (Execute como Admin para limpar também a Standby List)."
            };
        }

        private static uint ExecuteNtMemoryCommand(int command)
        {
            IntPtr pCommand = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(pCommand, command);
                return NtSetSystemInformation(80, pCommand, sizeof(int)); // 80 = SystemMemoryListInformation
            }
            finally
            {
                Marshal.FreeHGlobal(pCommand);
            }
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out IntPtr tokenHandle))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out long luid))
                    return false;

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                };

                return AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
    }
}