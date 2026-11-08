using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using xLozerOptimizer.Models;

namespace xLozerOptimizer.Core
{
    public class SystemScanner
    {
        // Regra 3 + Utilitários de Bandeja/Monitor (Ex: Twinkle Tray): Listados, mas DESMARCADOS por padrão
        private readonly HashSet<string> _defaultUncheckedApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord", "spotify", "steam", "steamwebhelper", "translucenttb",
            "twinkle tray", "twinkletray", "eartrumpet", "wallpaper32", "wallpaper64",
            "sharex", "lightshot", "chrome", "msedge", "brave", "firefox", "opera"
        };

        // Processos críticos do Sistema, Kernel, WebView, Segurança, Áudio, Drivers e Dev que NUNCA devem aparecer na lista
        private readonly HashSet<string> _systemAndDevBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            // Kernel, Memória Comprimida & Shell do Windows 11
            "system", "idle", "registry", "memory compression", "secure system", "vmmem", "vmmemwsl",
            "smss", "csrss", "wininit", "services", "lsass", "sppsvc", "wlms", "LsaIso",
            "svchost", "fontdrvhost", "winlogon", "dwm", "explorer", "taskmgr", "spoolsv",
            "sihost", "taskhostw", "ctfmon", "shellexperiencehost", "startmenuexperiencehost",
            "searchhost", "searchindexer", "searchprotocolhost", "runtimebroker", "applicationframehost",
            "systemsettings", "textinputhost", "widgetservice", "widgets", "lockapp", "dllhost",
            "conhost", "openconsole", "windowsterminal", "cmd", "powershell", "pwsh", "wsl",
            "audiodg", "dashost", "wmiprvse", "unsecapp", "vds", "smartscreen", "useroobebroker",
            "shellhost", "backgroundtaskhost", "compattelrunner", "mousocoreworker",
            "msedgewebview2", "gamebar", "gamebarftserver", "gameinputsvc",
            
            // Segurança / Windows Defender
            "securityhealthsystray", "securityhealthservice", "msmpeng", "nissrv", "mpdefendercoreservice",

            // Ferramentas de Desenvolvimento (Evita fechar o VS Code / .NET durante testes)
            "code", "devenv", "dotnet", "msbuild", "vbcscompiler", "git", "node",

