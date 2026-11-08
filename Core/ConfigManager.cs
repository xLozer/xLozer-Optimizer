using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace xLozerOptimizer.Core
{
    public class UserSettings
    {
        // Lista de processos que o usuário clicou em "Ignorar Sempre" (Whitelist permanente)
        public HashSet<string> IgnoredProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Lembra se o usuário deixou cada switch marcado (true) ou desmarcado (false)
        public Dictionary<string, bool> SavedToggleStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Estado do Auto-RAM Cleaner
        public bool AutoCleanEnabled { get; set; } = true;
    }

    public static class ConfigManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "xLozerOptimizer");

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "user_settings.json");

        public static UserSettings Current { get; private set; } = new UserSettings();

        static ConfigManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                    if (loaded != null)
                    {
                        Current = new UserSettings
                        {
                            IgnoredProcesses = new HashSet<string>(
                                loaded.IgnoredProcesses ?? new HashSet<string>(),
                                StringComparer.OrdinalIgnoreCase),
                            SavedToggleStates = new Dictionary<string, bool>(
                                loaded.SavedToggleStates ?? new Dictionary<string, bool>(),
                                StringComparer.OrdinalIgnoreCase),
                            AutoCleanEnabled = loaded.AutoCleanEnabled
                        };
                    }
                }
            }
            catch
            {
                Current = new UserSettings();
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // Ignora falhas de permissão de escrita em disco
            }
        }

        public static void AddIgnoredProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;

            string cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (Current.IgnoredProcesses.Add(cleanName))
            {
                Save();
            }
        }

        public static void RemoveIgnoredProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;

            string cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (Current.IgnoredProcesses.Remove(cleanName))
            {
                Save();
            }
        }

        public static void SaveToggleState(string itemName, bool shouldOptimize)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            Current.SavedToggleStates[itemName] = shouldOptimize;
            Save();
        }
    }
}