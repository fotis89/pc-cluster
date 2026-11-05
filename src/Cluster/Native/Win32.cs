using System.Runtime.InteropServices;

namespace Cluster.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public readonly bool Covers(RECT other) =>
        Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
    public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
}

internal static class User32
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000,
        WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

    public static readonly nint HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    public const int WM_NULL = 0x0, WM_ACTIVATE = 0x6, WM_WINDOWPOSCHANGED = 0x47, WM_DISPLAYCHANGE = 0x7E,
        WM_SETTINGCHANGE = 0x1A, WM_DPICHANGED = 0x2E0, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205,
        WM_MOUSEACTIVATE = 0x21, WM_APP = 0x8000;
    public const int MA_NOACTIVATE = 3;

    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITORINFOF_PRIMARY = 1;

    public const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern nint GetShellWindow();
    [DllImport("user32.dll")] public static extern nint GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hWnd, char[] buffer, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    public delegate bool EnumChildProc(nint hWnd, nint data);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumChildProc proc, nint data);

    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    public delegate void WinEventProc(nint hook, uint evt, nint hWnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hWnd, uint flags);
    public static nint MonitorFromWindowNearest(nint hWnd) => MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(nint hMenu, uint flags, nint idOrSubmenu, string? text);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hWnd, nint tpm);

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public int xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
    }

    [DllImport("user32.dll")] public static extern nint CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint hIcon);

    public static long GetExStyle(nint hWnd) => GetWindowLongPtr(hWnd, GWL_EXSTYLE);
    public static void SetExStyle(nint hWnd, long style) => SetWindowLongPtr(hWnd, GWL_EXSTYLE, (nint)style);

    public static string ClassName(nint hWnd)
    {
        var buffer = new char[64];
        int n = GetClassName(hWnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }
}

internal static class Gdi32
{
    [DllImport("gdi32.dll")] public static extern nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[]? bits);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
}

internal static class Shcore
{
    public const int MDT_EFFECTIVE_DPI = 0;
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hMonitor, int type, out uint dpiX, out uint dpiY);
}

internal static class Kernel32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
            ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
}

internal static class IpHlpApi
{
    /// <summary>Route lookup only: finds the interface that would carry traffic to the address. Sends nothing.</summary>
    [DllImport("iphlpapi.dll")] public static extern int GetBestInterface(uint destAddr, out uint interfaceIndex);
}

internal static class Shell32
{
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll")] public static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);

    public const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
}
