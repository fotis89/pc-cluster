using Cluster.Native;
using Cluster.Ui;

namespace Cluster.Tests;

/// <summary>Reads the real taskbar through UI Automation (no windows shown, no input). Skips when there is no taskbar.</summary>
public class TaskbarProbeTests
{
    [Fact]
    public void Finds_the_first_button_on_the_main_taskbar()
    {
        // CI runners have no interactive desktop or Explorer taskbar.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") return;
        var main = Monitors.All().FirstOrDefault(m => m.Primary);
        if (main is null) return;
        nint taskbar = TaskbarProbe.FindTaskbar(main.Handle);
        if (taskbar == 0 || !User32.GetWindowRect(taskbar, out var bounds)) return;

        int? left = TaskbarProbe.OccupiedLeft(taskbar, bounds);

        Assert.NotNull(left);
        Assert.InRange(left!.Value, bounds.Left, bounds.Right);
    }
}
