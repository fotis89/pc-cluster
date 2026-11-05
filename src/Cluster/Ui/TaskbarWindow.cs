using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cluster.Model;
using Cluster.Native;
using Cluster.Settings;

namespace Cluster.Ui;

/// <summary>
/// "In the taskbar" mode: a transparent topmost window laid over the empty part of the taskbar row, left of
/// the first taskbar button (Start, or Widgets/Search when shown). Nothing is injected into Explorer and the
/// taskbar isn't reparented; the window just sits above it and re-asserts its z-order when the taskbar comes forward.
/// Start, app buttons and the tray are never covered: when room shrinks it drops gauges from the right.
/// </summary>
internal sealed class TaskbarWindow : ClusterWindow
{
    const double EdgeGap = 12;

    // Widest first: all four gauges, then fewer.
    static readonly int[] Layouts = [4, 3, 2, 1];

    readonly Grid root;
    readonly DispatcherTimer raise;
    readonly User32.WinEventProc onForeground;
    nint foregroundHook;
    TaskbarInfo? taskbar;
    bool fullScreen, shown = true;
    string? lastReason;

    public TaskbarWindow(AppSettings settings, IClusterApp app) : base(settings, app)
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.Width;
        Height = DockedWindow.BarHeight;

        View = new ClusterView(vertical: false, compact: true)
        {
            // Alpha 1: invisible, but makes the whole cluster hit-testable for right-click and tooltips.
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        root = new Grid();
        root.Children.Add(View);
        Content = root;
        SetShown(false, null); // until the probe reports the taskbar

        raise = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Raise(), Dispatcher);
        onForeground = (_, _, _, _, _, _, _) => Raise();
    }

    protected override void OnReady()
    {
        foregroundHook = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, 0,
            onForeground, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
        SizeChanged += (_, _) => Place();
        raise.Start();
    }

    public void OnTaskbar(TaskbarInfo? info)
    {
        taskbar = info;
        Reposition();
    }

    public override void OnSnapshot(Snapshot snapshot)
    {
        base.OnSnapshot(snapshot);
        bool fs = taskbar is not null && FullScreen.IsActiveOn(taskbar.Monitor, Hwnd);
        if (fs != fullScreen)
        {
            fullScreen = fs;
            Reposition();
        }
    }

    public override void Reposition()
    {
        if (Hwnd == 0) return;
        var t = taskbar;
        if (t is null) { SetShown(false, "no taskbar on the chosen screen"); return; }
        if (!t.OnScreen) { SetShown(false, "taskbar is auto-hidden"); return; }
        if (fullScreen) { SetShown(false, null); return; }
        if (t.OccupiedLeft is not int occupied) { SetShown(false, "taskbar buttons can't be read"); return; }

        double scale = t.Monitor.Scale;
        double room = Placement.TaskbarRoom(t.Bounds, occupied, t.Monitor.Px(EdgeGap)) / scale;
        var widths = Layouts.Select(View.MeasureWidth).ToArray();
        int fit = Placement.FirstFit(room, widths);
        if (fit < 0) { SetShown(false, $"no room left of the taskbar buttons ({room:0} DIP)"); return; }

        View.SetParts(Layouts[fit]);
        Height = t.Bounds.Height / scale;
        SetShown(true, null);
        Place();
    }

    void Place()
    {
        if (taskbar is not { } t || Hwnd == 0) return;
        User32.SetWindowPos(Hwnd, User32.HWND_TOPMOST, t.Bounds.Left + t.Monitor.Px(EdgeGap), t.Bounds.Top, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    /// <summary>The taskbar puts itself on top when used; step back in front of it.</summary>
    void Raise()
    {
        if (shown && Hwnd != 0)
            User32.SetWindowPos(Hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    /// <summary>Hides the content rather than the window: a transparent window with nothing drawn is invisible and click-through.</summary>
    void SetShown(bool show, string? reason)
    {
        shown = show;
        root.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        if (reason is not null && reason != lastReason) Log.Write("Taskbar mode hidden: " + reason);
        lastReason = reason;
    }

    protected override void OnClosed(EventArgs e)
    {
        raise.Stop();
        if (foregroundHook != 0) User32.UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        base.OnClosed(e);
    }
}
