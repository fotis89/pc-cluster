using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Cluster.Settings;

public enum DisplayMode { Taskbar, Docked, Overlay }
public enum Edge { Top, Bottom, Left, Right }
public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight, TopCenter, BottomCenter }

public sealed class AppSettings
{
    public DisplayMode Mode { get; set; } = DisplayMode.Taskbar;
    /// <summary>Monitor device name, e.g. \\.\DISPLAY1. Bounds are kept as a fallback if names get reshuffled.</summary>
    public string? Screen { get; set; }
    public int[]? ScreenBounds { get; set; }
    public Edge Edge { get; set; } = Edge.Bottom;
    public Corner Corner { get; set; } = Corner.TopRight;
    public bool ClickThrough { get; set; }
    public double Opacity { get; set; } = 0.85;
    public bool HideInFullScreen { get; set; } = true;
    /// <summary>Needle in the Windows accent colour; off means the default Windows 11 blue.</summary>
    public bool UseAccentColor { get; set; } = true;

    public const double MinOpacity = 0.3;

    public AppSettings Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = DisplayMode.Taskbar;
        if (!Enum.IsDefined(Edge)) Edge = Edge.Bottom;
        if (!Enum.IsDefined(Corner)) Corner = Corner.TopRight;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, 1) : 0.85;
        if (ScreenBounds is { Length: not 4 }) ScreenBounds = null;
        return this;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext;

/// <summary>JSON file under %LOCALAPPDATA%\Cluster. A missing or broken file falls back to defaults.</summary>
public sealed class SettingsStore(string path)
{
    public static string Folder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cluster");

    public static string DefaultPath { get; } = System.IO.Path.Combine(Folder, "settings.json");

    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return (JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJson.Default.AppSettings) ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Write("Settings", ex);
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Settings", ex);
        }
    }
}

/// <summary>Start at sign-in through the per-user Run key (no admin, no scheduled task).</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Cluster";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>If the exe moved since autostart was turned on, point the Run entry at the current exe.</summary>
    public static void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, Command);
    }
}

/// <summary>Local error log only; nothing leaves the PC.</summary>
public static class Log
{
    static readonly Lock gate = new();

    /// <summary>Off by default so unit tests never touch the real log; the app turns it on at startup.</summary>
    public static bool Enabled { get; set; }

    public static void Write(string area, Exception ex) => Write($"{area}: {ex}");

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                string file = System.IO.Path.Combine(SettingsStore.Folder, "cluster.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
