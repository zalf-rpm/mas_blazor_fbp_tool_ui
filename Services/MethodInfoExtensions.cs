using System.Reflection;

namespace BlazorDrawFBP.Services;

public static class MethodInfoExtensions
{
    public static async Task<object> InvokeAsync(
        this MethodInfo @this,
        object obj,
        params object[] parameters
    )
    {
        if (@this.Invoke(obj, parameters) is not Task task)
            return Task.CompletedTask;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty == null)
            return Task.CompletedTask;
        var res = resultProperty.GetValue(task);
        return res ?? Task.CompletedTask;
    }
}
