namespace BlazorDrawFBP.Services;

using System;
using Newtonsoft.Json.Linq;

public class FlowSession
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DetachedAt { get; set; }
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    public JObject? FlowDocument { get; set; }
    public IFbpRuntimeService RuntimeService { get; set; } = null!;

    public bool IsDetached => DetachedAt != null;

    public void Touch() => LastAccessedAt = DateTime.UtcNow;

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
}
