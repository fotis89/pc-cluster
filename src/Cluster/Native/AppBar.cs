using static Cluster.Native.Shell32;

namespace Cluster.Native;

/// <summary>
/// Thin wrapper over SHAppBarMessage. Every registered bar is tracked so that ABM_REMOVE can run on
/// every exit path (normal exit, session end, unhandled exception); otherwise Windows keeps the space reserved.
/// </summary>
internal static class AppBar
{
    static readonly HashSet<nint> registered = [];
    static readonly Lock gate = new();

    static APPBARDATA Data(nint hwnd) => new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    public static bool Register(nint hwnd, uint callbackMessage)
    {
        var abd = Data(hwnd);
        abd.uCallbackMessage = callbackMessage;
        bool ok = SHAppBarMessage(ABM_NEW, ref abd) != 0;
        lock (gate) registered.Add(hwnd);
        return ok;
    }

    public static void Remove(nint hwnd)
    {
        lock (gate)
            if (!registered.Remove(hwnd)) return;
        var abd = Data(hwnd);
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    public static void RemoveAll()
    {
        nint[] all;
        lock (gate)
        {
            all = [.. registered];
            registered.Clear();
        }
        foreach (var hwnd in all)
        {
            var abd = Data(hwnd);
            SHAppBarMessage(ABM_REMOVE, ref abd);
        }
    }

    /// <summary>Asks for <paramref name="request"/> on <paramref name="edge"/>; returns the rect Windows granted.</summary>
    public static RECT QueryAndSet(nint hwnd, uint edge, RECT request, Func<RECT, RECT> adjust)
    {
        var abd = Data(hwnd);
        abd.uEdge = edge;
        abd.rc = request;
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc = adjust(abd.rc);
        SHAppBarMessage(ABM_SETPOS, ref abd);
        return abd.rc;
    }

    public static void Notify(nint hwnd, uint message)
    {
        var abd = Data(hwnd);
        SHAppBarMessage(message, ref abd);
    }
}
