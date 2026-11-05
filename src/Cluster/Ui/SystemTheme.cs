using System.Windows.Media;
using Microsoft.Win32;

namespace Cluster.Ui;

/// <summary>Reads Windows' light/dark taskbar setting and accent colour from the per-user registry.</summary>
internal static class SystemTheme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // AccentPalette holds 8 RGBA colours: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, (unused).
    // Windows 11 draws accent on dark surfaces with Light2 and on light surfaces with Dark1.
    const int LightShadeForDark = 1, DarkShadeForLight = 4;

    /// <summary>The palette for the current Windows theme. Never throws; falls back to the dark default.</summary>
    public static Palette Read(bool useAccent)
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            bool light = personalize?.GetValue("SystemUsesLightTheme") is int v && v != 0;
            byte[]? accentPalette = null;
            if (useAccent)
            {
                using var accent = Registry.CurrentUser.OpenSubKey(AccentKey);
                accentPalette = accent?.GetValue("AccentPalette") as byte[];
            }
            return For(light, accentPalette);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Settings.Log.Write("Theme", ex);
            return Palette.Dark();
        }
    }

    public static Palette For(bool light, byte[]? accentPalette)
    {
        var accent = AccentShade(accentPalette, light);
        return light ? Palette.Light(accent) : Palette.Dark(accent);
    }

    /// <summary>The accent shade that reads on a light or dark taskbar, or null if the palette is missing or malformed.</summary>
    public static Color? AccentShade(byte[]? accentPalette, bool light)
    {
        if (accentPalette is not { Length: >= 32 }) return null;
        int i = (light ? DarkShadeForLight : LightShadeForDark) * 4;
        return Color.FromRgb(accentPalette[i], accentPalette[i + 1], accentPalette[i + 2]);
    }
}
