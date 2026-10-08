using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.App;

/// <summary>可拖动的快捷入口。关闭仅隐藏悬浮球，不退出主程序。</summary>
public sealed class FloatingBallWindow : Window
{
    private readonly FloatingBallConfig _config;
    private readonly ContextMenu _menu = new();
    private readonly Thumb _dragHandle = new() { Cursor = Cursors.Hand, Margin = new Thickness(3) };
    private readonly TextBlock _updateMark = new()
    {
        Text = "●", Foreground = Brushes.OrangeRed, FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        ToolTip = "程序或公共词库有更新，点击悬浮球打开更新页"
    };
    private bool _dragged;
    private nint _handle;
    public event Action? PositionChanged;

    public FloatingBallWindow(FloatingBallConfig config, IEnumerable<(string Label, Action Run)> actions)
    {
        _config = config;
        Width = Height = 58;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = "LCTA 悬浮球";
        var layout = new Grid();
        var ball = new Border
        {
            Margin = new Thickness(3), CornerRadius = new CornerRadius(28),
            Background = Theme.Brush("SurfaceBrush", Color.FromRgb(32, 31, 29)),
            BorderBrush = Theme.Brush("AccentBrush", Colors.Orange), BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "译", FontSize = 25, FontWeight = FontWeights.Bold,
                Foreground = Theme.Brush("AccentBrush", Colors.Orange),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        layout.Children.Add(ball);
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        _dragHandle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };
        layout.Children.Add(_dragHandle);
        var close = new Button { Content = "×", Width = 19, Height = 19, Padding = new Thickness(0),
            Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, ToolTip = "关闭悬浮球（可在主界面重新开启）" };
        close.Click += (_, _) => Close();
        layout.Children.Add(close);
        layout.Children.Add(_updateMark);
        Content = layout;
        _menu.PlacementTarget = ball;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        foreach (var (label, run) in actions)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => run();
            _menu.Items.Add(item);
        }
        _dragHandle.DragStarted += (_, _) => _dragged = false;
        _dragHandle.DragDelta += (_, e) =>
        {
            if (e.HorizontalChange == 0 && e.VerticalChange == 0) return;
            _dragged = true;
            _menu.IsOpen = false;
            var point = PointToScreen(new Point());
            var dpi = VisualTreeHelper.GetDpi(this);
            OverlayWindowInterop.MoveTo(_handle, (int)Math.Round(point.X + e.HorizontalChange * dpi.DpiScaleX),
                (int)Math.Round(point.Y + e.VerticalChange * dpi.DpiScaleY));
        };
        _dragHandle.DragCompleted += (_, e) =>
        {
            if (_dragged) SavePosition();
            else if (!e.Canceled) _menu.IsOpen = !_menu.IsOpen;
        };
        _dragHandle.MouseRightButtonUp += (_, _) => _menu.IsOpen = true;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            OverlayWindowInterop.ExcludeFromCapture(_handle);
        };
        Loaded += (_, _) =>
        {
            var area = OverlayWindowInterop.WorkAreaAt(new Int32Rect(_config.X ?? 0, _config.Y ?? 0, 58, 58));
            var dpi = VisualTreeHelper.GetDpi(this);
            var size = (int)Math.Ceiling(58 * dpi.DpiScaleX);
            OverlayWindowInterop.MoveTo(_handle,
                Math.Clamp(_config.X ?? area.X + area.Width - size - 24, area.X, Math.Max(area.X, area.X + area.Width - size)),
                Math.Clamp(_config.Y ?? area.Y + area.Height / 2, area.Y, Math.Max(area.Y, area.Y + area.Height - size)));
        };
        Closed += (_, _) => _menu.IsOpen = false;
    }

    public void SetUpdateAvailable(bool available) => _updateMark.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    private void SavePosition()
    {
        var point = PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = OverlayWindowInterop.WorkAreaAt(new Int32Rect((int)point.X, (int)point.Y, width, height));
        _config.X = Math.Clamp((int)point.X, area.X, Math.Max(area.X, area.X + area.Width - width));
        _config.Y = Math.Clamp((int)point.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        OverlayWindowInterop.MoveTo(_handle, _config.X.Value, _config.Y.Value);
        PositionChanged?.Invoke();
    }
}
