using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cluster.Model;
using Cluster.Ui;

namespace Cluster.Tests;

/// <summary>Lays out the views offscreen (no window is shown).</summary>
public class LayoutTests
{
    static T OnSta<T>(Func<T> f)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = f(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Exception("STA test failed", error);
        return result;
    }

    [Fact]
    public void Compact_taskbar_layout_fits_left_of_a_centred_start_button()
    {
        // Main screen: Start at x=772 px at 100% → 748 DIP of room after 12 DIP margins.
        double full = OnSta(() => new ClusterView(vertical: false, compact: true).MeasureWidth(4));
        Assert.InRange(full, 500, 748);
    }

    [Fact]
    public void Dropping_parts_makes_it_narrower()
    {
        var widths = OnSta(() =>
        {
            var v = new ClusterView(vertical: false, compact: true);
            return new[] { v.MeasureWidth(4), v.MeasureWidth(3), v.MeasureWidth(2), v.MeasureWidth(1) };
        });
        for (int i = 1; i < widths.Length; i++) Assert.True(widths[i] < widths[i - 1], string.Join(", ", widths));
    }

    /// <summary>Set CLUSTER_RENDER=path\to\out.png to get a picture of the taskbar layout (optionally over CLUSTER_RENDER_BG).</summary>
    [Fact]
    public void Render_preview_when_requested()
    {
        string? output = Environment.GetEnvironmentVariable("CLUSTER_RENDER");
        if (string.IsNullOrEmpty(output)) return;
        string? background = Environment.GetEnvironmentVariable("CLUSTER_RENDER_BG");
        OnSta(() =>
        {
            var view = new ClusterView(vertical: false, compact: true);
            var r = new Readings { CpuPercent = 22, CpuGhz = 4.99, RamPercent = 87.6, RamUsedGb = 55.1, RamTotalGb = 62.9, DiskPercent = 78, DiskName = "Disk 1", NetDownMbps = 1.8, NetUpMbps = 0.3, Online = true };
            view.Show(new Snapshot(DateTime.UtcNow, r));
            view.SetParts(4);
            var size = new Size(1000, 48);
            view.Measure(new Size(double.PositiveInfinity, 48));
            view.Arrange(new Rect(12, 0, view.DesiredSize.Width, 48));
            view.UpdateLayout();
            // Let needles reach their targets.
            foreach (var dial in FindDials(view)) for (int i = 0; i < 300; i++) dial.Step(1.0 / 60);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (!string.IsNullOrEmpty(background) && File.Exists(background))
                {
                    var bg = new BitmapImage(new Uri(background));
                    dc.DrawImage(bg, new Rect(0, 0, bg.PixelWidth, bg.PixelHeight));
                    size = new Size(bg.PixelWidth, bg.PixelHeight);
                }
                else dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x30)), null, new Rect(size));
                dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(12, 0, view.DesiredSize.Width, 48));
            }
            var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(output);
            png.Save(file);
            return true;
        });
    }

    static IEnumerable<DialElement> FindDials(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is DialElement d) yield return d;
            foreach (var x in FindDials(child)) yield return x;
        }
    }
}
