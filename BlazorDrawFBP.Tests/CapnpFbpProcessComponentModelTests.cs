using BlazorDrawFBP.Models;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Tests;

using ProcessSchema = Process;

[TestClass]
public class CapnpFbpProcessComponentModelTests
{
    [TestMethod]
    public void ProcessComponentModel_InitialState_DefaultsAreExpected()
    {
        var node = new CapnpFbpProcessComponentModel();

        Assert.IsFalse(node.RemoteProcessAttached());
        Assert.IsFalse(node.CanEditCommandLine());
        Assert.IsFalse(node.SupportsLivePortChanges);
        Assert.AreEqual(Process.ActivityState.none, node.ActivityState);
        Assert.AreEqual("", node.ActivityPortName);
        Assert.AreEqual("None", node.ActivitySummary);
        Assert.IsFalse(node.HasLastRunInfo);
        Assert.AreEqual(Process.RunInfo.Outcome.none, node.LastRunOutcome);
        Assert.AreEqual("", node.LastRunSummary);
        Assert.IsFalse(node.IsProcessingActivity);
    }

    [TestMethod]
    public void FormatActivitySummary_MapsAllActivityStatesCorrectly()
    {
        Assert.AreEqual(
            "None",
            CapnpFbpProcessComponentModel.FormatActivitySummary(Process.ActivityState.none, "")
        );
        Assert.AreEqual(
            "Processing",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.processing,
                ""
            )
        );
        Assert.AreEqual(
            "Processing",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.processing,
                "in1"
            )
        );
        Assert.AreEqual(
            "Closing",
            CapnpFbpProcessComponentModel.FormatActivitySummary(Process.ActivityState.closing, "")
        );

        Assert.AreEqual(
            "Waiting input on dataIn",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.waitingInput,
                "dataIn"
            )
        );
        Assert.AreEqual(
            "Waiting input",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.waitingInput,
                ""
            )
        );

        Assert.AreEqual(
            "Waiting output on dataOut",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.waitingOutput,
                "dataOut"
            )
        );
        Assert.AreEqual(
            "Waiting output",
            CapnpFbpProcessComponentModel.FormatActivitySummary(
                Process.ActivityState.waitingOutput,
                ""
            )
        );
    }

    [TestMethod]
    public void FormatLastRunSummary_WhenNull_ReturnsEmptyString()
    {
        var summary = CapnpFbpProcessComponentModel.FormatLastRunSummary(null);
        Assert.AreEqual("", summary);
    }

    [TestMethod]
    public void FormatLastRunSummary_CompletedRun_FormatsOutcomeAndPhase()
    {
        var runInfo = new Process.RunInfo
        {
            TheOutcome = Process.RunInfo.Outcome.completed,
            ThePhase = Process.RunInfo.Phase.run,
        };

        var summary = CapnpFbpProcessComponentModel.FormatLastRunSummary(runInfo);
        Assert.AreEqual("Completed during run", summary);
    }

    [TestMethod]
    public void FormatLastRunSummary_FailedRun_IncludesPortAndMessage()
    {
        var runInfo = new Process.RunInfo
        {
            TheOutcome = Process.RunInfo.Outcome.failed,
            ThePhase = Process.RunInfo.Phase.run,
            Port = "inPort",
            Message = "Unexpected end of stream",
            DetailType = "IOException",
        };

        var summary = CapnpFbpProcessComponentModel.FormatLastRunSummary(runInfo);
        Assert.AreEqual(
            "Failed during run on inPort: IOException: Unexpected end of stream",
            summary
        );
    }

    [TestMethod]
    public void BuildLastRunDetailLines_IncludesHeadingDetailAndTraceback()
    {
        var runInfo = new Process.RunInfo
        {
            TheOutcome = Process.RunInfo.Outcome.failed,
            ThePhase = Process.RunInfo.Phase.run,
            Message = "Null pointer encountered",
            DetailType = "NullReferenceException",
            Traceback = new List<string> { "at Server.Process()", "at Runner.Execute()" },
        };

        var lines = CapnpFbpProcessComponentModel.BuildLastRunDetailLines(
            runInfo,
            true,
            false,
            true
        );

        Assert.IsTrue(lines.Count >= 4);
        StringAssert.Contains(
            lines[0],
            "Last run: Failed during run: NullReferenceException: Null pointer encountered"
        );
        Assert.IsTrue(lines.Contains("Traceback:"));
        Assert.IsTrue(lines.Contains("  at Server.Process()"));
        Assert.IsTrue(lines.Contains("  at Runner.Execute()"));
    }
}
