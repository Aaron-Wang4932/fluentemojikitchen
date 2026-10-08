using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FluentEmojiKitchen.Services.Windows
{
    public sealed class HotkeyRegistryService
    {
        private const string RootPath = @"SOFTWARE\Microsoft\Input\Settings";
        private const string ValueName = "EnableExpressiveInputShellHotkey";

        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluentEmojiKitchen");

        private static readonly string CacheFilePath =
            Path.Combine(AppDataFolder, "im-subkeys-cache.txt");

        private static readonly string BackupFilePath =
            Path.Combine(AppDataFolder, "previous-regvals-backup.json");

        private readonly Dictionary<string, int?> _previousValues = new();

        public bool IsDisabled { get; private set; }

        /// <summary>
        /// Call once at startup, before DisableOsHotkey. If a backup file exists,
        /// it means a previous run disabled the OS hotkey and never got to restore it
        /// (crash / hard kill) — so we restore from that leftover file right away.
        /// </summary>
        public void RecoverFromCrashIfNeeded()
        {
            if (!File.Exists(BackupFilePath)) return;

            var json = File.ReadAllText(BackupFilePath);
            var recovered = JsonSerializer.Deserialize<Dictionary<string, int?>>(json);
            if (recovered is null) return;

            foreach (var (path, previousValue) in recovered)
            {
                using var leafKey = Registry.LocalMachine.OpenSubKey(path, writable: true);
                if (leafKey is null) continue;

                if (previousValue is int value)
                    leafKey.SetValue(ValueName, value, RegistryValueKind.DWord);
                else
                    leafKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            File.Delete(BackupFilePath);
        }

        public void DisableOsHotkey()
        {
            if (IsDisabled) return;

            foreach (var leafPath in GetImSubkeyPaths())
            {
                using var leafKey = Registry.LocalMachine.OpenSubKey(leafPath, writable: true);
                if (leafKey is null) continue;

                var existing = leafKey.GetValue(ValueName) as int?;
                _previousValues[leafPath] = existing;

                leafKey.SetValue(ValueName, 0, RegistryValueKind.DWord);
            }

            WriteBackupFile();
            IsDisabled = true;
        }

        public void RestoreOsHotkey()
        {
            if (!IsDisabled) return;

            foreach (var (path, previousValue) in _previousValues)
            {
                using var leafKey = Registry.LocalMachine.OpenSubKey(path, writable: true);
                if (leafKey is null) continue;

                if (previousValue is int value)
                    leafKey.SetValue(ValueName, value, RegistryValueKind.DWord);
                else
                    leafKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            _previousValues.Clear();
            IsDisabled = false;

            if (File.Exists(BackupFilePath))
                File.Delete(BackupFilePath);
        }

        private void WriteBackupFile()
        {
            Directory.CreateDirectory(AppDataFolder);
            var json = JsonSerializer.Serialize(_previousValues);
            File.WriteAllText(BackupFilePath, json);
        }

        private List<string> GetImSubkeyPaths()
        {
            if (File.Exists(CacheFilePath))
                return File.ReadAllLines(CacheFilePath).ToList();

            using var root = Registry.LocalMachine.OpenSubKey(RootPath, writable: false);
            var paths = root is null ? new List<string>() : FindImSubkeys(root).ToList();

            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllLines(CacheFilePath, paths);

            return paths;
        }

        public static void InvalidateCache()
        {
            if (File.Exists(CacheFilePath))
                File.Delete(CacheFilePath);
        }

        private static IEnumerable<string> FindImSubkeys(RegistryKey procRoot)
        {
            foreach (var procName in procRoot.GetSubKeyNames())
            {
                using var procKey = procRoot.OpenSubKey(procName);
                if (procKey is null) continue;

                foreach (var locName in procKey.GetSubKeyNames())
                {
                    using var locKey = procKey.OpenSubKey(locName);
                    if (locKey is null) continue;

                    foreach (var imName in locKey.GetSubKeyNames())
                        yield return $@"{RootPath}\{procName}\{locName}\{imName}";
                }
            }
        }
    }
}
