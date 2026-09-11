using BlazorDrawFBP.Models;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Tests;

using ProcessSchema = Process;

[TestClass]
public class CapnpFbpPortColorsTests
{
    [TestMethod]
    public void ResolveLifecycleFrameColor_ReturnsExpectedPaletteForLifecycleStates()
    {
        Assert.AreEqual(
            CapnpFbpPortColors.TransitionColor,
            CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Starting)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.TransitionColor,
            CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Stopping)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.ReadyColor,
            CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Running)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.PendingColor,
            CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Failed)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.DefaultColor,
            CapnpFbpPortColors.ResolveLifecycleFrameColor(ComponentLifecycleState.Idle)
        );
    }

    [TestMethod]
    public void ResolveActivityColor_ReturnsExpectedPaletteForActivityStates()
    {
        Assert.AreEqual(
            CapnpFbpPortColors.WaitingInputColor,
            CapnpFbpPortColors.ResolveActivityColor(Process.ActivityState.waitingInput)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.ReadyColor,
            CapnpFbpPortColors.ResolveActivityColor(Process.ActivityState.processing)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.WaitingOutputColor,
            CapnpFbpPortColors.ResolveActivityColor(Process.ActivityState.waitingOutput)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.ClosingColor,
            CapnpFbpPortColors.ResolveActivityColor(Process.ActivityState.closing)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.DefaultColor,
            CapnpFbpPortColors.ResolveActivityColor(Process.ActivityState.none)
        );
    }

    [TestMethod]
    public void ResolveActiveFrameColor_ReturnsReadyOrPendingColor()
    {
        Assert.AreEqual(
            CapnpFbpPortColors.ReadyColor,
            CapnpFbpPortColors.ResolveActiveFrameColor(true)
        );
        Assert.AreEqual(
            CapnpFbpPortColors.PendingColor,
            CapnpFbpPortColors.ResolveActiveFrameColor(false)
        );
    }
}
