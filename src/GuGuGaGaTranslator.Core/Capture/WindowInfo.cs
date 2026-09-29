using System.Windows;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>One top-level window the user can choose as a translation target. Geometry is in physical
/// screen pixels, captured from a DPI-aware process, so it can be handed straight to a screen capture.</summary>
public sealed record WindowInfo
{
    /// <summary>The window handle; it changes every launch, so it is never persisted.</summary>
    public required nint Handle { get; init; }

    public required string Title { get; init; }

    public required string ClassName { get; init; }

    public required uint ProcessId { get; init; }

    public required string ProcessName { get; init; }

    /// <summary>The client area in screen coordinates: no title bar, no border, which is where a game draws its dialogue.</summary>
    public required Int32Rect ClientRect { get; init; }

    public required Int32Rect FrameRect { get; init; }

    /// <summary>True while the window is minimized, which makes it uncapturable.</summary>
    public required bool IsMinimized { get; init; }

    public required bool IsOwned { get; init; }

    /// <summary>A restart-stable identity: a process name plus window class names the same target window across launches.</summary>
    public string Identity => $"{ProcessName}|{ClassName}";

    public bool HasClientArea => ClientRect.Width > 0 && ClientRect.Height > 0;

    public string Display =>
        $"{Title} — {ProcessName} · client {ClientRect.Width}×{ClientRect.Height} @ {ClientRect.X},{ClientRect.Y}";
}
