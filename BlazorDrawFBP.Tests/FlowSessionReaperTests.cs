namespace BlazorDrawFBP.Tests;

using System;
using System.Threading.Tasks;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class FlowSessionReaperTests
{
    private FlowSessionStore _store = null!;
    private FlowSessionReaper _reaper = null!;

    [TestInitialize]
    public void Setup()
    {
        _store = new FlowSessionStore();
        _reaper = new FlowSessionReaper(
            _store,
            NullLogger<FlowSessionReaper>.Instance,
            TimeSpan.FromSeconds(1)
        );
    }

    [TestMethod]
    public async Task ReapExpiredSessionsAsync_DoesNotReap_ActiveAttachedSessions()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        Assert.IsFalse(session.IsDetached);

        var futureTime = DateTime.UtcNow.AddHours(2);
        var reapedCount = await _reaper.ReapExpiredSessionsAsync(futureTime);

        Assert.AreEqual(0, reapedCount);
        Assert.AreEqual(1, _store.SessionCount);
        Assert.IsTrue(_store.TryGetSession(flowId, out _));
    }

    [TestMethod]
    public async Task ReapExpiredSessionsAsync_DoesNotReap_DetachedSessionWithinTtl()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        session.Ttl = TimeSpan.FromMinutes(30);
        session.MarkDetached();

        var timeWithinTtl = session.DetachedAt!.Value.AddMinutes(15);
        var reapedCount = await _reaper.ReapExpiredSessionsAsync(timeWithinTtl);

        Assert.AreEqual(0, reapedCount);
        Assert.AreEqual(1, _store.SessionCount);
        Assert.IsTrue(_store.TryGetSession(flowId, out _));
    }

    [TestMethod]
    public async Task ReapExpiredSessionsAsync_ReapsDetachedSession_WhenTtlExceeded()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        session.Ttl = TimeSpan.FromMinutes(30);
        session.MarkDetached();

        var timePastTtl = session.DetachedAt!.Value.AddMinutes(31);
        var reapedCount = await _reaper.ReapExpiredSessionsAsync(timePastTtl);

        Assert.AreEqual(1, reapedCount);
        Assert.AreEqual(0, _store.SessionCount);
        Assert.IsFalse(_store.TryGetSession(flowId, out _));
    }

    [TestMethod]
    public async Task ReapExpiredSessionsAsync_DoesNotReap_IfFlowIsExecuting()
    {
        var flowId = Guid.NewGuid();
        var fakeRuntime = new FakeFbpRuntimeService { IsExecutingFlow = true };
        var session = _store.GetOrCreateSession(flowId, () => fakeRuntime);
        session.Ttl = TimeSpan.FromMinutes(30);
        session.MarkDetached();

        var timePastTtl = session.DetachedAt!.Value.AddMinutes(60);
        var reapedCount = await _reaper.ReapExpiredSessionsAsync(timePastTtl);

        Assert.AreEqual(0, reapedCount);
        Assert.AreEqual(1, _store.SessionCount);
        Assert.IsTrue(_store.TryGetSession(flowId, out _));
    }
}
