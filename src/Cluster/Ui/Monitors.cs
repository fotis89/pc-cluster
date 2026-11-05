using Cluster.Native;
using Cluster.Settings;

namespace Cluster.Ui;

internal sealed record MonitorInfo(nint Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool Primary, double Scale)
{
    public int Px(double dip) => (int)Math.Round(dip * Scale);
}

/// <summary>Monitor enumeration in physical pixels (the process is per-monitor-v2 DPI aware).</summary>
internal static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        User32.EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (User32.GetMonitorInfo(hMonitor, ref info))
            {
                double scale = Shcore.GetDpiForMonitor(hMonitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
                list.Add(new MonitorInfo(hMonitor, info.szDevice, info.rcMonitor, info.rcWork,
                    (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0, scale));
            }
            return true;
        }, 0);
        // Number screens left to right, then top to bottom.
        list.Sort((a, b) => a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top));
        return list;
    }

    /// <summary>The saved screen: by device name, else by saved bounds, else the primary screen.</summary>
    public static MonitorInfo Resolve(AppSettings settings) => Resolve(All(), settings.Screen, settings.ScreenBounds);

    public static MonitorInfo Resolve(IReadOnlyList<MonitorInfo> all, string? device, int[]? bounds)
    {
        if (device is not null)
            foreach (var m in all)
                if (string.Equals(m.DeviceName, device, StringComparison.OrdinalIgnoreCase)) return m;
        if (bounds is { Length: 4 })
            foreach (var m in all)
                if (m.Bounds.Left == bounds[0] && m.Bounds.Top == bounds[1] && m.Bounds.Right == bounds[2] && m.Bounds.Bottom == bounds[3]) return m;
        foreach (var m in all)
            if (m.Primary) return m;
        return all.Count > 0 ? all[0] : new MonitorInfo(0, "", new RECT(0, 0, 1920, 1080), new RECT(0, 0, 1920, 1040), true, 1);
    }

    public static string Describe(MonitorInfo m, int number) =>
        $"Screen {number}: {m.Bounds.Width}×{m.Bounds.Height}" + (m.Scale != 1 ? $" @{m.Scale * 100:0}%" : "") + (m.Primary ? " (main)" : "");
}

/// <summary>Pure placement math, all in physical pixels.</summary>
internal static class Placement
{
    public static bool IsHorizontal(Edge edge) => edge is Edge.Top or Edge.Bottom;

    public static uint ToAbe(Edge edge) => edge switch
    {
        Edge.Left => Shell32.ABE_LEFT,
        Edge.Top => Shell32.ABE_TOP,
        Edge.Right => Shell32.ABE_RIGHT,
        _ => Shell32.ABE_BOTTOM,
    };

    /// <summary>What to ask Windows for: a strip of <paramref name="thickness"/> along the monitor edge.</summary>
    public static RECT DockRequest(RECT monitor, Edge edge, int thickness) => Fit(monitor, edge, thickness);

    /// <summary>After ABM_QUERYPOS moved the rect clear of other bars (e.g. the taskbar), restore our thickness.</summary>
    public static RECT Fit(RECT r, Edge edge, int thickness) => edge switch
    {
        Edge.Top => new RECT(r.Left, r.Top, r.Right, r.Top + thickness),
        Edge.Left => new RECT(r.Left, r.Top, r.Left + thickness, r.Bottom),
        Edge.Right => new RECT(r.Right - thickness, r.Top, r.Right, r.Bottom),
        _ => new RECT(r.Left, r.Bottom - thickness, r.Right, r.Bottom),
    };

    /// <summary>Index of the first layout (widest first) whose width fits, or -1 when none does.</summary>
    public static int FirstFit(double available, IReadOnlyList<double> widths)
    {
        for (int i = 0; i < widths.Count; i++)
            if (widths[i] <= available) return i;
        return -1;
    }

    /// <summary>
    /// Free space in the taskbar row left of its first button, in physical px, after a margin on both sides.
    /// </summary>
    public static int TaskbarRoom(RECT taskbar, int occupiedLeft, int margin) =>
        Math.Max(0, Math.Min(occupiedLeft, taskbar.Right) - taskbar.Left - 2 * margin);

    /// <summary>Free space of a stretch of the taskbar row, in physical px, after a margin on both sides.</summary>
    public static int TaskbarGap(int start, int end, int margin) =>
        Math.Max(0, end - start - 2 * margin);

    /// <summary>
    /// Widest free stretch right of the first button: between buttons, or between the last one and the notification area.
    /// With left-aligned icons Windows puts Widgets next to the tray, so the free stretch is before it, not at the tray.
    /// Ties go to the rightmost. (0, 0) when there is none.
    /// </summary>
    public static (int Start, int End) LargestGap(IEnumerable<(int Left, int Right)> buttons, int? trayLeft)
    {
        var sorted = buttons.OrderBy(b => b.Left).ToList();
        var best = (Start: 0, End: 0);
        int edge = int.MinValue;
        void Consider(int start, int end)
        {
            if (end - start >= best.End - best.Start) best = (start, end);
        }
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i > 0 && sorted[i].Left > edge) Consider(edge, sorted[i].Left);
            edge = Math.Max(edge, sorted[i].Right);
        }
        if (trayLeft is int tray && tray > edge) Consider(edge, tray);
        return best;
    }

    public static (int X, int Y) CornerPosition(RECT workArea, Corner corner, int width, int height, int margin)
    {
        int left = workArea.Left + margin, right = workArea.Right - margin - width;
        int top = workArea.Top + margin, bottom = workArea.Bottom - margin - height;
        int center = workArea.Left + (workArea.Width - width) / 2;
        return corner switch
        {
            Corner.TopLeft => (left, top),
            Corner.TopCenter => (center, top),
            Corner.TopRight => (right, top),
            Corner.BottomLeft => (left, bottom),
            Corner.BottomCenter => (center, bottom),
            _ => (right, bottom),
        };
    }
}
