namespace BlazorDrawFBP.Tests;

using BlazorDrawFBP.Models;
using ProcessSchema = Mas.Schema.Fbp.Process;

[TestClass]
public class CapnpFbpPortColorsTests
{
    [TestMethod]
    public void ResolveLifecycleFrameColor_ReturnsExpectedPaletteForLifecycleStates()
    {
        Assert.AreEqual(CapnpFbpPortColors.TransitionColor, CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Starting));
        Assert.AreEqual(CapnpFbpPortColors.TransitionColor, CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Stopping));
        Assert.AreEqual(CapnpFbpPortColors.ReadyColor, CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Running));
        Assert.AreEqual(CapnpFbpPortColors.PendingColor, CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Failed));
        Assert.AreEqual(CapnpFbpPortColors.DefaultColor, CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Idle));
    }

    [TestMethod]
    public void ResolveActivityColor_ReturnsExpectedPaletteForActivityStates()
    {
        Assert.AreEqual(CapnpFbpPortColors.WaitingInputColor, CapnpFbpPortColors.ResolveActivityColor(ProcessSchema.ActivityState.waitingInput));
        Assert.AreEqual(CapnpFbpPortColors.ReadyColor, CapnpFbpPortColors.ResolveActivityColor(ProcessSchema.ActivityState.processing));
        Assert.AreEqual(CapnpFbpPortColors.WaitingOutputColor, CapnpFbpPortColors.ResolveActivityColor(ProcessSchema.ActivityState.waitingOutput));
        Assert.AreEqual(CapnpFbpPortColors.ClosingColor, CapnpFbpPortColors.ResolveActivityColor(ProcessSchema.ActivityState.closing));
        Assert.AreEqual(CapnpFbpPortColors.DefaultColor, CapnpFbpPortColors.ResolveActivityColor(ProcessSchema.ActivityState.none));
    }

    [TestMethod]
    public void ResolveActiveFrameColor_ReturnsReadyOrPendingColor()
    {
        Assert.AreEqual(CapnpFbpPortColors.ReadyColor, CapnpFbpPortColors.ResolveActiveFrameColor(true));
        Assert.AreEqual(CapnpFbpPortColors.PendingColor, CapnpFbpPortColors.ResolveActiveFrameColor(false));
    }
}
