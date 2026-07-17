using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ZapretikApp
{
    /// <summary>
    /// Enable/disable app autostart via HKCU Run key.
    /// </summary>
    internal static class AutostartHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ZapretikApp";

        public static string GetExecutablePath()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch
            {
            }

            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Command line written to Run: exe + --tray (start hidden in tray).</summary>
        public static string GetAutostartCommand()
        {
            var exe = GetExecutablePath();
            if (string.IsNullOrEmpty(exe))
                return string.Empty;
            return "\"" + exe + "\" --tray";
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                        return false;
                    var value = key.GetValue(ValueName) as string;
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                    throw new InvalidOperationException("Не удалось открыть ключ автозапуска Windows.");

                if (enabled)
                {
                    var cmd = GetAutostartCommand();
                    if (string.IsNullOrEmpty(cmd))
                        throw new InvalidOperationException("Не удалось определить путь к ZapretikApp.exe.");
                    key.SetValue(ValueName, cmd);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
