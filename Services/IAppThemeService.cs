namespace BlazorDrawFBP.Services;

using System;
using System.Threading.Tasks;

public interface IAppThemeService
{
    bool IsDarkMode { get; }
    event Action? ThemeChanged;

    Task InitializeAsync();
    Task ToggleDarkModeAsync();
    Task SetDarkModeAsync(bool isDark);
}
