using System.Windows;
using System.Windows.Media;

namespace GuGuGaGaTranslator.App;

/// <summary>
/// Reads the palette declared in <c>App.xaml</c>, so the borderless windows that
/// build their brushes in C# keep the theme in one file.
/// </summary>
internal static class Theme
{
    /// <summary>Look up a palette color.</summary>
    public static Color Color(string key, Color fallback) =>
        Application.Current?.Resources[key] is Color color ? color : fallback;

    /// <summary>Look up a palette color as a brush.</summary>
    public static SolidColorBrush Brush(string key, Color fallback) => new(Color(key, fallback));

    /// <summary>Look up a palette color as a brush with an explicit alpha (0–255), which is how the overlay applies the configured opacity.</summary>
    public static SolidColorBrush Brush(string key, byte alpha, Color fallback)
    {
        var color = Color(key, fallback);
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
