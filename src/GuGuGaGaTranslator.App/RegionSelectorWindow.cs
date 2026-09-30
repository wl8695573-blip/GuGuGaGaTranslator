using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.App;

/// <summary>The full-screen region picker; it reports a rectangle in physical screen pixels, which is what the capture layer works in.</summary>
public sealed class RegionSelectorWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Border _selection;
    private readonly TextBlock _readout = new();
    private readonly TextBlock _hint = new();
    private readonly CheckBox _hideOwn = new();

    private Point _startClient;
    private Int32Rect? _selectionScreen;
    private Action<bool>? _onHideChanged;

    private RegionSelectorWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Theme.Brush("DimColor", Color.FromArgb(0x66, 0x00, 0x00, 0x00));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;

        _selection = new Border
        {
            BorderBrush = Theme.Brush("SelectionStrokeColor", Color.FromRgb(0x6E, 0x8B, 0xD6)),
            BorderThickness = new Thickness(2),
            Background = Theme.Brush("SelectionFillColor", Color.FromArgb(0x22, 0x6E, 0x8B, 0xD6)),
            Visibility = Visibility.Collapsed,
        };

        _readout.Foreground = Brushes.White;
        _readout.FontSize = 16;
        _readout.Background = Theme.Brush("ChipColor", Color.FromArgb(0xCC, 0x10, 0x12, 0x1A));
        _readout.Padding = new Thickness(8, 4, 8, 4);
        _readout.Visibility = Visibility.Collapsed;

        _hint.Text = "拖动鼠标框选要翻译的区域 · Enter 确认 · Esc 取消";
        _hint.Foreground = Brushes.White;
        _hint.FontSize = 15;
        _hint.Background = Theme.Brush("ChipColor", Color.FromArgb(0xCC, 0x10, 0x12, 0x1A));
        _hint.Padding = new Thickness(10, 6, 10, 6);

        // 和微信截图一样:框选时把自家窗口收起来,免得控制窗口和翻译框挡住要框的东西。
        _hideOwn.Content = "隐藏翻译界面(推荐)";
        _hideOwn.Foreground = Brushes.White;
        _hideOwn.FontSize = 14;
        _hideOwn.Padding = new Thickness(10, 6, 10, 6);
        _hideOwn.Background = Theme.Brush("ChipColor", Color.FromArgb(0xCC, 0x10, 0x12, 0x1A));
        _hideOwn.Checked += (_, _) => _onHideChanged?.Invoke(true);
        _hideOwn.Unchecked += (_, _) => _onHideChanged?.Invoke(false);

        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_readout);
        _canvas.Children.Add(_hint);
        _canvas.Children.Add(_hideOwn);
        Content = _canvas;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            // 尺寸用设备无关单位、位置用物理像素:Win32 坐标避开了 WPF 每显示器不同的 DIP 换算。
            var virtualScreen = DpiAwareness.VirtualScreen();
            var dpi = VisualTreeHelper.GetDpi(this);
            Width = virtualScreen.Width / Math.Max(0.1, dpi.DpiScaleX);
            Height = virtualScreen.Height / Math.Max(0.1, dpi.DpiScaleY);

            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            OverlayWindowInterop.MoveTo(handle, virtualScreen.X, virtualScreen.Y, topmost: true);

            PositionHint();
            Activate();
            Focus();
        };
    }

    /// <summary>Show the picker and return the chosen screen rectangle, or null when cancelled.
    /// <paramref name="hideOwnWindows"/> is the starting state of the「隐藏翻译界面」switch, and
    /// <paramref name="onHideChanged"/> is called every time the user flips it, so the caller can hide or
    /// restore its own windows live.</summary>
    public static Int32Rect? Select(Window owner, bool hideOwnWindows, Action<bool>? onHideChanged = null)
    {
        var selector = new RegionSelectorWindow { Owner = owner };
        selector._onHideChanged = onHideChanged;
        selector._hideOwn.IsChecked = hideOwnWindows;
        return selector.ShowDialog() == true ? selector._selectionScreen : null;
    }

    private void PositionHint()
    {
        Canvas.SetLeft(_hint, 40);
        Canvas.SetTop(_hint, 40);
        Canvas.SetLeft(_hideOwn, 40);
        Canvas.SetTop(_hideOwn, 84);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startClient = e.GetPosition(_canvas);
        _selection.Visibility = Visibility.Visible;
        _readout.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(_startClient);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        UpdateSelection(e.GetPosition(_canvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        UpdateSelection(e.GetPosition(_canvas));
    }

    private void UpdateSelection(Point currentClient)
    {
        // PointToScreen 是从客户区 DIP 到抓屏层所用物理像素的、考虑 DPI 的桥。
        var startScreen = PointToScreen(_startClient);
        var currentScreen = PointToScreen(currentClient);

        var left = (int)Math.Round(Math.Min(startScreen.X, currentScreen.X));
        var top = (int)Math.Round(Math.Min(startScreen.Y, currentScreen.Y));
        var width = (int)Math.Round(Math.Abs(currentScreen.X - startScreen.X));
        var height = (int)Math.Round(Math.Abs(currentScreen.Y - startScreen.Y));
        _selectionScreen = new Int32Rect(left, top, width, height);

        var boxLeft = Math.Min(_startClient.X, currentClient.X);
        var boxTop = Math.Min(_startClient.Y, currentClient.Y);
        Canvas.SetLeft(_selection, boxLeft);
        Canvas.SetTop(_selection, boxTop);
        _selection.Width = Math.Abs(currentClient.X - _startClient.X);
        _selection.Height = Math.Abs(currentClient.Y - _startClient.Y);

        _readout.Text = $"{width} × {height}  @ {left},{top}";
        Canvas.SetLeft(_readout, boxLeft);
        Canvas.SetTop(_readout, Math.Max(0, boxTop - 34));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _selectionScreen = null;
            DialogResult = false;
            Close();
            return;
        }

        if (e.Key == Key.Enter
            && _selectionScreen is { Width: >= 8, Height: >= 8 })
        {
            DialogResult = true;
            Close();
        }
    }
}
