using Blazor.Diagrams;
using Newtonsoft.Json.Linq;

namespace BlazorDrawFBP.Services;

public class FlowSession : IAsyncDisposable
{
    public static readonly TimeSpan[] SupportedTtls =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(24),
    ];

    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DetachedAt { get; set; }
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    public JObject? FlowDocument { get; set; }
    public IFbpRuntimeService RuntimeService { get; set; } = null!;
    public BlazorDiagram? Diagram { get; set; }

    public bool IsDetached => DetachedAt != null;

    public async ValueTask DisposeAsync()
    {
        if (RuntimeService is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
        else
            await RuntimeService.ClearDiagramAsync();
    }

    public void Touch()
    {
        LastAccessedAt = DateTime.UtcNow;
    }

    public void MarkAttached()
    {
        DetachedAt = null;
        Touch();
    }

    public void MarkDetached()
    {
        DetachedAt = DateTime.UtcNow;
        Touch();
    }

    public static string FormatTtl(TimeSpan ttl)
    {
        if (ttl == TimeSpan.FromHours(24))
            return "24h";
        if (ttl.TotalDays >= 1 && ttl.TotalHours % 24 == 0)
            return $"{(int)ttl.TotalDays}d";
        if (ttl.TotalHours >= 1 && ttl.Minutes == 0)
            return $"{(int)ttl.TotalHours}h";
        if (ttl.TotalMinutes >= 1 && ttl.Seconds == 0)
            return $"{(int)ttl.TotalMinutes}m";
        return ttl.ToString();
    }

    public TimeSpan CycleTtl()
    {
        var currentIndex = -1;
        for (var i = 0; i < SupportedTtls.Length; i++)
            if (SupportedTtls[i] == Ttl)
            {
                currentIndex = i;
                break;
            }

        var nextIndex = (currentIndex + 1) % SupportedTtls.Length;
        Ttl = SupportedTtls[nextIndex];
        Touch();
        return Ttl;
    }
}
