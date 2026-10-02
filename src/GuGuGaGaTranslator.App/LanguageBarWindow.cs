using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Pipeline;

namespace GuGuGaGaTranslator.App;

/// <summary>Control bar shown above the translation overlay.</summary>
public sealed class LanguageBarWindow : Window
{
    /// <summary>The physical-pixel gap kept between the bar and the panel below it.</summary>
    private const int Gap = 6;

    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private readonly Border _panel;
    private readonly List<(string Key, Border Chrome, TextBlock Label)> _toggles = [];
    private readonly Border _primary;
    private readonly Border _profile;

    private nint _handle;
    private Int32Rect _anchor; private bool _editing;
    private bool _showSource;
    private bool _showPanel = true;
    private LanguagePreset? _primaryPreset;
    private bool _allowClose;
    private bool _closePending;
    private readonly Thumb _dragHandle = new()
    {
        Width = 24,
        Height = 26,
        Cursor = Cursors.SizeAll,
        Background = Brushes.Transparent,
        ToolTip = "拖动控制条和翻译框",
    };

    /// <summary>Whether this bar stays out of screen capture; set by the overlay from the configuration.</summary>
    public bool CaptureExcluded { get; set; } = true;

    /// <summary>Follow the configuration's capture switch while the tool is running, so unchecking it is
    /// enough to start recording the bar — no restart.</summary>
    public void ApplyCaptureExclusion(bool exclude)
    {
        CaptureExcluded = exclude;
        if (_handle != 0)
            OverlayWindowInterop.ApplyDisplayAffinity(_handle, exclude);
    }

