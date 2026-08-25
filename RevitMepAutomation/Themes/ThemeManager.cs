using System;
using System.Linq;
using System.Windows;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Themes
{
    public static class ThemeManager
    {
        private static AppThemeMode _currentTheme = AppThemeMode.Dark;

        public static AppThemeMode CurrentTheme => _currentTheme;

        public static event Action<AppThemeMode>? ThemeChanged;

        public static void SetTheme(AppThemeMode theme)
        {
            _currentTheme = theme;
            ApplyTheme(theme);
            ThemeChanged?.Invoke(theme);
        }

        public static void ToggleTheme()
        {
            SetTheme(_currentTheme == AppThemeMode.Dark ? AppThemeMode.Light : AppThemeMode.Dark);
        }

        public static void ApplyTheme(AppThemeMode theme)
        {
            var app = Application.Current;
            if (app == null) return;

            string themeUri = theme == AppThemeMode.Dark
                ? "pack://application:,,,/RevitMepAutomation;component/Themes/DarkTheme.xaml"
                : "pack://application:,,,/RevitMepAutomation;component/Themes/LightTheme.xaml";

            try
            {
                var newDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.RelativeOrAbsolute) };

                // Find existing theme dictionary if any
                var existingDict = app.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && 
                        (d.Source.OriginalString.Contains("DarkTheme.xaml") || d.Source.OriginalString.Contains("LightTheme.xaml")));

                if (existingDict != null)
                {
                    int index = app.Resources.MergedDictionaries.IndexOf(existingDict);
                    app.Resources.MergedDictionaries[index] = newDict;
                }
                else
                {
                    app.Resources.MergedDictionaries.Add(newDict);
                }
            }
            catch (Exception)
            {
                // Fallback for isolated UserControl hosts
            }
        }
    }
}