            // Jogos / Gravadores / Anti-Cheats exatos
            "fivem", "gta5", "gtavlauncher", "playgtav", "valorant", "valorant-win64-shipping",
            "vgc", "vgtray", "riotclientservices", "riotclientux", "obs64", "obs32", "obs-ffmpeg-mux"
        };

        // Termos protegidos em qualquer parte do nome ou caminho da pasta (Blindagem para sub-processos do FiveM, OBS, Rockstar, WebView2, Anti-Cheats)
        private readonly string[] _protectedSubstrings =
        {
            "fivem", "citizenfx", "cfx", "gtaprocess", "roslauncher", "socialclub", "rockstar",
            "obs-studio", "obs-browser", "obs-plugins", "streamlabs", "tiktok live", "tiktoklive",
            "easyanticheat", "battleye", "vanguard", "riot games", "anti-cheat",
            "edgewebview", "windowsapps", "microsoft.windowsshell", "systemapps"
        };

        // Prefixos de drivers de hardware (AMD, Nvidia, Intel, Realtek, Periféricos) que nunca devem ser encerrados
        private readonly string[] _driverPrefixes =
        {
            "amd", "radeon", "atieclxx", "atiesrxx", "nvidia", "nvcontainer", "nvdisplay",
            "igfx", "intel", "rtk", "realtek", "nahimic", "dolby", "logi", "lghub", "razer", "corsair"
        };

        // Serviços conhecidos de segundo plano que podem ser pausados com segurança para ganhar performance
        private readonly (string ServiceName, string FriendlyName, string Description)[] _optimizableServices =
        {
            ("SysMain", "SysMain (Superfetch)", "Pré-carregamento de apps na RAM (Pode causar stutters em jogos)"),
            ("WSearch", "Windows Search", "Indexador de arquivos em segundo plano (Alto uso de disco/RAM)"),
            ("wuauserv", "Windows Update", "Busca e download de atualizações automáticas do Windows"),
            ("BITS", "Serviço de Transferência Inteligente (BITS)", "Transferência de dados em segundo plano da Microsoft"),
            ("DiagTrack", "Telemetria Conectada (DiagTrack)", "Coleta e envio de diagnósticos de uso da Microsoft"),
            ("nordvpn-service", "NordVPN Background Service", "Serviço de túnel de rede da NordVPN em segundo plano"),
            ("nordsec-threatprotection-service", "Nord Threat Protection", "Monitoramento de tráfego da Nord em segundo plano")
        };

        public List<OptimizeItem> ScanSystem()
        {
            var resultList = new List<OptimizeItem>();
            string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();

            var allProcesses = Process.GetProcesses();
            var groupedProcesses = allProcesses.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedProcesses)
            {
                string procName = group.Key;
                string lowerName = procName.ToLowerInvariant();

                // 1. Ignora o próprio aplicativo
                if (lowerName.Contains("stream_optimizer") || lowerName.Contains("xlozer"))
                    continue;

                // 2. Ignora se o utilizador adicionou na lista de "Ignorados Sempre" (Whitelist salva no JSON)
                if (ConfigManager.Current.IgnoredProcesses.Contains(procName))
                    continue;

                // 3. Ignora se estiver na Blacklist de Sistema / Kernel / Dev
                if (_systemAndDevBlacklist.Contains(lowerName))
                    continue;

                // 4. Ignora se contiver termos de sub-processos de Jogos / FiveM / OBS / WebView2 / Anti-Cheat
                if (_protectedSubstrings.Any(sub => lowerName.Contains(sub, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // 5. Ignora se começar com prefixo de Driver de GPU/Áudio/Chipset
                if (_driverPrefixes.Any(prefix => lowerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                long totalMemoryBytes = 0;
                string? executablePath = null;
                string? friendlyDescription = null;
                string? companyName = null;
                bool hasUserSessionInstance = false;

                foreach (var proc in group)
                {
                    try
                    {
                        // Processos na SessionId 0 são serviços de sistema/Kernel (Memory Compression, Secure System, sppsvc)
                        if (proc.SessionId > 0)
                        {
                            hasUserSessionInstance = true;
                        }

                        totalMemoryBytes += proc.WorkingSet64;

                        if (executablePath == null)
                        {
                            executablePath = proc.MainModule?.FileName;
                            friendlyDescription = proc.MainModule?.FileVersionInfo?.FileDescription;
                            companyName = proc.MainModule?.FileVersionInfo?.CompanyName;
                        }
                    }
                    catch
                    {
                        // Processos protegidos pelo Kernel lançam Access Denied ao aceder ao MainModule
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // 6. BLINDAGEM CRÍTICA DE KERNEL E SESSÃO 0:
                // Se o processo roda apenas na Sessão 0 (Sistema/Serviços internos) OU se o Windows bloqueou a leitura do caminho do .exe
                // (como acontece com Memory Compression, Secure System, sppsvc e Anti-Cheats de Kernel), IGNORA TOTALMENTE!
                if (!hasUserSessionInstance || string.IsNullOrEmpty(executablePath))
                    continue;

                // 7. BLINDAGEM POR CAMINHO DE PASTA E ASSINATURA DA MICROSOFT:
                string lowerPath = executablePath.ToLowerInvariant();
                if (lowerPath.StartsWith(windowsFolder))
                    continue;

                if (_protectedSubstrings.Any(sub => lowerPath.Contains(sub, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Se for um componente de sistema assinado como "Microsoft Windows Operating System", ignora
                if (!string.IsNullOrEmpty(companyName) &&
                    companyName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                    lowerPath.Contains(@"\microsoft\edgewebview\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 8. Ignora micro-processos irrelevantes (< 15 MB somados)
                if (totalMemoryBytes < 15 * 1024 * 1024)
                    continue;

                bool isDefaultException = _defaultUncheckedApps.Contains(lowerName);
                double mb = totalMemoryBytes / (1024.0 * 1024.0);

                bool shouldOptimize;
                bool hasSavedPreference = ConfigManager.Current.SavedToggleStates.TryGetValue(procName, out bool savedState);
                if (hasSavedPreference)
                {
                    shouldOptimize = savedState;
                }
                else
                {
                    shouldOptimize = !isDefaultException;
                }

                string displayTitle = !string.IsNullOrWhiteSpace(friendlyDescription)
                    ? friendlyDescription.Trim()
                    : char.ToUpper(procName[0]) + procName.Substring(1);

                string descriptionText;
                if (hasSavedPreference)
                {
                    descriptionText = shouldOptimize
                        ? $"Preferência salva: Otimizar ({group.Count()} instância(s))"
                        : "Preferência salva: Manter aberto (Não encerrar)";
                }
                else
                {
                    descriptionText = isDefaultException
                        ? "Uso contínuo / Utilitário (Desmarcado por padrão)"
                        : $"Processo de terceiros ativo ({group.Count()} instância(s))";
                }

                resultList.Add(new OptimizeItem
                {
                    Name = procName,
                    DisplayName = $"{displayTitle} ({procName}.exe)",
                    Category = "Aplicativo em 2º Plano",
                    MemoryBytes = totalMemoryBytes,
                    MemoryUsageText = $"{mb:F0} MB",
                    IsService = false,
                    ShouldOptimize = shouldOptimize,
                    Description = descriptionText
                });
            }

            // 9. Escanear Serviços do Windows que estão ATIVOS (Running) no momento
            foreach (var svcInfo in _optimizableServices)
            {
                if (ConfigManager.Current.IgnoredProcesses.Contains(svcInfo.ServiceName))
                    continue;

                try
                {
                    using ServiceController sc = new ServiceController(svcInfo.ServiceName);
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        bool shouldOptimizeSvc = true;
                        if (ConfigManager.Current.SavedToggleStates.TryGetValue(svcInfo.ServiceName, out bool savedSvcState))
                        {
                            shouldOptimizeSvc = savedSvcState;
                        }

                        resultList.Add(new OptimizeItem
                        {
                            Name = svcInfo.ServiceName,
                            DisplayName = svcInfo.FriendlyName,
                            Category = "Serviço do Windows",
                            MemoryBytes = 0,
                            MemoryUsageText = "Ativo",
                            IsService = true,
                            ShouldOptimize = shouldOptimizeSvc,
                            Description = svcInfo.Description
                        });
                    }
                }
                catch
                {
                    // Serviço não instalado nesta máquina
                }
            }

            return resultList
                .OrderByDescending(x => x.ShouldOptimize)
                .ThenByDescending(x => x.IsService)
                .ThenByDescending(x => x.MemoryBytes)
                .ToList();
        }
    }
}