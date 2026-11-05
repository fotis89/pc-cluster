using System.IO;
using Cluster.Model;
using Cluster.Native;
using Cluster.Settings;
using Cluster.Ui;

namespace Cluster.Tests;

public class GaugeSpecTests
{
    [Theory]
    [InlineData(0, 135)]
    [InlineData(0.5, 270)]
    [InlineData(1, 405)]
    [InlineData(-0.2, 135)]
    [InlineData(1.3, 405)]
    [InlineData(double.NaN, 135)]
    public void Angle_sweeps_270_degrees_and_clamps(double position, double expected) =>
        Assert.Equal(expected, GaugeSpec.Angle(position), 6);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.1, 0)]
    [InlineData(1, 0.25)]
    [InlineData(10, 0.5)]
    [InlineData(100, 0.75)]
    [InlineData(1000, 1)]
    [InlineData(5000, 1)]
    public void Net_is_log_scale_from_0_1_Mbps_to_1_Gbps(double mbps, double expected) =>
        Assert.Equal(expected, GaugeSpec.Net.Position(mbps), 6);

    [Theory]
    [InlineData(79, Level.Normal)]
    [InlineData(80, Level.Amber)]
    [InlineData(89.9, Level.Amber)]
    [InlineData(90, Level.Red)]
    public void Cpu_and_disk_zones_are_80_and_90(double value, Level expected)
    {
        Assert.Equal(expected, GaugeSpec.Cpu.LevelFor(value));
        Assert.Equal(expected, GaugeSpec.Disk.LevelFor(value));
    }

    [Theory]
    [InlineData(84, Level.Normal)]
    [InlineData(85, Level.Amber)]
    [InlineData(95, Level.Red)]
    public void Ram_zones_are_85_and_95(double value, Level expected) =>
        Assert.Equal(expected, GaugeSpec.Ram.LevelFor(value));

    [Fact]
    public void Net_has_no_redline()
    {
        Assert.Empty(GaugeSpec.Net.Zones);
        Assert.Equal(Level.Normal, GaugeSpec.Net.LevelFor(10_000));
    }

    [Fact]
    public void Printed_zones_match_thresholds()
    {
        Assert.Equal([new Zone(0.8, 0.9, Level.Amber), new Zone(0.9, 1, Level.Red)], GaugeSpec.Cpu.Zones);
        Assert.Equal([new Zone(0.85, 0.95, Level.Amber), new Zone(0.95, 1, Level.Red)], GaugeSpec.Ram.Zones);
    }
}

public class NeedleDamperTests
{
    [Fact]
    public void Swings_past_the_target_then_rests_on_it()
    {
        var d = new NeedleDamper(0) { Target = 0.5 };
        double peak = 0, t = 0;
        while (d.Step(1.0 / 60) && t < 5)
        {
            t += 1.0 / 60;
            peak = Math.Max(peak, d.Position);
        }
        Assert.True(peak > 0.5, "a small overshoot, like a real needle");
        Assert.True(peak < 0.6, $"overshoot too large: {peak}");
        Assert.True(t < 1.5, $"took {t:0.00}s to settle");
        Assert.Equal(0.5, d.Position);
        Assert.True(d.AtRest);
    }

    [Fact]
    public void Long_frames_stay_stable()
    {
        var d = new NeedleDamper(0) { Target = 1 };
        for (int i = 0; i < 40; i++) d.Step(0.5);
        Assert.Equal(1, d.Position);
    }

    [Fact]
    public void At_rest_returns_false_immediately()
    {
        var d = new NeedleDamper(0.3);
        Assert.False(d.Step(1.0 / 60));
    }
}

public class ReadoutTests
{
    [Theory]
    [InlineData(0, "0.0", "Mbps")]
    [InlineData(0.04, "0.0", "Mbps")]
    [InlineData(1.84, "1.8", "Mbps")]
    [InlineData(9.94, "9.9", "Mbps")]
    [InlineData(18.4, "18", "Mbps")]
    [InlineData(940, "940", "Mbps")]
    [InlineData(1000, "1.0", "Gbps")]
    [InlineData(2500, "2.5", "Gbps")]
    public void Rates(double mbps, string value, string unit) =>
        Assert.Equal((value, unit), Readouts.Rate(mbps));

