using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace LensDrop
{
    public static class ThemeManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LensDrop",
            "theme.txt"
        );

        public static string CurrentTheme { get; private set; } = "System";

        public static void Initialize()
        {
            string saved = "System";
            try
            {
                if (File.Exists(ConfigPath))
                    saved = File.ReadAllText(ConfigPath).Trim();
            }
            catch { }

            ApplyTheme(saved, savePreference: false);
        }

        public static void ApplyTheme(string themeMode, bool savePreference = true)
        {
            CurrentTheme = themeMode;
            string resolvedTheme = themeMode == "System" ? GetWindowsTheme() : themeMode;

            string themeUri = resolvedTheme == "Dark"
                ? "Themes/DarkTheme.xaml"
                : "Themes/LightTheme.xaml";

            var newDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) };

            var existingTheme = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));

            if (existingTheme != null)
                Application.Current.Resources.MergedDictionaries.Remove(existingTheme);

            Application.Current.Resources.MergedDictionaries.Insert(0, newDict);

            if (savePreference)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                    File.WriteAllText(ConfigPath, themeMode);
                }
                catch { }
            }
        }

        private static string GetWindowsTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int useLight)
                    return useLight == 0 ? "Dark" : "Light";
            }
            catch { }
            return "Light";
        }
    }
}