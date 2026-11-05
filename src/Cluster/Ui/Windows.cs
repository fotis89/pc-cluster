using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cluster.Model;
using Cluster.Native;
using Cluster.Settings;

namespace Cluster.Ui;

internal interface IClusterApp
{
    void ShowMenu();
    void Quit();
}

/// <summary>Borderless, non-activating tool window shared by both display modes.</summary>
internal abstract class ClusterWindow : Window
{
    protected readonly AppSettings Settings;
    protected readonly IClusterApp ClusterApp;
    protected nint Hwnd;
    protected MonitorInfo Monitor;

    public ClusterView View { get; protected init; } = null!;

    protected ClusterWindow(AppSettings settings, IClusterApp app)
    {
        Settings = settings;
        ClusterApp = app;
        Monitor = Monitors.Resolve(settings);
        Title = "Cluster";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClusterApp.ShowMenu();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = new WindowInteropHelper(this).Handle;
        long ex = User32.GetExStyle(Hwnd);
        ex = (ex | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE) & ~User32.WS_EX_APPWINDOW;
        User32.SetExStyle(Hwnd, ex);
        HwndSource.FromHwnd(Hwnd)!.AddHook(WndProc);
        // Start on the target monitor so WPF lays out at that monitor's DPI from the first frame.
        User32.SetWindowPos(Hwnd, 0, Monitor.WorkArea.Left, Monitor.WorkArea.Top, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        OnReady();
    }

    protected abstract void OnReady();

    /// <summary>Re-applies position after a display, DPI or work-area change.</summary>
    public abstract void Reposition();

    public virtual void OnSnapshot(Snapshot snapshot) => View.Show(snapshot);

    protected virtual nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case User32.WM_MOUSEACTIVATE:
                handled = true;
                return User32.MA_NOACTIVATE;
            case User32.WM_DISPLAYCHANGE:
            case User32.WM_DPICHANGED:
            case User32.WM_SETTINGCHANGE when wParam == 0x2F: // SPI_SETWORKAREA
                // Let WPF finish its own handling first, then put the window back where it belongs.
                Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Background);
                break;
        }
        return 0;
    }

    protected UIElement CloseButton()
    {
        var x = new TextBlock
        {
            Text = "×",
            FontFamily = Theme.TextFont,
            FontSize = 16,
            Foreground = Theme.Muted,
            Padding = new Thickness(6, 0, 6, 2),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Exit Cluster (right-click for options)",
        };
        x.MouseEnter += (_, _) => x.Foreground = Theme.Ink;
        x.MouseLeave += (_, _) => x.Foreground = Theme.Muted;
        x.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClusterApp.Quit();
        };
        return x;
    }

    protected override void OnClosed(EventArgs e)
    {
        View.Detach();
        base.OnClosed(e);
    }
}

/// <summary>
/// Docked mode: a real Windows app bar (SHAppBarMessage). Windows reserves the space, so maximised
/// windows and the desktop make room for it and nothing covers it.
/// </summary>
internal sealed class DockedWindow : ClusterWindow
{
    public const double BarHeight = 48, ColumnWidth = 144;

    static readonly uint TaskbarCreated = User32.RegisterWindowMessage("TaskbarCreated");
    readonly uint callbackMessage = User32.RegisterWindowMessage("Cluster.AppBarNotify");
    readonly Edge edge;
    readonly bool horizontal;
    bool registered, docking;

    public DockedWindow(AppSettings settings, IClusterApp app) : base(settings, app)
    {
        edge = settings.Edge;
        horizontal = Placement.IsHorizontal(edge);
        Background = Theme.BarBackground;
        Width = 1;
        Height = 1;

        var root = new Grid();
        // A 1 px line on the side that faces the desktop.
        root.Children.Add(new Border
        {
            BorderBrush = Theme.BarEdge,
            SnapsToDevicePixels = true,
            BorderThickness = edge switch
            {
                Edge.Top => new Thickness(0, 0, 0, 1),
                Edge.Left => new Thickness(0, 0, 1, 0),
                Edge.Right => new Thickness(1, 0, 0, 0),
                _ => new Thickness(0, 1, 0, 0),
            },
        });
        View = new ClusterView(vertical: !horizontal);
        root.Children.Add(View);
        var close = CloseButton();
        if (close is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            fe.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Top;
            fe.Margin = horizontal ? new Thickness(0, 0, 6, 0) : new Thickness(0, 6, 2, 0);
        }
        root.Children.Add(close);
        Content = root;
    }

    protected override void OnReady()
    {
        registered = AppBar.Register(Hwnd, callbackMessage);
        Dock();
        ContentRendered += (_, _) => Dock();
    }

    public override void Reposition() => Dock();