    [Fact]
    public void Cpu_shows_percent_and_clock()
    {
        var r = Readouts.Cpu(new Readings { CpuPercent = 21.6, CpuGhz = 4.987 });
        Assert.Equal(("22", "%", "4.99 GHz", Level.Normal), (r.Value, r.Unit, r.Sub, r.Level));
    }

    [Fact]
    public void Colour_follows_shown_digits()
    {
        Assert.Equal(Level.Amber, Readouts.Cpu(new Readings { CpuPercent = 79.6 }).Level);
        Assert.Equal("88", Readouts.Ram(new Readings { RamPercent = 87.6, RamUsedGb = 55.1, RamTotalGb = 62.9 }).Value);
        Assert.Equal(Level.Amber, Readouts.Ram(new Readings { RamPercent = 87.6 }).Level);
    }

    [Fact]
    public void Missing_value_shows_a_dash_never_zero()
    {
        var r = Readouts.Disk(new Readings());
        Assert.Equal(Readouts.Dash, r.Value);
        Assert.Equal(0, r.Position);
    }

    [Fact]
    public void Ram_and_net_sub_readings()
    {
        Assert.Equal("55.1/62.9 GB", Readouts.Ram(new Readings { RamPercent = 88, RamUsedGb = 55.1, RamTotalGb = 62.9 }).Sub);
        var net = Readouts.Net(new Readings { NetDownMbps = 1.8, NetUpMbps = 0.3, Online = true });
        Assert.Equal(("1.8", "Mbps", "↑ 0.3 Mbps"), (net.Value, net.Unit, net.Sub));
    }
}

public class PlacementTests
{
    static readonly RECT Main = new(0, 0, 2560, 1440);

    [Theory]
    [InlineData(Edge.Bottom, 0, 1392, 2560, 1440)]
    [InlineData(Edge.Top, 0, 0, 2560, 48)]
    [InlineData(Edge.Left, 0, 0, 48, 1440)]
    [InlineData(Edge.Right, 2512, 0, 2560, 1440)]
    public void Dock_request_is_a_strip_on_the_edge(Edge edge, int l, int t, int r, int b) =>
        Assert.Equal(new RECT(l, t, r, b), Placement.DockRequest(Main, edge, 48));

    [Fact]
    public void Fit_keeps_thickness_after_windows_moves_the_bar_above_the_taskbar()
    {
        var queried = new RECT(0, 1392, 2560, 1392); // QUERYPOS moved the bottom up to the taskbar top
        Assert.Equal(new RECT(0, 1344, 2560, 1392), Placement.Fit(queried, Edge.Bottom, 48));
    }

    [Theory]
    [InlineData(Corner.TopLeft, 16, 16)]
    [InlineData(Corner.TopRight, 2560 - 16 - 600, 16)]
    [InlineData(Corner.BottomLeft, 16, 1392 - 16 - 50)]
    [InlineData(Corner.BottomRight, 2560 - 16 - 600, 1392 - 16 - 50)]
    [InlineData(Corner.TopCenter, 980, 16)]
    [InlineData(Corner.BottomCenter, 980, 1392 - 16 - 50)]
    public void Overlay_corners_sit_inside_the_work_area(Corner corner, int x, int y) =>
        Assert.Equal((x, y), Placement.CornerPosition(new RECT(0, 0, 2560, 1392), corner, 600, 50, 16));

    [Fact]
    public void Taskbar_room_is_the_space_left_of_the_first_button()
    {
        var taskbar = new RECT(0, 1392, 2560, 1440);
        Assert.Equal(772 - 24, Placement.TaskbarRoom(taskbar, 772, 12)); // Start at 772 on a centred taskbar
        Assert.Equal(0, Placement.TaskbarRoom(taskbar, 0, 12));           // left-aligned taskbar: no room
        var second = new RECT(2560, 2500, 4000, 2560);
        Assert.Equal(400 - 30, Placement.TaskbarRoom(second, 2960, 15));
    }

