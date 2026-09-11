using BlazorDrawFBP.Services;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class CircuitHandlerTests
{
    [TestMethod]
    public async Task CleanupDiagramService_WhenActionRegistered_ExecutesCleanup()
    {
        var cleanupService = new CleanupDiagramService();
        var wasCalled = false;

        cleanupService.RegisterCleanup(() =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        });

        await cleanupService.Cleanup();

        Assert.IsTrue(wasCalled, "Cleanup action was not executed.");
    }

    [TestMethod]
    public async Task CleanupDiagramService_WhenUnregistered_DoesNotExecuteCleanup()
    {
        var cleanupService = new CleanupDiagramService();
        var wasCalled = false;

        cleanupService.RegisterCleanup(() =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        });

        cleanupService.UnregisterCleanup();
        await cleanupService.Cleanup();

        Assert.IsFalse(wasCalled, "Cleanup action should not execute after being unregistered.");
    }

    [TestMethod]
    public async Task CleanupDiagramService_WithoutRegistration_DoesNotThrow()
    {
        var cleanupService = new CleanupDiagramService();
        await cleanupService.Cleanup();
    }
}
