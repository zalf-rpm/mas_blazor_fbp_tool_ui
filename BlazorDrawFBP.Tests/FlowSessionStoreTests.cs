namespace BlazorDrawFBP.Tests;

using System;
using System.Linq;
using BlazorDrawFBP.Services;
using Newtonsoft.Json.Linq;

[TestClass]
public class FlowSessionStoreTests
{
    private FlowSessionStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _store = new FlowSessionStore();
    }

    [TestMethod]
    public void GetOrCreateSession_CreatesNewSession_WithProvidedId()
    {
        var flowId = Guid.NewGuid();
        Assert.AreEqual(0, _store.SessionCount);

        var session = _store.GetOrCreateSession(flowId);

        Assert.IsNotNull(session);
        Assert.AreEqual(flowId, session.Id);
        Assert.AreEqual(1, _store.SessionCount);
        Assert.IsFalse(session.IsDetached);
        Assert.IsNull(session.DetachedAt);
        Assert.IsNotNull(session.RuntimeService);
        Assert.AreEqual(TimeSpan.FromMinutes(30), session.Ttl);
    }

    [TestMethod]
    public void GetOrCreateSession_ExistingSession_ReturnsSameInstanceAndReattaches()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        session.MarkDetached();
        Assert.IsTrue(session.IsDetached);
        Assert.IsNotNull(session.DetachedAt);

        var retrieved = _store.GetOrCreateSession(flowId);

        Assert.AreSame(session, retrieved);
        Assert.IsFalse(retrieved.IsDetached);
        Assert.IsNull(retrieved.DetachedAt);
        Assert.AreEqual(1, _store.SessionCount);
    }

    [TestMethod]
    public void TryGetSession_ReturnsTrueWhenExists_FalseWhenMissing()
    {
        var flowId = Guid.NewGuid();
        Assert.IsFalse(_store.TryGetSession(flowId, out var missing));
        Assert.IsNull(missing);

        _store.GetOrCreateSession(flowId);

        Assert.IsTrue(_store.TryGetSession(flowId, out var existing));
        Assert.IsNotNull(existing);
        Assert.AreEqual(flowId, existing.Id);
    }

    [TestMethod]
    public void RemoveSession_RemovesAndReturnsSession()
    {
        var flowId = Guid.NewGuid();
        _store.GetOrCreateSession(flowId);
        Assert.AreEqual(1, _store.SessionCount);

        var removed = _store.RemoveSession(flowId, out var session);

        Assert.IsTrue(removed);
        Assert.IsNotNull(session);
        Assert.AreEqual(flowId, session.Id);
        Assert.AreEqual(0, _store.SessionCount);
    }

    [TestMethod]
    public void GetAllSessions_ReturnsAllActiveSessions()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _store.GetOrCreateSession(id1);
        _store.GetOrCreateSession(id2);

        var all = _store.GetAllSessions();

        Assert.AreEqual(2, all.Count);
        Assert.IsTrue(all.Any(s => s.Id == id1));
        Assert.IsTrue(all.Any(s => s.Id == id2));
    }

    [TestMethod]
    public void FlowSession_FlowDocument_CanStoreAndRetrieveJson()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        Assert.IsNull(session.FlowDocument);

        var doc = new JObject
        {
            { "version", "0.3" },
            { "pan", new JObject { { "x", 10 }, { "y", 20 } } },
            { "zoom", 1.5 }
        };
        session.FlowDocument = doc;

        Assert.IsNotNull(session.FlowDocument);
        Assert.AreEqual("0.3", session.FlowDocument["version"]?.ToString());
        Assert.AreEqual(1.5, session.FlowDocument["zoom"]?.Value<double>());
    }

    [TestMethod]
    public void MarkAttached_And_MarkDetached_UpdatesState()
    {
        var flowId = Guid.NewGuid();
        _store.GetOrCreateSession(flowId);

        _store.MarkDetached(flowId);
        Assert.IsTrue(_store.TryGetSession(flowId, out var detachedSession));
        Assert.IsTrue(detachedSession.IsDetached);
        Assert.IsNotNull(detachedSession.DetachedAt);

        _store.MarkAttached(flowId);
        Assert.IsFalse(detachedSession.IsDetached);
        Assert.IsNull(detachedSession.DetachedAt);
    }

    [TestMethod]
    public void FlowSession_Diagram_PreservedAcrossReconnect()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        var diagram = new Blazor.Diagrams.BlazorDiagram();
        session.Diagram = diagram;

        // Simulate reconnecting to existing session
        var reconnected = _store.GetOrCreateSession(flowId);

        Assert.AreSame(session, reconnected);
        Assert.AreSame(diagram, reconnected.Diagram);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task FlowSession_DisposeAsync_CleansUpRuntimeService()
    {
        var flowId = Guid.NewGuid();
        var session = _store.GetOrCreateSession(flowId);
        Assert.IsNotNull(session.RuntimeService);

        await session.DisposeAsync();
        // Disposed cleanly without error
    }
}

