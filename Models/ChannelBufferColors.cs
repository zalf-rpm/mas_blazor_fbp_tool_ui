using MudBlazor;

namespace BlazorDrawFBP.Models;

public static class ChannelBufferColors
{
    public static Color For(double queueFillPercent)
    {
        return queueFillPercent switch
        {
            >= 90 => Color.Error,
            >= 65 => Color.Warning,
            _ => Color.Success,
        };
    }
}
