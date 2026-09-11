using Blazored.LocalStorage;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Tests.TestDoubles;

public class FakeRegistryService : IRegistry
{
    public bool ShouldFailPing { get; set; }
    public bool IsDisposed { get; private set; }
    public string ServiceId { get; set; } = "fake-registry-id";

    public Task<IdInformation> Info(CancellationToken cancellationToken_ = default)
    {
        if (ShouldFailPing)
            throw new InvalidOperationException("Registry service unreachable");
        return Task.FromResult(new IdInformation { Id = ServiceId, Name = "Fake Registry" });
    }

    public Task<IReadOnlyList<IdInformation>> SupportedCategories(
        CancellationToken cancellationToken_ = default
    )
    {
        return Task.FromResult<IReadOnlyList<IdInformation>>([]);
    }

    public Task<IdInformation> CategoryInfo(
        string categoryId,
        CancellationToken cancellationToken_ = default
    )
    {
        return Task.FromResult(new IdInformation { Id = categoryId });
    }

    public Task<IReadOnlyList<Registry.Entry>> Entries(
        string categoryId,
        CancellationToken cancellationToken_ = default
    )
    {
        return Task.FromResult<IReadOnlyList<Registry.Entry>>([]);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public class FakeStartChannelsService : IStartChannelsService
{
    public bool ShouldFailPing { get; set; }
    public bool IsDisposed { get; private set; }
    public string ServiceId { get; set; } = "fake-chan-id";

    public Task<IdInformation> Info(CancellationToken cancellationToken_ = default)
    {
        if (ShouldFailPing)
            throw new InvalidOperationException("Channel starter service unreachable");
        return Task.FromResult(new IdInformation { Id = ServiceId, Name = "Fake Channel Starter" });
    }

    public Task<(IReadOnlyList<Channel<object>.StartupInfo>, IStoppable)> Start(
        StartChannelsService.Params arg_,
        CancellationToken cancellationToken_ = default
    )
    {
        return Task.FromResult<(IReadOnlyList<Channel<object>.StartupInfo>, IStoppable)>(
            ([], null!)
        );
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public class FakeLocalStorageService : ILocalStorageService
{
    private readonly Dictionary<string, object> _store = new();

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<T?> GetItemAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var val) && val is T typed)
            return ValueTask.FromResult<T?>(typed);
        return ValueTask.FromResult<T?>(default);
    }

    public ValueTask SetItemAsync<T>(
        string key,
        T data,
        CancellationToken cancellationToken = default
    )
    {
        if (data != null)
            _store[key] = data;
        else
            _store.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ContainKeyAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_store.ContainsKey(key));
    }

    public ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetItemAsStringAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_store.TryGetValue(key, out var val) ? val?.ToString() : null);
    }

    public ValueTask SetItemAsStringAsync(
        string key,
        string data,
        CancellationToken cancellationToken = default
    )
    {
        _store[key] = data;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> KeyAsync(int index, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<string?>(null);
    }

    public ValueTask<IEnumerable<string>> KeysAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IEnumerable<string>>(_store.Keys);
    }

    public ValueTask<int> LengthAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_store.Count);
    }

    public ValueTask RemoveItemsAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var k in keys)
            _store.Remove(k);
        return ValueTask.CompletedTask;
    }

    public event EventHandler<ChangingEventArgs>? Changing
    {
        add { }
        remove { }
    }

    public event EventHandler<ChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}
