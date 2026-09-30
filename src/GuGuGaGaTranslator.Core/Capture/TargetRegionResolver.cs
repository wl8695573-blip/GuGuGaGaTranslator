using System.Windows;
using GuGuGaGaTranslator.Core.Config;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>Maps a saved client-relative selection to current physical pixels, including negative monitor coordinates.</summary>
public static class TargetRegionResolver
{
    public static Int32Rect? Resolve(RegionRect selection, Int32Rect client, int referenceWidth = 0, int referenceHeight = 0)
    {
        if (!selection.IsUsable || client.Width <= 0 || client.Height <= 0) return null;
        var scaleX = referenceWidth > 0 ? (double)client.Width / referenceWidth : 1;
        var scaleY = referenceHeight > 0 ? (double)client.Height / referenceHeight : 1;
        // Scale both edges rather than rounding an origin and a width independently.
        var left = Math.Clamp(Math.Round(selection.X * scaleX), 0, client.Width);
        var top = Math.Clamp(Math.Round(selection.Y * scaleY), 0, client.Height);
        var right = Math.Clamp(Math.Round(((double)selection.X + selection.Width) * scaleX), 0, client.Width);
        var bottom = Math.Clamp(Math.Round(((double)selection.Y + selection.Height) * scaleY), 0, client.Height);
        if (right <= left || bottom <= top) return null;
        return new Int32Rect(checked(client.X + (int)left), checked(client.Y + (int)top),
            (int)(right - left), (int)(bottom - top));
    }
}
