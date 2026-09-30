using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Pipeline;

namespace GuGuGaGaTranslator.App;

/// <summary>
/// The list behind the switcher's "其他" button: every configured direction, in a
/// small window under the bar.
/// </summary>
public sealed class DirectionChooserWindow : Window
{
    private LanguagePreset? _chosen;

    private DirectionChooserWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    /// <summary>Show the chooser under a bar and return what was picked. <paramref name="anchor"/> is the bar's physical-pixel rectangle.</summary>
    public static LanguagePreset? Choose(
        Window owner,
        IReadOnlyList<LanguagePreset> presets,
        LanguagePair current,
        Int32Rect anchor)
    {
        var chooser = new DirectionChooserWindow();
        // 只在主窗口确实能当宿主时才设 Owner:被最小化的 Owner 会让模态子窗口行为异常。
        if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
            chooser.Owner = owner;
        chooser.Build(presets, current);
        chooser.Place(anchor);
        chooser.ShowDialog();
        return chooser._chosen;
    }

    private void Build(IReadOnlyList<LanguagePreset> presets, LanguagePair current)
    {
        var list = new StackPanel { Margin = new Thickness(4) };

        foreach (var preset in presets)
        {
            var active = preset.From.Equals(current.From, StringComparison.OrdinalIgnoreCase)
                && preset.To.Equals(current.To, StringComparison.OrdinalIgnoreCase);

            var label = new TextBlock
            {
                Text = preset.Label,
                FontSize = 13,
                Margin = new Thickness(10, 5, 14, 5),
                Foreground = Theme.Brush("TextBrush", Colors.White),
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            };

            var row = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = active ? Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = label,
            };

            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _chosen = preset;
                DialogResult = true;
                Close();
            };
            row.MouseEnter += (sender, _) =>
            {
                if (sender is Border { Background: not SolidColorBrush } hovered)
                {
                    hovered.Background = Theme.Brush("BorderBrush", Color.FromRgb(0x2E, 0x33, 0x46));
                }
            };
            row.MouseLeave += (sender, _) =>
            {
                if (sender is Border hovered && !active)
                    hovered.Background = Brushes.Transparent;
            };

            list.Children.Add(row);
        }

        Content = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Brush("OverlayBorderColor", (byte)0x99, Color.FromRgb(0x6E, 0x8B, 0xD6)),
            Background = Theme.Brush("OverlayPanelColor", 0xF2, Color.FromRgb(0x10, 0x12, 0x1A)),
            Child = list,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            DialogResult = false;
            Close();
        };

        Deactivated += (_, _) =>
        {
            if (IsLoaded)
                Close();
        };
    }

    private void Place(Int32Rect anchor)
    {
        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var height = (int)Math.Ceiling(ActualHeight * Math.Max(0.1, dpi.DpiScaleY));
            var screen = DpiAwareness.VirtualScreen();

            var x = anchor.X;
            var y = anchor.Y + anchor.Height + 4;
            if (y + height > screen.Y + screen.Height)
                y = Math.Max(screen.Y, anchor.Y - height - 4);

            var handle = new WindowInteropHelper(this).Handle;
            OverlayWindowInterop.ExcludeFromCapture(handle);
            OverlayWindowInterop.ApplyClickThrough(handle, enabled: false);
            OverlayWindowInterop.MoveTo(handle, x, y, topmost: true);
            Activate();
        };
    }
}
