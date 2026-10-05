using System.Collections.Concurrent;
using BlazorDrawFBP.Data;
using Blazored.LocalStorage;

namespace BlazorDrawFBP.Services;

/// <summary>
/// Connects and disconnects stored sturdy-ref bookmarks on a flow session's runtime and decides
/// which of them get connected automatically.
/// </summary>
public interface IBookmarkConnectionService
{
    /// <summary>
    /// Connects every auto-connect bookmark that is not connected yet, except those the user
    /// disconnected on purpose earlier. Never throws; unreachable services are just skipped.
    /// </summary>
    Task AutoConnectAsync(IFbpRuntimeService runtime);

    /// <summary>Connects a bookmark on request of the user (also re-enables auto-connect for it).</summary>
    Task<bool> ConnectAsync(IFbpRuntimeService runtime, StoredSrData bookmark);

    /// <summary>Disconnects a bookmark on request of the user (it won't be auto-connected again).</summary>
    void Disconnect(IFbpRuntimeService runtime, StoredSrData bookmark);

    /// <summary>Forgets that the user disconnected this bookmark, e.g. after it was edited or deleted.</summary>
    void ForgetManualDisconnect(StoredSrData bookmark);
}

/// <remarks>
/// Registered scoped, i.e. one instance per browser circuit: a disconnect the user did stays
/// respected across new flows and terminated sessions, and resets with a page reload.
/// </remarks>
public class BookmarkConnectionService(ILocalStorageService localStorage)
    : IBookmarkConnectionService
{
    private readonly ConcurrentDictionary<(ulong InterfaceId, string SturdyRef), byte> _manuallyDisconnected =
        new();

    public async Task AutoConnectAsync(IFbpRuntimeService runtime)
    {
        List<StoredSrData> bookmarks;
        try
        {
            bookmarks = await StoredSrData.GetAllData(localStorage);
        }
        catch (Exception ex)
        {
            // e.g. JS interop not available yet (prerendering) or the circuit is already gone
            Console.WriteLine($"Auto-connect skipped, couldn't read bookmarks: {ex.Message}");
            return;
        }

        bookmarks.Sort();
        // one after the other: the runtime's service tables are not built for parallel updates
        foreach (var bookmark in bookmarks.Where(b => ShouldAutoConnect(runtime, b)))
            await runtime.ConnectBookmarkAsync(
                bookmark.InterfaceId,
                bookmark.PetName,
                bookmark.SturdyRef
            );
    }

    public Task<bool> ConnectAsync(IFbpRuntimeService runtime, StoredSrData bookmark)
    {
        ForgetManualDisconnect(bookmark);
        return runtime.ConnectBookmarkAsync(
            bookmark.InterfaceId,
            bookmark.PetName,
            bookmark.SturdyRef
        );
    }

    public void Disconnect(IFbpRuntimeService runtime, StoredSrData bookmark)
    {
        _manuallyDisconnected[Key(bookmark)] = 0;
        runtime.DisconnectBookmark(bookmark.InterfaceId, bookmark.SturdyRef);
    }

    public void ForgetManualDisconnect(StoredSrData bookmark)
    {
        _manuallyDisconnected.TryRemove(Key(bookmark), out _);
    }

    private bool ShouldAutoConnect(IFbpRuntimeService runtime, StoredSrData bookmark)
    {
        return bookmark.AutoConnect
            && !string.IsNullOrWhiteSpace(bookmark.SturdyRef)
            && !_manuallyDisconnected.ContainsKey(Key(bookmark))
            && !runtime.IsConnected(bookmark.SturdyRef);
    }

    private static (ulong, string) Key(StoredSrData bookmark)
    {
        return (bookmark.InterfaceId, bookmark.SturdyRef);
    }
}