    void Dock()
    {
        if (!registered || docking || Hwnd == 0) return;
        docking = true;
        try
        {
            Monitor = Monitors.Resolve(Settings);
            int thickness = Monitor.Px(horizontal ? BarHeight : ColumnWidth);
            var rc = AppBar.QueryAndSet(Hwnd, Placement.ToAbe(edge), Placement.DockRequest(Monitor.Bounds, edge, thickness),
                granted => Placement.Fit(granted, edge, thickness));
            User32.SetWindowPos(Hwnd, 0, rc.Left, rc.Top, rc.Width, rc.Height, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        finally
        {
            docking = false;
        }
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == callbackMessage && registered)
        {
            handled = true;
            switch ((int)wParam)
            {
                case Shell32.ABN_POSCHANGED:
                    Dock();
                    break;
                case Shell32.ABN_FULLSCREENAPP:
                    // Step back while a full-screen app (game, video, presentation) is in front.
                    Topmost = lParam == 0;
                    break;
            }
            return 0;
        }
        if (msg == TaskbarCreated && TaskbarCreated != 0)
        {
            // Explorer restarted and forgot every app bar: register again.
            AppBar.Remove(Hwnd);
            registered = AppBar.Register(Hwnd, callbackMessage);
            Dock();
            return 0;
        }
        switch (msg)
        {
            case User32.WM_ACTIVATE when registered:
                AppBar.Notify(Hwnd, Shell32.ABM_ACTIVATE);
                break;
            case User32.WM_WINDOWPOSCHANGED when registered:
                AppBar.Notify(Hwnd, Shell32.ABM_WINDOWPOSCHANGED);
                break;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnClosed(EventArgs e)
    {
        registered = false;
        AppBar.Remove(Hwnd);
        base.OnClosed(e);
    }
}

/// <summary>
/// Overlay mode: a small topmost cluster pinned to a corner, like the Steam overlay. Optional click-through;
/// turns solid while the cursor is over it; hides while a full-screen app covers its screen.
/// </summary>
internal sealed class OverlayWindow : ClusterWindow
{
    readonly System.Windows.Threading.DispatcherTimer hover;
    bool hiddenForFullScreen;

    public OverlayWindow(AppSettings settings, IClusterApp app) : base(settings, app)
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = settings.Opacity;

        View = new ClusterView(vertical: false);
        var row = new DockPanel { LastChildFill = true };
        var close = CloseButton();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(View);
        Content = new Border
        {
            Background = Theme.BarBackground,
            BorderBrush = Theme.BarEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 0, 4, 0),
            Height = DockedWindow.BarHeight + 2,
            Child = row,
        };

        hover = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250),
            System.Windows.Threading.DispatcherPriority.Background, (_, _) => UpdateHover(), Dispatcher);
    }

    protected override void OnReady()
    {
        if (Settings.ClickThrough)
            User32.SetExStyle(Hwnd, User32.GetExStyle(Hwnd) | User32.WS_EX_TRANSPARENT | User32.WS_EX_LAYERED);
        SizeChanged += (_, _) => Reposition();
        // WPF applies its own startup position after SourceInitialized; place again once shown.
        ContentRendered += (_, _) => Reposition();
        hover.Start();
    }

    public override void Reposition()
    {
        if (Hwnd == 0) return;
        Monitor = Monitors.Resolve(Settings);
        if (!User32.GetWindowRect(Hwnd, out var r)) return;
        var (x, y) = Placement.CornerPosition(Monitor.WorkArea, Settings.Corner, r.Width, r.Height, Monitor.Px(16));
        if (x != r.Left || y != r.Top)
            User32.SetWindowPos(Hwnd, User32.HWND_TOPMOST, x, y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    void UpdateHover()
    {
        if (!IsVisible || Hwnd == 0) return;
        bool over = User32.GetCursorPos(out var p) && User32.GetWindowRect(Hwnd, out var r) && r.Contains(p.X, p.Y);
        double target = over ? 1 : Settings.Opacity;
        if (Math.Abs(Opacity - target) > 0.001) Opacity = target;
    }

    public override void OnSnapshot(Snapshot snapshot)
    {
        base.OnSnapshot(snapshot);
        bool hide = Settings.HideInFullScreen && FullScreen.IsActiveOn(Monitor, Hwnd);
        if (hide && !hiddenForFullScreen)
        {
            hiddenForFullScreen = true;
            Hide();
        }
        else if (!hide && hiddenForFullScreen)
        {
            hiddenForFullScreen = false;
            Show();
            Reposition();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        hover.Stop();
        base.OnClosed(e);
    }
}

internal static class FullScreen
{
    /// <summary>True when a game/video/presentation fills <paramref name="monitor"/> (not just a maximised window).</summary>
    public static bool IsActiveOn(MonitorInfo monitor, nint self)
    {
        nint fg = User32.GetForegroundWindow();
        if (fg == 0 || fg == self || fg == User32.GetShellWindow() || fg == User32.GetDesktopWindow()) return false;
        string cls = User32.ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (User32.GetWindowRect(fg, out var r) && r.Covers(monitor.Bounds)) return true;
        // Exclusive D3D full screen may not report a covering rect.
        return Shell32.SHQueryUserNotificationState(out int state) == 0 && state == Shell32.QUNS_RUNNING_D3D_FULL_SCREEN
               && User32.MonitorFromWindowNearest(fg) == monitor.Handle;
    }
}
