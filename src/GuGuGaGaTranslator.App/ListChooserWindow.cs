using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.App;

/// <summary>
/// A small list that hangs under the overlay's control bar and returns what was
/// picked: the game profiles, selectable without leaving the game.
/// </summary>
public sealed class ListChooserWindow : Window
{
    private int _chosen = -1;

    private ListChooserWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    /// <summary>One row of the list.</summary>
    public sealed record Item(string Label, string? Note = null);

    /// <summary>Show the list under an anchor (physical pixels) and return the index that was picked, or -1 when dismissed.</summary>
    public static int Choose(Window owner, IReadOnlyList<Item> items, int current, Int32Rect anchor)
    {
        var chooser = new ListChooserWindow();
        if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
            chooser.Owner = owner;
        chooser.Build(items, current);
        chooser.Place(anchor);
        chooser.ShowDialog();
        return chooser._chosen;
    }

    private void Build(IReadOnlyList<Item> items, int current)
    {
        var list = new StackPanel { Margin = new Thickness(4) };

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var active = index == current;

            var row = new StackPanel { Margin = new Thickness(10, 5, 14, 5) };
            row.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 13,
                Foreground = Theme.Brush("TextBrush", Colors.White),
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            });

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.Note,
                    FontSize = 11,
                    MaxWidth = 460,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("HintTextBrush", Colors.Gray),
                });
            }

            var chrome = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = active ? Theme.Brush("AccentBrush", Color.FromRgb(0x6E, 0x8B, 0xD6)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row,
            };

            var captured = index;
            chrome.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _chosen = captured;
                DialogResult = true;
                Close();
            };
            chrome.MouseEnter += (sender, _) =>
            {
                if (sender is Border { Background: not SolidColorBrush } hovered)
                {
                    hovered.Background = Theme.Brush("BorderBrush", Color.FromRgb(0x2E, 0x33, 0x46));
                }
            };
            chrome.MouseLeave += (_, _) =>
            {
                if (!active)
                    chrome.Background = Brushes.Transparent;
            };

            list.Children.Add(chrome);
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
