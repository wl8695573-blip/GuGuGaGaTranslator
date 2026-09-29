using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Pipeline;

namespace GuGuGaGaTranslator.App;

/// <summary>The translation overlay: a borderless, always-on-top panel that shows
/// the translated line next to the region it came from.</summary>
public sealed class OverlayWindow : Window
{
    private const int GripSize = 14;

    private readonly TextBlock _sourceText = new();
    private readonly TextBlock _translationText = new();
    private readonly StackPanel _stack = new();
    private readonly Border _panel;
    private readonly Grid _root = new();

    private readonly Thumb _dragSurface = new() { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
    private readonly (Thumb Grip, Cursor Cursor, int SignX, int SignY)[] _grips;

    private readonly DropShadowEffect _outline = new()
    {
        Color = Theme.Color("OverlayOutlineColor", Colors.Black),
        ShadowDepth = 0,
        BlurRadius = 6,
        Opacity = 0.95,
        RenderingBias = RenderingBias.Performance,
    };

    private nint _handle;
    private OverlayConfig _config = new();
    private Int32Rect _region;
    private int _panelTopPhysical;
    private LanguageBarWindow? _bar;
    private string? _profileLabel;
    private LanguagePair _languages = new("ja", "zh-Hans");

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        MinWidth = 120;

        _sourceText.FontSize = 14;
        _sourceText.Foreground = Theme.Brush("MutedTextBrush", Color.FromRgb(0x9A, 0xA3, 0xBC));
        _sourceText.TextWrapping = TextWrapping.Wrap;
        _sourceText.Margin = new Thickness(0, 0, 0, 4);
        _sourceText.Visibility = Visibility.Collapsed;

        _translationText.FontSize = 24;
        _translationText.Foreground = Theme.Brush("TextBrush", Colors.White);
        _translationText.TextWrapping = TextWrapping.Wrap;
        _translationText.LineHeight = 34;

        _stack.Children.Add(_sourceText);
        _stack.Children.Add(_translationText);

        _panel = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            BorderThickness = new Thickness(1),
            Child = _stack,
        };

        // 四角手柄:每个只沿它所在的两条边缩放。
        _grips =
        [
            (MakeGrip(Cursors.SizeNWSE), Cursors.SizeNWSE, -1, -1),
            (MakeGrip(Cursors.SizeNESW), Cursors.SizeNESW, 1, -1),
            (MakeGrip(Cursors.SizeNESW), Cursors.SizeNESW, -1, 1),
            (MakeGrip(Cursors.SizeNWSE), Cursors.SizeNWSE, 1, 1),
        ];

        PositionGrip(_grips[0].Grip, HorizontalAlignment.Left, VerticalAlignment.Top);
        PositionGrip(_grips[1].Grip, HorizontalAlignment.Right, VerticalAlignment.Top);
        PositionGrip(_grips[2].Grip, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        PositionGrip(_grips[3].Grip, HorizontalAlignment.Right, VerticalAlignment.Bottom);

        _dragSurface.DragDelta += OnDragMove;
        foreach (var grip in _grips) grip.Grip.DragDelta += OnDragResize;
        foreach (var grip in _grips) grip.Grip.DragCompleted += (_, _) => LayoutChanged?.Invoke();
        _dragSurface.DragCompleted += (_, _) => LayoutChanged?.Invoke();

        _root.Children.Add(_panel);
        _root.Children.Add(_dragSurface);
        foreach (var grip in _grips) _root.Children.Add(grip.Grip);
        Content = _root;

        _panel.IsHitTestVisible = true;
    }

    public event Action? LayoutChanged;

    public event Action<LanguagePreset>? LanguageRequested;

    public event Action? DirectionsRequested;

    /// <summary>Raised when a switcher toggle is pressed: <c>edit</c>, <c>source</c>, or <c>panel</c>.</summary>
    public event Action<string>? ToggleRequested;

    public event Action? ProfileRequested;

