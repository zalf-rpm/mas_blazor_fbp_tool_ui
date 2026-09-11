namespace BlazorDrawFBP.Tests;

using System.Threading.Tasks;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;

[TestClass]
public class AppThemeServiceTests
{
    [TestMethod]
    public void AppThemeService_DefaultsToLightMode()
    {
        var themeService = new AppThemeService();
        Assert.IsFalse(themeService.IsDarkMode);
    }

    [TestMethod]
    public async Task AppThemeService_SetDarkMode_UpdatesStateAndNotifiesListeners()
    {
        var fakeStorage = new FakeLocalStorageService();
        var themeService = new AppThemeService(fakeStorage);
        var changeCount = 0;
        themeService.ThemeChanged += () => changeCount++;

        await themeService.SetDarkModeAsync(true);

        Assert.IsTrue(themeService.IsDarkMode);
        Assert.AreEqual(1, changeCount);

        var saved = await fakeStorage.GetItemAsync<bool>(AppThemeService.StorageKey);
        Assert.IsTrue(saved);

        // Setting same state should be a no-op and not notify again
        await themeService.SetDarkModeAsync(true);
        Assert.AreEqual(1, changeCount);
    }

    [TestMethod]
    public async Task AppThemeService_ToggleDarkMode_InvertsState()
    {
        var fakeStorage = new FakeLocalStorageService();
        var themeService = new AppThemeService(fakeStorage);
        var changeCount = 0;
        themeService.ThemeChanged += () => changeCount++;

        await themeService.ToggleDarkModeAsync();
        Assert.IsTrue(themeService.IsDarkMode);
        Assert.AreEqual(1, changeCount);

        await themeService.ToggleDarkModeAsync();
        Assert.IsFalse(themeService.IsDarkMode);
        Assert.AreEqual(2, changeCount);
    }

    [TestMethod]
    public async Task AppThemeService_InitializeAsync_RestoresPreferenceFromStorage()
    {
        var fakeStorage = new FakeLocalStorageService();
        await fakeStorage.SetItemAsync(AppThemeService.StorageKey, true);

        var themeService = new AppThemeService(fakeStorage);
        var notified = false;
        themeService.ThemeChanged += () => notified = true;

        await themeService.InitializeAsync();

        Assert.IsTrue(themeService.IsDarkMode);
        Assert.IsTrue(notified);
    }
}