    [Fact]
    public void Taskbar_gap_is_the_space_between_the_last_button_and_the_tray()
    {
        Assert.Equal(2000 - 1200 - 24, Placement.TaskbarGap(1200, 2000, 12)); // left-aligned taskbar
        Assert.Equal(0, Placement.TaskbarGap(1990, 2000, 12));                // apps fill the row
    }

    [Fact]
    public void Largest_gap_skips_the_widgets_button_next_to_the_tray()
    {
        (int, int)[] buttons = [(0, 48), (52, 100), (104, 152), (2000, 2048)]; // Start, Search, an app, Widgets
        Assert.Equal((152, 2000), Placement.LargestGap(buttons, 2060));
        Assert.Equal((152, 2060), Placement.LargestGap(buttons[..3], 2060));  // no Widgets: gap runs to the tray
    }

    [Theory]
    [InlineData(800, 0)]
    [InlineData(650, 1)]
    [InlineData(300, 3)]
    [InlineData(100, -1)]
    public void Taskbar_layout_drops_gauges_from_the_right(double room, int expected) =>
        Assert.Equal(expected, Placement.FirstFit(room, [720, 600, 450, 300, 150]));

    [Fact]
    public void Missing_screen_falls_back_to_saved_bounds_then_primary()
    {
        var a = new MonitorInfo(1, @"\\.\DISPLAY1", Main, Main, true, 1);
        var b = new MonitorInfo(2, @"\\.\DISPLAY2", new RECT(2560, 0, 4000, 2560), new RECT(2560, 0, 4000, 2512), false, 1.25);
        Assert.Same(b, Monitors.Resolve([a, b], @"\\.\DISPLAY2", null));
        Assert.Same(b, Monitors.Resolve([a, b], @"\\.\DISPLAY9", [2560, 0, 4000, 2560]));
        Assert.Same(a, Monitors.Resolve([a, b], @"\\.\DISPLAY9", null));
        Assert.Equal(60, b.Px(48));
    }
}

public class SettingsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "cluster-tests-" + Guid.NewGuid().ToString("N"));
    string File => Path.Combine(dir, "settings.json");

    [Fact]
    public void Round_trips()
    {
        var store = new SettingsStore(File);
        store.Save(new AppSettings { Mode = DisplayMode.Overlay, Corner = Corner.BottomLeft, Edge = Edge.Left, Opacity = 0.7, ClickThrough = true, Screen = @"\\.\DISPLAY2" });
        var back = store.Load();
        Assert.Equal((DisplayMode.Overlay, Corner.BottomLeft, Edge.Left, 0.7, true, @"\\.\DISPLAY2"),
            (back.Mode, back.Corner, back.Edge, back.Opacity, back.ClickThrough, back.Screen));
        Assert.Contains("\"Overlay\"", System.IO.File.ReadAllText(File)); // enums are readable strings
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        var store = new SettingsStore(File);
        Assert.Equal(DisplayMode.Taskbar, store.Load().Mode);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(File, "{ not json");
        var s = store.Load();
        Assert.Equal((DisplayMode.Taskbar, Edge.Bottom), (s.Mode, s.Edge));
    }

    [Fact]
    public void Normalize_clamps_opacity_and_bad_enums()
    {
        var s = new AppSettings { Opacity = 0.05, Mode = (DisplayMode)42 }.Normalize();
        Assert.Equal(AppSettings.MinOpacity, s.Opacity);
        Assert.Equal(DisplayMode.Taskbar, s.Mode);
    }

    [Fact]
    public void Accent_colour_is_on_by_default_and_old_files_keep_it_on()
    {
        Assert.True(new AppSettings().UseAccentColor);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(File, """{ "Mode": "Docked" }""");
        Assert.True(new SettingsStore(File).Load().UseAccentColor);

        new SettingsStore(File).Save(new AppSettings { UseAccentColor = false });
        Assert.False(new SettingsStore(File).Load().UseAccentColor);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
