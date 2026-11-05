using System.Windows.Media;
using Cluster.Ui;

namespace Cluster.Tests;

public class ThemeTests
{
    // Windows' default blue AccentPalette: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, unused (RGBA each).
    static readonly byte[] BluePalette =
    [
        0x99, 0xEB, 0xFF, 0, 0x4C, 0xC2, 0xFF, 0, 0x00, 0x91, 0xF8, 0, 0x00, 0x78, 0xD4, 0,
        0x00, 0x5A, 0x9E, 0, 0x00, 0x42, 0x75, 0, 0x00, 0x26, 0x42, 0, 0xF7, 0x63, 0x0C, 0,
    ];

    [Fact]
    public void Dark_taskbar_uses_the_light2_accent_shade() =>
        Assert.Equal(Color.FromRgb(0x4C, 0xC2, 0xFF), SystemTheme.AccentShade(BluePalette, light: false));

    [Fact]
    public void Light_taskbar_uses_the_dark1_accent_shade() =>
        Assert.Equal(Color.FromRgb(0x00, 0x5A, 0x9E), SystemTheme.AccentShade(BluePalette, light: true));

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Missing_or_short_palette_falls_back_to_default_blue(byte[]? palette)
    {
        Assert.Equal(Palette.DefaultDarkAccent, SystemTheme.For(light: false, palette).AccentColor);
        Assert.Equal(Palette.DefaultLightAccent, SystemTheme.For(light: true, palette).AccentColor);
    }

    [Fact]
    public void Light_and_dark_palettes_differ_where_it_matters()
    {
        var dark = SystemTheme.For(light: false, null);
        var light = SystemTheme.For(light: true, null);
        Assert.False(dark.IsLight);
        Assert.True(light.IsLight);
        Assert.NotEqual(Ink(dark), Ink(light));
        Assert.False(dark.Matches(light));
        Assert.True(dark.Matches(SystemTheme.For(light: false, null)));
    }

    [Fact]
    public void A_new_accent_is_a_change() =>
        Assert.False(SystemTheme.For(false, null).Matches(SystemTheme.For(false, BluePalette)));

    static Color Ink(Palette p) => ((SolidColorBrush)p.Ink).Color;
}
