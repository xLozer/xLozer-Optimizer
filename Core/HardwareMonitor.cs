using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace xLozerOptimizer.Core
{
    public class TelemetrySnapshot
    {
        public double CpuUsagePercent { get; set; }
        public double RamUsagePercent { get; set; }
        public double RamAvailableGb { get; set; }
        public double RamUsedGb { get; set; }
        public bool AutoCleanedNow { get; set; }
        public double AutoFreedMb { get; set; }
    }

    public class HardwareMonitor
    {
        private PerformanceCounter? _cpuCounter;
        private DateTime _lastAutoClean = DateTime.MinValue;

        // Dados de Hardware Estáticos (Lidos automaticamente da máquina do usuário)
        public string UserName { get; private set; } = Environment.UserName;
        public string CpuName { get; private set; } = "Processador Desconhecido";
        public string CpuCoresText { get; private set; } = "Núcleos N/A";

        public string GpuName { get; private set; } = "Gráficos Integrados / Padrão";
        public string GpuTypeText { get; private set; } = "DirectX Adapter";

        public string TotalRamText { get; private set; } = "16.0 GB";
        public string RamTypeText { get; private set; } = "DDR4 / Dual Channel";

        public string OsName { get; private set; } = "Microsoft Windows";
        public string OsVersionText { get; private set; } = "64-bit";

        public string DiskModel { get; private set; } = "Unidade Principal (C:)";
        public string DiskSpaceText { get; private set; } = "N/A";

        public bool AutoCleanEnabled { get; set; } = true;

        public HardwareMonitor()
        {
            DetectSystemHardware();
            InitializeCounters();
        }

        private void InitializeCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }
                catch
                {
                    _cpuCounter = null;
                }
            }
        }

        private void DetectSystemHardware()
        {
            // 1. CPU (Modelo + Contagem de Cores/Threads)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    CpuName = obj["Name"]?.ToString()?.Trim() ?? "CPU Genérica";
                    string cores = obj["NumberOfCores"]?.ToString() ?? "?";
                    string threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "?";
                    CpuCoresText = $"{cores} Cores / {threads} Threads";
                    break;
                }
            }
            catch { }

            // 2. GPU (Modelo + Tipo Integrada vs Dedicada)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString()?.Trim() ?? "GPU";
                    GpuName = name;

                    bool isIntegrated = name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)
                                     || name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase)
                                     || name.Contains("Intel(R) Iris", StringComparison.OrdinalIgnoreCase)
                                     || name.Contains("Vega", StringComparison.OrdinalIgnoreCase);

                    GpuTypeText = isIntegrated ? "Integrated (APU Graphics)" : "Dedicated Graphics";
                    break;
                }
            }
            catch { }

            // 3. Memória RAM Total + Tipo (DDR4 / DDR5 / Velocidade MHz)
            try
            {
                var memMetrics = MemoryManager.GetMemoryMetrics();
                TotalRamText = $"{memMetrics.TotalGb:F2} GB Usável";

                using var searcher = new ManagementObjectSearcher("SELECT SMBIOSMemoryType, Speed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    int memType = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                    string speed = obj["Speed"]?.ToString() ?? "";
                    string ddrLabel = memType switch
                    {
                        24 => "DDR3",
                        26 => "DDR4",
                        34 => "DDR5",
                        _ => "DRAM"
                    };
                    RamTypeText = string.IsNullOrEmpty(speed) ? ddrLabel : $"{ddrLabel} @ {speed} MHz";
                    break;
                }
            }
            catch { }

            // 4. Sistema Operacional (Nome + Build/DisplayVersion)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    OsName = obj["Caption"]?.ToString()?.Replace("Microsoft ", "").Trim() ?? "Windows 11";
                    string build = obj["BuildNumber"]?.ToString() ?? "";
                    OsVersionText = $"Build {build} (64-bit)";
                    break;
                }
            }
            catch { }

            // 5. Armazenamento (Modelo do SSD/NVMe + Capacidade Livre/Total)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get())
                {
                    DiskModel = obj["Model"]?.ToString()?.Trim() ?? "SSD / NVMe";
                    break;
                }

                DriveInfo cDrive = new DriveInfo("C");
                if (cDrive.IsReady)
                {
                    double totalGb = cDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = cDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    DiskSpaceText = $"{totalGb:F0} GB Total ({freeGb:F0} GB Livres)";
                }
            }
            catch { }
        }

        public async Task StartMonitoringAsync(Action<TelemetrySnapshot> onUpdate, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    double cpuPercent = 0;
                    if (_cpuCounter != null)
                    {
                        cpuPercent = Math.Clamp(_cpuCounter.NextValue(), 0, 100);
                    }

                    var (totalGb, availMb, usedPercent) = MemoryManager.GetMemoryMetrics();
                    double availGb = availMb / 1024.0;
                    double usedGb = Math.Max(0, totalGb - availGb);

                    bool autoCleaned = false;
                    double autoFreedMb = 0;

                    // REGRA 5: Limpeza Automática Inteligente se uso >= 85% ou menos de 1.8 GB livres (com cooldown de 60s)
                    if (AutoCleanEnabled && (usedPercent >= 85 || availMb < 1800) && (DateTime.Now - _lastAutoClean).TotalSeconds > 60)
                    {
                        _lastAutoClean = DateTime.Now;
                        var cleanResult = MemoryManager.ClearStandbyList();
                        autoCleaned = true;
                        autoFreedMb = cleanResult.FreedMegabytes;
                    }

                    onUpdate(new TelemetrySnapshot
                    {
                        CpuUsagePercent = cpuPercent,
                        RamUsagePercent = usedPercent,
                        RamAvailableGb = availGb,
                        RamUsedGb = usedGb,
                        AutoCleanedNow = autoCleaned,
                        AutoFreedMb = autoFreedMb
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro Telemetria: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1200, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}