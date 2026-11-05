using System.Windows;
using System.Windows.Threading;
using Cluster.Model;
using Cluster.Native;
using Cluster.Sensors;
using Cluster.Settings;
using Cluster.Ui;

namespace Cluster;

public partial class App : Application, IClusterApp
{
    Mutex? singleInstance;
    EventWaitHandle? exitSignal;
    SettingsStore store = null!;
    AppSettings settings = null!;
    Sampler? sampler;
    TrayIcon? tray;
    ClusterWindow? window;
    TaskbarProbe? probe;
    TaskbarInfo? lastTaskbar;
    Snapshot? last;
    bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Enabled = true;
        bool exitRequest = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Cluster.Exit.4b1e7c");
        singleInstance = new Mutex(true, @"Local\Cluster.SingleInstance.4b1e7c", out bool first);
        if (!first || exitRequest)
        {
            // `Cluster.exe --exit` asks the running copy to quit cleanly (frees the docked space).
            if (exitRequest && !first) exitSignal.Set();
            if (first) singleInstance.ReleaseMutex();
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown(0);
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => Dispatcher.BeginInvoke(Quit), null, Timeout.Infinite, executeOnlyOnce: true);

        // ABM_REMOVE on every exit path, or Windows keeps the docked bar's space reserved.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Write("Unhandled", (Exception)args.ExceptionObject);
            AppBar.RemoveAll();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppBar.RemoveAll();
        SessionEnding += (_, _) => AppBar.RemoveAll();

        store = new SettingsStore(SettingsStore.DefaultPath);
        settings = store.Load();
        try { Autostart.Refresh(); } catch (Exception ex) { Log.Write("Autostart", ex); }

        tray = new TrayIcon();
        tray.MenuRequested += ShowMenu;
        OpenWindow();

        sampler = new Sampler();
        sampler.Sampled += snapshot => Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Apply(snapshot));
        sampler.Start();
    }

    void Apply(Snapshot snapshot)
    {
        if (exiting) return;
        last = snapshot;
        window?.OnSnapshot(snapshot);
        tray?.SetTip(Readouts.TrayTip(snapshot.Readings));
    }

    /// <summary>Each mode or placement change builds a fresh window; the old one gives back its app-bar space first.</summary>
    void OpenWindow()
    {
        window?.Close();
        window = settings.Mode switch
        {
            DisplayMode.Docked => new DockedWindow(settings, this),
            DisplayMode.Overlay => new OverlayWindow(settings, this),
            _ => new TaskbarWindow(settings, this),
        };
        window.Show();
        if (last is not null) window.OnSnapshot(last);

        // The taskbar probe only runs in taskbar mode.
        if (window is TaskbarWindow tw)
        {
            if (probe is null)
            {
                probe = new TaskbarProbe(settings);
                probe.Changed += info => Dispatcher.BeginInvoke(() =>
                {
                    lastTaskbar = info;
                    (window as TaskbarWindow)?.OnTaskbar(info);
                });
                probe.Start();
            }
            else
            {
                probe.Retarget(settings);
                // Same screen: reuse the last report. Otherwise wait for the probe's next one.
                if (lastTaskbar?.Monitor.DeviceName == Monitors.Resolve(settings).DeviceName) tw.OnTaskbar(lastTaskbar);
            }
        }
        else
        {
            probe?.Dispose();
            probe = null;
            lastTaskbar = null;
        }
    }

    void Change(Action<AppSettings> edit)
    {
        edit(settings);
        settings.Normalize();
        store.Save(settings);
        OpenWindow();
    }

    public void ShowMenu()
    {
        if (tray is null || exiting) return;
        bool overlay = settings.Mode == DisplayMode.Overlay;
        bool docked = settings.Mode == DisplayMode.Docked;
        var monitors = Monitors.All();
        var current = Monitors.Resolve(monitors, settings.Screen, settings.ScreenBounds);
        bool autostart = Autostart.IsEnabled();

        using var menu = new MenuBuilder();
        menu.Item("In the taskbar", () => Change(s => s.Mode = DisplayMode.Taskbar), check: settings.Mode == DisplayMode.Taskbar)
            .Item("Docked bar", () => Change(s => s.Mode = DisplayMode.Docked), check: docked)
            .Item("Overlay", () => Change(s => s.Mode = DisplayMode.Overlay), check: overlay)
            .Separator()
            .Sub("Screen", sub =>
            {
                for (int i = 0; i < monitors.Count; i++)
                {
                    var m = monitors[i];
                    sub.Item(Monitors.Describe(m, i + 1), () => Change(s =>
                    {
                        s.Screen = m.DeviceName;
                        s.ScreenBounds = [m.Bounds.Left, m.Bounds.Top, m.Bounds.Right, m.Bounds.Bottom];
                    }), check: m.DeviceName == current.DeviceName);
                }
            })
            .Sub("Dock edge", sub =>
            {
                foreach (var edge in new[] { Edge.Top, Edge.Bottom, Edge.Left, Edge.Right })
                    sub.Item(edge.ToString(), () => Change(s => { s.Edge = edge; s.Mode = DisplayMode.Docked; }),
                        check: docked && settings.Edge == edge);
            })
            .Sub("Overlay position", sub =>
            {
                foreach (var (corner, label) in new[]
                {
                    (Corner.TopLeft, "Top left"), (Corner.TopCenter, "Top centre"), (Corner.TopRight, "Top right"),
                    (Corner.BottomLeft, "Bottom left"), (Corner.BottomCenter, "Bottom centre"), (Corner.BottomRight, "Bottom right"),
                })
                    sub.Item(label, () => Change(s => { s.Corner = corner; s.Mode = DisplayMode.Overlay; }),
                        check: overlay && settings.Corner == corner);
            })
            .Separator()
            .Item("Click-through (overlay)", () => Change(s => s.ClickThrough = !s.ClickThrough), check: settings.ClickThrough, enabled: overlay)
            .Sub("Opacity (overlay)", sub =>
            {
                foreach (var o in new[] { 1.0, 0.85, 0.7, 0.55, 0.4 })
                    sub.Item($"{o * 100:0}%", () => Change(s => s.Opacity = o), check: Math.Abs(settings.Opacity - o) < 0.01);
            }, enabled: overlay)
            .Item("Hide when a full-screen app is active (overlay)", () => Change(s => s.HideInFullScreen = !s.HideInFullScreen),
                check: settings.HideInFullScreen, enabled: overlay)
            .Separator()
            .Item("Start at sign-in", () =>
            {
                try { Autostart.Set(!autostart); } catch (Exception ex) { Log.Write("Autostart", ex); }
            }, check: autostart)
            .Separator()
            .Item("Exit", Quit);
        menu.Show(tray.Handle);
    }

    public void Quit()
    {
        if (exiting) return;
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        sampler?.Dispose();
        probe?.Dispose();
        window?.Close();
        window = null;
        AppBar.RemoveAll();
        tray?.Dispose();
        singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        exitSignal?.Dispose();
        base.OnExit(e);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("UI", e.Exception);
        AppBar.RemoveAll();
        e.Handled = true;
        Shutdown(1);
    }
}
