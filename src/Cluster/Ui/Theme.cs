using System.Windows;
using System.Windows.Media;
using Cluster.Model;

namespace Cluster.Ui;

/// <summary>Colours and fonts from the Option A strip (slim-options.html). All brushes are frozen.</summary>
internal static class Theme
{
    public static readonly Brush Ink = Solid(0xEE, 0xF1, 0xF5);
    public static readonly Brush Muted = Solid(0x8F, 0xA3, 0xAB);
    public static readonly Brush Needle = Solid(0xFF, 0x6A, 0x2B);
    public static readonly Brush Amber = Solid(0xFF, 0xB5, 0x47);
    public static readonly Brush Red = Solid(0xFF, 0x5B, 0x4F);
    public static readonly Brush Hub = Solid(0x5A, 0x61, 0x6B);
    public static readonly Brush TickMajor = Solid(0xE4, 0xE8, 0xED);
    public static readonly Brush TickMinor = Solid(0x7B, 0x85, 0x90);
    public static readonly Brush BarEdge = Solid(0x2C, 0x40, 0x48);

    public static readonly Brush BarBackground = Freeze(new LinearGradientBrush(
        Color.FromRgb(0x1B, 0x2C, 0x33), Color.FromRgb(0x17, 0x27, 0x2D), new Point(0, 0), new Point(0, 1)));

    public static readonly Brush Face = Freeze(new RadialGradientBrush(Color.FromRgb(0x22, 0x26, 0x2D), Color.FromRgb(0x07, 0x08, 0x0A))
    {
        Center = new Point(0.5, 0.8), GradientOrigin = new Point(0.5, 0.8), RadiusX = 0.8, RadiusY = 0.8,
    });

    public static readonly Brush Chrome = Freeze(new LinearGradientBrush(
        Color.FromRgb(0xA3, 0xAB, 0xB6), Color.FromRgb(0x2A, 0x2F, 0x36), new Point(0, 0), new Point(0, 1)));

    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static Brush ForLevel(Level level) => level switch
    {
        Level.Amber => Amber,
        Level.Red => Red,
        _ => Ink,
    };

    static Brush Solid(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
