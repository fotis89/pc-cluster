using System.Windows;
using System.Windows.Media;
using Cluster.Model;

namespace Cluster.Ui;

/// <summary>One complete set of flat Fluent colours (design option A) for a dark or light taskbar. All brushes are frozen.</summary>
internal sealed record Palette(
    bool IsLight,
    Color AccentColor,
    Brush Ink,
    Brush Muted,
    Brush Accent,
    Brush Amber,
    Brush Red,
    Brush Tick,
    Brush DialFace,
    Brush DialRim,
    Brush DialTrack,
    Brush BarBackground,
    Brush BarEdge)
{
    /// <summary>The accent Windows 11 uses for its default blue, as shown on dark and on light surfaces.</summary>
    public static readonly Color DefaultDarkAccent = Color.FromRgb(0x60, 0xCD, 0xFF);
    public static readonly Color DefaultLightAccent = Color.FromRgb(0x00, 0x5F, 0xB8);

    public static Palette Dark(Color? accent = null) => new(
        IsLight: false,
        AccentColor: accent ?? DefaultDarkAccent,
        Ink: Solid(0xEE, 0xF1, 0xF5),
        Muted: Solid(0x8F, 0xA3, 0xAB),
        Accent: Solid(accent ?? DefaultDarkAccent),
        Amber: Solid(0xFF, 0xB5, 0x47),
        Red: Solid(0xFF, 0x5B, 0x4F),
        Tick: Solid(0x6F, 0x78, 0x82),
        DialFace: Tint(0xFF, 0.05),
        DialRim: Tint(0xFF, 0.10),
        DialTrack: Tint(0xFF, 0.12),
        BarBackground: Solid(0x1C, 0x1F, 0x24),
        BarEdge: Solid(0x2B, 0x2F, 0x36));

    public static Palette Light(Color? accent = null) => new(
        IsLight: true,
        AccentColor: accent ?? DefaultLightAccent,
        Ink: Solid(0x1B, 0x1F, 0x24),
        Muted: Solid(0x5F, 0x68, 0x73),
        Accent: Solid(accent ?? DefaultLightAccent),
        Amber: Solid(0xB8, 0x6E, 0x00),
        Red: Solid(0xC4, 0x2B, 0x1C),
        Tick: Solid(0x9A, 0xA2, 0xAB),
        DialFace: Tint(0x00, 0.04),
        DialRim: Tint(0x00, 0.12),
        DialTrack: Tint(0x00, 0.10),
        BarBackground: Solid(0xF3, 0xF3, 0xF3),
        BarEdge: Solid(0xD7, 0xDB, 0xE0));

    static Brush Solid(byte r, byte g, byte b) => Solid(Color.FromRgb(r, g, b));

    /// <summary>Same theme and accent; brushes are new instances each time, so record equality doesn't apply.</summary>
    public bool Matches(Palette other) => IsLight == other.IsLight && AccentColor == other.AccentColor;

    static Brush Solid(Color c) => Frozen(new SolidColorBrush(c));

    static Brush Tint(byte grey, double alpha) => Frozen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), grey, grey, grey)));

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// The palette in use. Views read it when they're built; the app swaps it with <see cref="Apply"/> and rebuilds
/// the window when the Windows theme changes. Only touched on the UI thread.
/// </summary>
internal static class Theme
{
    public static Palette Current { get; private set; } = Palette.Dark();

    /// <summary>The tray/app icon ignores the theme: it's a fixed dark dial that reads on either taskbar.</summary>
    public static readonly Palette Icon = Palette.Dark();

    public static void Apply(Palette palette) => Current = palette;

    public static Brush Ink => Current.Ink;
    public static Brush Muted => Current.Muted;
    public static Brush Needle => Current.Accent;
    public static Brush Amber => Current.Amber;
    public static Brush Red => Current.Red;
    public static Brush BarBackground => Current.BarBackground;
    public static Brush BarEdge => Current.BarEdge;

    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static Brush ForLevel(Level level) => level switch
    {
        Level.Amber => Amber,
        Level.Red => Red,
        _ => Ink,
    };
}
