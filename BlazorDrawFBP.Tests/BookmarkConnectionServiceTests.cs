using BlazorDrawFBP.Data;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class BookmarkConnectionServiceTests
{
    private const ulong Registry = 1;
    private FakeLocalStorageService _storage = null!;
    private FakeFbpRuntimeService _runtime = null!;
    private BookmarkConnectionService _service = null!;

    private static StoredSrData Bookmark(string sturdyRef, bool autoConnect) =>
        new()
        {
            InterfaceId = Registry,
            SturdyRef = sturdyRef,
            PetName = sturdyRef,
            AutoConnect = autoConnect,
        };

    [TestInitialize]
    public async Task Setup()
    {
        _storage = new FakeLocalStorageService();
        _runtime = new FakeFbpRuntimeService();
        _service = new BookmarkConnectionService(_storage);
        await StoredSrData.SaveAllData(
            _storage,
            [Bookmark("capnp://auto-1", true), Bookmark("capnp://auto-2", true), Bookmark("capnp://manual", false)]
        );
    }

    private IEnumerable<string> Connected => _runtime.ConnectBookmarkCalls.Select(c => c.SturdyRef).Order();

    [TestMethod]
    public async Task AutoConnect_ConnectsOnlyAutoConnectBookmarks()
    {
        await _service.AutoConnectAsync(_runtime);

        CollectionAssert.AreEqual(new[] { "capnp://auto-1", "capnp://auto-2" }, Connected.ToArray());
    }

    [TestMethod]
    public async Task AutoConnect_SkipsBookmarksThatAreAlreadyConnected()
    {
        _runtime.SturdyRef2Services["capnp://auto-1"] = null!;

        await _service.AutoConnectAsync(_runtime);

        CollectionAssert.AreEqual(new[] { "capnp://auto-2" }, Connected.ToArray());
    }

    [TestMethod]
    public async Task AutoConnect_DoesNotReconnectWhatTheUserDisconnected_UntilConnectedByHand()
    {
        var disconnected = Bookmark("capnp://auto-1", true);
        _service.Disconnect(_runtime, disconnected);
        Assert.AreEqual(1, _runtime.DisconnectBookmarkCalls.Count);

        // e.g. the user clicked "new flow": a new runtime gets its auto-connect services
        var newRuntime = new FakeFbpRuntimeService();
        await _service.AutoConnectAsync(newRuntime);
        CollectionAssert.AreEqual(
            new[] { "capnp://auto-2" },
            newRuntime.ConnectBookmarkCalls.Select(c => c.SturdyRef).ToArray()
        );

        // connecting by hand makes it a normal auto-connect bookmark again
        await _service.ConnectAsync(newRuntime, disconnected);
        var anotherRuntime = new FakeFbpRuntimeService();
        await _service.AutoConnectAsync(anotherRuntime);
        CollectionAssert.AreEqual(
            new[] { "capnp://auto-1", "capnp://auto-2" },
            anotherRuntime.ConnectBookmarkCalls.Select(c => c.SturdyRef).Order().ToArray()
        );
    }

    [TestMethod]
    public async Task AutoConnect_AfterForgettingTheManualDisconnect_ConnectsAgain()
    {
        var bookmark = Bookmark("capnp://auto-1", true);
        _service.Disconnect(_runtime, bookmark);
        _service.ForgetManualDisconnect(bookmark);

        await _service.AutoConnectAsync(_runtime);

        CollectionAssert.Contains(Connected.ToArray(), "capnp://auto-1");
    }
}