    public LanguageBarWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        _panel = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Brush("OverlayBorderColor", (byte)0x99, Color.FromRgb(0x6E, 0x8B, 0xD6)),
            Background = Theme.Brush("OverlayPanelColor", 0xE6, Color.FromRgb(0x10, 0x12, 0x1A)),
            Child = _buttons,
        };

        _primary = MakeButton("…", onClick: () => { if (_primaryPreset is not null) PresetSelected?.Invoke(_primaryPreset); });
        _primary.Background = Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6));

        _profile = MakeButton("通用", onClick: () => ProfileRequested?.Invoke());

        var gripText = new FrameworkElementFactory(typeof(TextBlock));
        gripText.SetValue(TextBlock.TextProperty, "⠿");
        gripText.SetValue(TextBlock.FontSizeProperty, 22.0);
        gripText.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        gripText.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        gripText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        _dragHandle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripText };
        _dragHandle.DragDelta += (_, e) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            DragRequested?.Invoke((int)Math.Round(e.HorizontalChange * dpi.DpiScaleX),
                (int)Math.Round(e.VerticalChange * dpi.DpiScaleY));
        };
        _dragHandle.DragCompleted += (_, _) => DragFinished?.Invoke();

        Content = _panel;
    }

    public event Action<LanguagePreset>? PresetSelected;

    public event Action? ProfileRequested;

    public event Action? MoreRequested;
    public event Action<int, int>? DragRequested;
    public event Action? DragFinished;
    public event Action? CloseRequested;

    /// <summary>Raised when a toggle is pressed: <c>edit</c>, <c>source</c>, or <c>panel</c>.</summary>
    public event Action<string>? ToggleRequested;

    /// <summary>Where the bar is right now, in physical pixels, for a popup to hang from.</summary>
    public Int32Rect ScreenBounds
    {
        get; private set;
    }

    /// <summary>Rebuild the bar for the current direction and state.</summary>
    public void Configure(
        IReadOnlyList<LanguagePreset> presets,
        LanguagePair current,
        bool editing,
        bool showSource,
        bool showPanel,
        string? profile = null)
    {
        _editing = editing;
        _showSource = showSource;
        _showPanel = showPanel;

        _buttons.Children.Clear();
        _toggles.Clear();
        _buttons.Children.Add(_dragHandle);

        _primaryPreset = presets.FirstOrDefault(preset => Matches(preset, current)) ?? presets.FirstOrDefault();
        ((TextBlock)_primary.Child).Text = _primaryPreset?.Label ?? "语言";
        _buttons.Children.Add(_primary);

        SetProfileLabel(profile);
        _buttons.Children.Add(_profile);

        if (presets.Count > 1)
        {
            var more = MakeButton("其他 ▾", onClick: () => MoreRequested?.Invoke());
            _buttons.Children.Add(more);
        }

        _buttons.Children.Add(MakeSeparator());

        _buttons.Children.Add(MakeToggle("edit", editing ? "编辑中" : "编辑", editing));
        _buttons.Children.Add(MakeToggle("source", "原文", showSource));
        _buttons.Children.Add(MakeToggle("panel", "翻译框", showPanel));
        var close = MakeButton("×", () => CloseRequested?.Invoke());
        close.ToolTip = "关闭悬浮层并停止翻译";
        _buttons.Children.Add(close);

        RefreshToggles();
    }

    /// <summary>Reflect a new direction or state without rebuilding.</summary>
    public void Update(LanguagePair current, bool editing, bool showSource, bool showPanel, string? profile = null)
    {
        _editing = editing;
        _showSource = showSource;
        _showPanel = showPanel;
        SetProfileLabel(profile);
        RefreshToggles();
    }

    /// <summary>Label the profile button with the game it stands for; a long name is cut short rather than pushing the toggles off the bar.</summary>
    private void SetProfileLabel(string? profile)
    {
        var named = !string.IsNullOrWhiteSpace(profile);
        var label = named ? profile!.Trim() : "通用翻译";
        if (label.Length > 10)
            label = label[..10] + "…";
        ((TextBlock)_profile.Child).Text = named ? label : "通用";
    }

    private void RefreshToggles()
    {
        foreach (var (key, chrome, label) in _toggles)
        {
            var active = key switch
            {
                "edit" => _editing,
                "source" => _showSource,
                _ => _showPanel,
            };
            chrome.Background = active ? Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6)) : Brushes.Transparent;
            label.Text = key switch
            {
                "edit" => _editing ? "编辑中" : "编辑",
                "source" => "原文",
                _ => "翻译框",
            };
            label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>Place the bar just above an overlay anchored at a region; both edges are physical pixels, so the bar's device-independent size is converted here.</summary>
    public void PlaceAbove(Int32Rect region, int overlayTop)
    {
        _anchor = region;
        if (_handle == 0)
            return;

        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var height = (int)Math.Ceiling((ActualHeight > 0 ? ActualHeight : 30) * Math.Max(0.1, dpi.DpiScaleY));
        var width = (int)Math.Ceiling((ActualWidth > 0 ? ActualWidth : 300) * Math.Max(0.1, dpi.DpiScaleX));

        var area = OverlayWindowInterop.WorkAreaAt(region);
        var x = Math.Clamp(region.X, area.X, Math.Max(area.X, area.X + area.Width - width));
        var y = overlayTop - height - Gap;
        if (y < area.Y)
            y = overlayTop + Gap;
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        OverlayWindowInterop.MoveTo(_handle, x, y, topmost: true);
        ScreenBounds = new Int32Rect(x, y, width, height);
    }

    private static bool Matches(LanguagePreset preset, LanguagePair current) =>
        preset.From.Equals(current.From, StringComparison.OrdinalIgnoreCase)
        && preset.To.Equals(current.To, StringComparison.OrdinalIgnoreCase);

    private Border MakeButton(string text, Action onClick)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Margin = new Thickness(9, 4, 9, 4),
            Foreground = Theme.Brush("TextBrush", Colors.White),
        };

        var chrome = new Border
        {
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = label,
        };

        chrome.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        chrome.MouseEnter += (sender, _) =>
        {
            if (sender is Border { Background: not SolidColorBrush } hovered)
                hovered.Background = Hover();
        };
        chrome.MouseLeave += (sender, _) =>
        {
            if (sender is Border hovered && !ReferenceEquals(hovered, _primary))
                hovered.Background = Brushes.Transparent;
        };

        return chrome;
    }

    private Border MakeToggle(string key, string text, bool active)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Margin = new Thickness(7, 4, 7, 4),
            Foreground = Theme.Brush("TextBrush", Colors.White),
        };

        var chrome = new Border
        {
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2, 0, 2, 0),
            Background = active ? Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6)) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = label,
        };

        chrome.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleRequested?.Invoke(key);
        };
        chrome.MouseEnter += (sender, _) =>
        {
            if (sender is Border border && !IsToggleActive(border))
                border.Background = Hover();
        };
        chrome.MouseLeave += (_, _) => RefreshToggles();

        _toggles.Add((key, chrome, label));
        return chrome;
    }

    private static bool IsToggleActive(Border border) =>
        border.Background is SolidColorBrush brush
        && brush.Color == Theme.Color("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6));

    private static SolidColorBrush Hover() => Theme.Brush("BorderBrush", Color.FromRgb(0x2E, 0x33, 0x46));

    private static Border MakeSeparator() => new()
    {
        Width = 1,
        Margin = new Thickness(4, 6, 4, 6),
        Background = Theme.Brush("BorderBrush", Color.FromRgb(0x2E, 0x33, 0x46)),
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        // The bar does not participate in capture and does not use click-through.
        OverlayWindowInterop.ApplyDisplayAffinity(_handle, CaptureExcluded);
        OverlayWindowInterop.ApplyClickThrough(_handle, enabled: false);
        if (_anchor.Width > 0)
            PlaceAbove(_anchor, _anchor.Y + _anchor.Height);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            if (!_closePending)
            {
                _closePending = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _closePending = false;
                    if (!_allowClose)
                        CloseRequested?.Invoke();
                }));
            }
        }
        base.OnClosing(e);
    }
}
