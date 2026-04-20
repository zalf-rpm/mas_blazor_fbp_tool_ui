// Register as Scoped in DI

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorDrawFBP.Services;

public class BlazorDispatcher
{
    private SynchronizationContext? _context;

    public void Capture() => _context = SynchronizationContext.Current;

    public Task InvokeAsync(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        _context.Post(
            _ =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            },
            null
        );
        return tcs.Task;
    }
}
