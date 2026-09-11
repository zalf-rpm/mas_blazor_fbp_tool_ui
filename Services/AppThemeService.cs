namespace BlazorDrawFBP.Services;

using System;
using System.Threading.Tasks;
using Blazored.LocalStorage;

public class AppThemeService(ILocalStorageService? localStorage = null) : IAppThemeService
{
    public const string StorageKey = "mas-theme-dark-mode";

    public bool IsDarkMode { get; private set; }

    public event Action? ThemeChanged;

    public async Task InitializeAsync()
    {
        if (localStorage == null)
            return;

        try
        {
            if (await localStorage.ContainKeyAsync(StorageKey))
            {
                IsDarkMode = await localStorage.GetItemAsync<bool>(StorageKey);
                ThemeChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load theme preference: {ex.Message}");
        }
    }

    public async Task ToggleDarkModeAsync()
    {
        await SetDarkModeAsync(!IsDarkMode);
    }

    public async Task SetDarkModeAsync(bool isDark)
    {
        if (IsDarkMode == isDark)
            return;

        IsDarkMode = isDark;
        ThemeChanged?.Invoke();

        if (localStorage != null)
        {
            try
            {
                await localStorage.SetItemAsync(StorageKey, IsDarkMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save theme preference: {ex.Message}");
            }
        }
    }
}
