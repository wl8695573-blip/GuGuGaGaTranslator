using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.App;

/// <summary>可拖动的快捷入口。关闭仅隐藏悬浮球，不退出主程序。</summary>
public sealed class FloatingBallWindow : Window
{
    private readonly FloatingBallConfig _config;
    private readonly ContextMenu _menu = new();
    private MenuItem? _updateMenuItem;
    private readonly Thumb _dragHandle = new() { Cursor = Cursors.Hand };
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
        Width = 88;
        Height = 40;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = "LCTA 悬浮球";
        var layout = new Grid();
        // 沿字标橙色外轮廓裁切，保留中央缺口，桌面上不显示图片的黑色底边。
        var ball = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L42.2,0 L42.2,6.9 L45.8,6.9 L45.8,0 L88,0 L88,40 L45.8,40 L45.8,32.9 L42.2,32.9 L42.2,40 L0,40 Z"),
            Fill = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/LCTA;component/Assets/lcta-wordmark.png")))
            {
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(46d / 1815, 46d / 866, 1724d / 1815, 770d / 866), Stretch = Stretch.Fill
            },
            ToolTip = "点击打开快捷菜单，拖动调整位置；在菜单中关闭"
        };
        layout.Children.Add(ball);
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        _dragHandle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };
        layout.Children.Add(_dragHandle);
        layout.Children.Add(_updateMark);
        Content = layout;
        _menu.PlacementTarget = ball;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        _menu.Tag = BuildMenuHeading();
        _menu.Opened += (_, _) =>
        {
            if (!IsLoaded) return;
            var position = PointToScreen(new Point());
            var area = OverlayWindowInterop.WorkAreaAt(new Int32Rect((int)position.X, (int)position.Y, 88, 40));
            _menu.MaxHeight = Math.Max(160, area.Height / VisualTreeHelper.GetDpi(this).DpiScaleY - 24);
        };
        foreach (var (label, run) in actions)
        {
            if (_menu.Items.Count > 0 && label is "开始翻译" or "手动框选字幕" or "服务设置" or "关闭悬浮球")
                _menu.Items.Add(new Separator());
            var item = new MenuItem
            {
                Header = label,
                Icon = new TextBlock
                {
                    Text = MenuGlyph(label), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
                    Foreground = Theme.Brush("AccentBrush", Colors.DarkOrange), VerticalAlignment = VerticalAlignment.Center
                }
            };
            if (label == "程序 / 词库更新") _updateMenuItem = item;
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
            var area = OverlayWindowInterop.WorkAreaAt(new Int32Rect(_config.X ?? 0, _config.Y ?? 0, (int)Width, (int)Height));
            var dpi = VisualTreeHelper.GetDpi(this);
            var size = (int)Math.Ceiling(Width * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
            OverlayWindowInterop.MoveTo(_handle,
                Math.Clamp(_config.X ?? area.X + area.Width - size - 24, area.X, Math.Max(area.X, area.X + area.Width - size)),
                Math.Clamp(_config.Y ?? area.Y + area.Height / 2, area.Y, Math.Max(area.Y, area.Y + area.Height - height)));
        };
        Closed += (_, _) => _menu.IsOpen = false;
    }

    public void SetUpdateAvailable(bool available)
    {
        _updateMark.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (_updateMenuItem is not null) _updateMenuItem.InputGestureText = available ? "有更新" : "";
    }

    private static FrameworkElement BuildMenuHeading()
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/LCTA;component/Assets/app-wordmark.png")),
            Width = 54, Height = 26, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0)
        });
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = "LCTA", FontSize = 16, FontFamily = new FontFamily("Bahnschrift"), FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Brush("AccentBrush", Colors.DarkOrange)
        });
        text.Children.Add(new TextBlock
        {
            Text = "快捷导航", FontSize = 11, Foreground = Theme.Brush("HintTextBrush", Colors.BurlyWood)
        });
        heading.Children.Add(text);
        return heading;
    }

    private static string MenuGlyph(string label) => label switch
    {
        "打开主界面" => "\uE80F",
        "开始翻译" => "\uE768",
        "暂停 / 继续" => "\uE769",
        "停止翻译" => "\uE71A",
        "手动框选字幕" => "\uE8A7",
        "切换翻译方向" => "\uE8AB",
        "添加 / 编辑术语" => "\uE8D2",
        "移动译文 / 鼠标穿透" => "\uE73F",
        "服务设置" => "\uE713",
        "程序 / 词库更新" => "\uE72C",
        "关闭悬浮球" => "\uE8BB",
        _ => "\uE10C"
    };

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
