using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using xLozerOptimizer.Models;

namespace xLozerOptimizer.Core
{
    public class OptimizationReport
    {
        public int ProcessesKilled { get; set; }
        public int ServicesStopped { get; set; }
        public double RamFreedMb { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class OptimizerEngine
    {
        private readonly string[] _defaultServicesToManage =
        {
            "SysMain", "WSearch", "wuauserv", "BITS", "DiagTrack",
            "nordvpn-service", "nordsec-threatprotection-service"
        };

        public async Task<OptimizationReport> ApplyOptimizationsAsync(List<OptimizeItem> itemsToOptimize)
        {
            return await Task.Run(() =>
            {
                var beforeMem = MemoryManager.GetMemoryMetrics();
                int killedCount = 0;
                int stoppedServicesCount = 0;

                foreach (var item in itemsToOptimize)
                {
                    if (!item.ShouldOptimize)
                        continue;

                    if (item.IsService)
                    {
                        if (StopAndLockService(item.Name))
                            stoppedServicesCount++;
                    }
                    else
                    {
                        if (KillProcessGroup(item.Name))
                            killedCount++;
                    }
                }

                // Executa limpeza completa da RAM (Working Set + Standby List)
                MemoryManager.ClearStandbyList();

                var afterMem = MemoryManager.GetMemoryMetrics();
                double totalFreedMb = Math.Max(0, afterMem.AvailableMb - beforeMem.AvailableMb);

                return new OptimizationReport
                {
                    ProcessesKilled = killedCount,
                    ServicesStopped = stoppedServicesCount,
                    RamFreedMb = totalFreedMb,
                    IsAdmin = MemoryManager.IsRunningAsAdmin()
                };
            });
        }

        public async Task<int> RestoreSystemAsync()
        {
            return await Task.Run(() =>
            {
                int restored = 0;
                foreach (var serviceName in _defaultServicesToManage)
                {
                    if (UnlockAndStartService(serviceName))
                        restored++;
                }
                return restored;
            });
        }

        private bool KillProcessGroup(string processName)
        {
            bool killedAny = false;
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var proc in processes)
                {
                    try
                    {
                        // Segurança extra: nunca tenta encerrar processos de Kernel (SessionId 0)
                        if (proc.SessionId == 0)
                            continue;

                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1500);
                        killedAny = true;
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao encerrar {processName}: {ex.Message}");
            }
            return killedAny;
        }

        private bool StopAndLockService(string serviceName)
        {
            try
            {
                using ServiceController sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                {
                    // 1. Altera o serviço para Manual (demand) para impedir que o Windows Search/SysMain reinicie sozinho
                    RunScCommand($"config \"{serviceName}\" start= demand");

                    // 2. Para o serviço ativamente
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6));
                    return sc.Status == ServiceControllerStatus.Stopped;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao parar serviço {serviceName}: {ex.Message}");
            }
            return false;
        }

        private bool UnlockAndStartService(string serviceName)
        {
            try
            {
                using ServiceController sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    // Devolve o modo automático padrão para SysMain e WSearch
                    if (serviceName.Equals("SysMain", StringComparison.OrdinalIgnoreCase) ||
                        serviceName.Equals("WSearch", StringComparison.OrdinalIgnoreCase))
                    {
                        RunScCommand($"config \"{serviceName}\" start= auto");
                    }
                    else
                    {
                        RunScCommand($"config \"{serviceName}\" start= demand");
                    }

                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(6));
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao iniciar serviço {serviceName}: {ex.Message}");
            }
            return false;
        }

        private void RunScCommand(string arguments)
        {
            try
            {
                using Process proc = new Process();
                proc.StartInfo.FileName = "sc.exe";
                proc.StartInfo.Arguments = arguments;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.UseShellExecute = false;
                proc.Start();
                proc.WaitForExit(2000);
            }
            catch { }
        }
    }
}