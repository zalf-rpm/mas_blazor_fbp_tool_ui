namespace BlazorDrawFBP.Services;

public interface IAppThemeService
{
    bool IsDarkMode { get; }
    event Action? ThemeChanged;

    Task InitializeAsync();
    Task ToggleDarkModeAsync();
    Task SetDarkModeAsync(bool isDark);
}