    private static Thumb MakeGrip(Cursor cursor) => new()
    {
        Width = GripSize,
        Height = GripSize,
        Cursor = cursor,
        Visibility = Visibility.Collapsed,
        Background = Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6)),
        Opacity = 0.85,
    };

    private static void PositionGrip(Thumb grip, HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        grip.HorizontalAlignment = horizontal;
        grip.VerticalAlignment = vertical;
        grip.Margin = new Thickness(
            horizontal == HorizontalAlignment.Left ? -GripSize / 2 : 0,
            vertical == VerticalAlignment.Top ? -GripSize / 2 : 0,
            horizontal == HorizontalAlignment.Right ? -GripSize / 2 : 0,
            vertical == VerticalAlignment.Bottom ? -GripSize / 2 : 0);
    }

    /// <summary>Apply appearance settings and show or hide the language switcher.</summary>
    public void Configure(OverlayConfig config, LanguagePair languages)
    {
        _config = config;
        _languages = languages;
        _translationText.FontSize = Math.Clamp(config.FontSize, 8, 96);
        _translationText.LineHeight = _translationText.FontSize * 1.4;
        _sourceText.FontSize = Math.Max(10, _translationText.FontSize * 0.58);
        _sourceText.Visibility = config.ShowSource ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(config.FontFamily))
        {
            var family = new FontFamily(config.FontFamily);
            _translationText.FontFamily = family;
            _sourceText.FontFamily = family;
        }

        _translationText.TextAlignment = config.TextAlign.ToLowerInvariant() switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

        // 隐藏底框(或把不透明度设成 0)就只剩带描边的文字浮在游戏上,也就是多数玩家要的「盖住原文」效果。
        var alpha = config.ShowPanel
            ? (byte)Math.Clamp(config.BackgroundOpacity * 255, 0, 255)
            : (byte)0;
        _panel.Background = Theme.Brush("OverlayPanelColor", alpha, Color.FromRgb(0x10, 0x12, 0x1A));
        _panel.BorderBrush = Theme.Brush("OverlayBorderColor", (byte)(alpha * 0.4), Color.FromRgb(0x6E, 0x8B, 0xD6));
        _panel.CornerRadius = new CornerRadius(Math.Max(0, config.CornerRadius));
        _panel.Padding = new Thickness(Math.Max(0, config.Padding), Math.Max(0, config.Padding * 0.7),
            Math.Max(0, config.Padding), Math.Max(0, config.Padding * 0.7));

        _translationText.Effect = config.TextOutline ? _outline : null;
        _sourceText.Effect = config.TextOutline ? _outline : null;

        // 解锁 = 可拖动、可缩放;锁定 = 每次点击都交给游戏。
        var editable = !config.ClickThrough;
        _dragSurface.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        foreach (var grip in _grips) grip.Grip.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;

        if (_handle != 0) OverlayWindowInterop.ApplyClickThrough(_handle, config.ClickThrough);
        UpdateLanguageBar();
        if (_region.Width > 0) PlaceAt(_region);
    }

    /// <summary>Move the panel with the mouse; the drag delta arrives in device-independent units and is converted to physical pixels here.</summary>
    private void OnDragMove(object sender, DragDeltaEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _config.OffsetX += (int)Math.Round(e.HorizontalChange * dpi.DpiScaleX);
        _config.OffsetY += (int)Math.Round(e.VerticalChange * dpi.DpiScaleY);
        PlaceAt(_region);
    }

    /// <summary>Resize the panel from a corner; width and height become explicit because an auto-sized panel has nothing to add to.</summary>
    private void OnDragResize(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb grip) return;
        var entry = _grips.FirstOrDefault(candidate => ReferenceEquals(candidate.Grip, grip));
        if (entry.Grip is null) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (int)Math.Round(e.HorizontalChange * dpi.DpiScaleX);
        var dy = (int)Math.Round(e.VerticalChange * dpi.DpiScaleY);

        if (_config.Width <= 0) _config.Width = Math.Max(120, _region.Width);
        if (_config.Height <= 0) _config.Height = Math.Max(32, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));

        _config.Width = Math.Max(120, _config.Width + (entry.SignX * dx));
        _config.Height = Math.Max(32, _config.Height + (entry.SignY * dy));

        // 拖左上角时,面板移动的量和缩放它的量一样多。
        if (entry.SignX < 0) _config.OffsetX += dx;
        if (entry.SignY < 0) _config.OffsetY += dy;

        PlaceAt(_region);
    }

    public void SetLanguages(LanguagePair languages)
    {
        _languages = languages;
        _bar?.Update(languages, !_config.ClickThrough, _config.ShowSource, _config.ShowPanel, _profileLabel);
    }

    public void SetProfile(string? profile)
    {
        _profileLabel = profile;
        _bar?.Update(_languages, !_config.ClickThrough, _config.ShowSource, _config.ShowPanel, _profileLabel);
    }

    /// <summary>The switcher's screen rectangle in physical pixels, for the direction list to hang from.</summary>
    public Int32Rect LanguageBarBounds() => _bar?.ScreenBounds ?? default;

    private void UpdateLanguageBar()
    {
        if (!_config.ShowLanguageBar || _config.LanguagePresets.Count == 0)
        {
            _bar?.Hide();
            return;
        }

        if (_bar is null)
        {
            _bar = new LanguageBarWindow { CaptureExcluded = _config.ExcludeFromCapture };
            _bar.PresetSelected += preset => LanguageRequested?.Invoke(preset);
            _bar.MoreRequested += () => DirectionsRequested?.Invoke();
            _bar.ToggleRequested += key => ToggleRequested?.Invoke(key);
            _bar.ProfileRequested += () => ProfileRequested?.Invoke();
        }

        _bar.Configure(
            _config.LanguagePresets,
            _languages,
            !_config.ClickThrough,
            _config.ShowSource,
            _config.ShowPanel,
            _profileLabel);
        if (!_bar.IsVisible) _bar.Show();
        PlaceLanguageBar();
    }

    private void PlaceLanguageBar()
    {
        if (_bar is null || !_bar.IsVisible || _region.Width <= 0) return;

        _bar.PlaceAbove(_region, _panelTopPhysical);
    }

    /// <summary>Show a translation anchored to a screen region (<paramref name="region"/> in physical pixels), placed as configured.</summary>
    public void ShowTranslated(string source, string translation, Int32Rect region)
    {
        _region = region;
        _sourceText.Text = source;

        var lines = translation.Split('\n');
        var maxLines = Math.Max(1, _config.MaxLines);
        _translationText.Text = lines.Length <= maxLines
            ? translation
            : string.Join('\n', lines.Take(maxLines)) + " …";

        PlaceAt(region);
        if (!IsVisible) Show();
    }

    /// <summary>Reposition the overlay for a region in physical screen pixels, following the target window.</summary>
    public void PlaceAt(Int32Rect region)
    {
        _region = region;
        if (_handle == 0) return;

        // WPF 的尺寸是设备无关单位、位置是物理像素:混用会让悬浮层在缩放显示器上漂移,
        // 所以转换只在这里做。
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = Math.Max(0.1, dpi.DpiScaleX);
        var scaleY = Math.Max(0.1, dpi.DpiScaleY);

        var widthPhysical = _config.Width > 0 ? _config.Width : region.Width;
        Width = Math.Max(MinWidth, widthPhysical / scaleX);

        if (_config.Height > 0)
        {
            SizeToContent = SizeToContent.Manual;
            Height = Math.Max(24, _config.Height / scaleY);
        }
        else
        {
            SizeToContent = SizeToContent.Height;
        }

        // Height 为 0 时面板自适应:ActualHeight 是设备无关单位,要先乘 DPI 缩放;
        // 尚未布局过(ActualHeight 为 0)就按字号的 2.4 倍估一个高度。
        var panelHeight = _config.Height > 0
            ? _config.Height
            : (int)Math.Ceiling((ActualHeight > 0 ? ActualHeight : _config.FontSize * 2.4) * scaleY);

        var x = region.X + _config.OffsetX;
        var y = _config.Placement.ToLowerInvariant() switch
        {
            OverlayPlacement.Over => region.Y + _config.OffsetY,
            OverlayPlacement.Above => region.Y - panelHeight - _config.OffsetY,
            _ => region.Y + region.Height + _config.OffsetY,
        };
        if (y < 0) y = 0;

        _panelTopPhysical = y;
        OverlayWindowInterop.MoveTo(_handle, x, y, topmost: true);
        PlaceLanguageBar();
    }

    public void ClearText()
    {
        _translationText.Text = string.Empty;
        _sourceText.Text = string.Empty;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        if (_config.ExcludeFromCapture) OverlayWindowInterop.ExcludeFromCapture(_handle);
        OverlayWindowInterop.ApplyClickThrough(_handle, _config.ClickThrough);
        if (_region.Width > 0) PlaceAt(_region);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // 语言条是独立窗口,这里不关就会留下一个孤儿窗口。
        _bar?.Close();
        _bar = null;
        base.OnClosed(e);
    }
}
