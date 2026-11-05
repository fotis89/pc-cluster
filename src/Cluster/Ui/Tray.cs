using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cluster.Model;
using Cluster.Native;

namespace Cluster.Ui;

/// <summary>
/// Notification-area icon. It's the always-reachable way to the menu, even when the overlay is click-through.
/// Owns a hidden window that also serves as owner for the popup menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    const uint Id = 1;
    const int CallbackMessage = User32.WM_APP + 1;
    static readonly uint TaskbarCreated = User32.RegisterWindowMessage("TaskbarCreated");

    readonly HwndSource window;
    readonly nint icon;
    string tip = "Cluster";
    bool added;

    public event Action? MenuRequested;

    public TrayIcon()
    {
        window = new HwndSource(new HwndSourceParameters("Cluster tray") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(WndProc);
        icon = IconFactory.Create(32);
        Add();
    }

    public nint Handle => window.Handle;

    Shell32.NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<Shell32.NOTIFYICONDATA>(),
        hWnd = window.Handle,
        uID = Id,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = icon,
        szTip = tip,
        szInfo = "",
        szInfoTitle = "",
    };

    void Add()
    {
        var data = Data(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        added = Shell32.Shell_NotifyIcon(Shell32.NIM_ADD, ref data);
    }

    public void SetTip(string text)
    {
        if (text.Length > 127) text = text[..127];
        if (text == tip || !added) return;
        tip = text;
        var data = Data(Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        Shell32.Shell_NotifyIcon(Shell32.NIM_MODIFY, ref data);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int mouse = (int)lParam & 0xFFFF;
            if (mouse is User32.WM_RBUTTONUP or User32.WM_LBUTTONUP) MenuRequested?.Invoke();
            handled = true;
        }
        else if (msg == TaskbarCreated && TaskbarCreated != 0)
        {
            Add(); // Explorer restarted
        }
        return 0;
    }

    public void Dispose()
    {
        if (added)
        {
            var data = Data(0);
            Shell32.Shell_NotifyIcon(Shell32.NIM_DELETE, ref data);
            added = false;
        }
        User32.DestroyIcon(icon);
        window.Dispose();
    }
}

/// <summary>Native popup menu (TrackPopupMenuEx) built fresh each time it's opened.</summary>
internal sealed class MenuBuilder : IDisposable
{
    readonly nint menu;
    readonly Dictionary<int, Action> actions;
    readonly Counter ids;

    sealed class Counter { public int Next = 1; }

    public MenuBuilder() : this(User32.CreatePopupMenu(), [], new Counter()) { }

    MenuBuilder(nint menu, Dictionary<int, Action> actions, Counter ids)
    {
        this.menu = menu;
        this.actions = actions;
        this.ids = ids;
    }

    public MenuBuilder Item(string text, Action action, bool check = false, bool enabled = true)
    {
        int id = ids.Next++;
        actions[id] = action;
        User32.AppendMenu(menu, User32.MF_STRING | (check ? User32.MF_CHECKED : 0) | (enabled ? 0 : User32.MF_GRAYED), id, text);
        return this;
    }

    public MenuBuilder Sub(string text, Action<MenuBuilder> build, bool enabled = true)
    {
        nint sub = User32.CreatePopupMenu();
        build(new MenuBuilder(sub, actions, ids));
        User32.AppendMenu(menu, User32.MF_POPUP | (enabled ? 0 : User32.MF_GRAYED), sub, text);
        return this;
    }

    public MenuBuilder Separator()
    {
        User32.AppendMenu(menu, User32.MF_SEPARATOR, 0, null);
        return this;
    }

    /// <summary>Shows the menu at the cursor and runs the chosen action.</summary>
    public void Show(nint owner)
    {
        User32.GetCursorPos(out var p);
        User32.SetForegroundWindow(owner); // otherwise the menu won't close when clicking elsewhere
        int id = User32.TrackPopupMenuEx(menu, User32.TPM_RETURNCMD | User32.TPM_RIGHTBUTTON | User32.TPM_NONOTIFY, p.X, p.Y, owner, 0);
        User32.PostMessage(owner, User32.WM_NULL, 0, 0);
        if (actions.TryGetValue(id, out var action)) action();
    }

    public void Dispose() => User32.DestroyMenu(menu); // also destroys submenus
}

internal static class IconFactory
{
    /// <summary>Renders the tachometer as a 32-bit HICON.</summary>
    public static nint Create(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / DialElement.Size, size / DialElement.Size));
            dc.DrawDrawing(DialElement.BuildIcon(GaugeSpec.Cpu, 0.62));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4) // icons want straight (not premultiplied) alpha
        {
            int a = pixels[i + 3];
            if (a is 0 or 255) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
        }
        nint color = Gdi32.CreateBitmap(size, size, 1, 32, pixels);
        nint mask = Gdi32.CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
        var info = new User32.ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
        nint icon = User32.CreateIconIndirect(ref info);
        Gdi32.DeleteObject(color);
        Gdi32.DeleteObject(mask);
        return icon;
    }
}
