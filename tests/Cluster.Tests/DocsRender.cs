using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cluster.Model;
using Cluster.Ui;

namespace Cluster.Tests;

/// <summary>
/// Renders the README images and the app icon offscreen from the real UI code, on a synthetic desktop
/// (no personal windows or names). Runs only when CLUSTER_DOCS is set to the repository root:
///   $env:CLUSTER_DOCS = (Get-Location); dotnet test --filter DocsRender
/// </summary>
public class DocsRender
{
    static readonly Readings Sample = new()
    {
        CpuPercent = 34, CpuGhz = 4.71, RamPercent = 87.6, RamUsedGb = 28.0, RamTotalGb = 31.9,
        DiskPercent = 12, DiskName = "Disk 0", NetDownMbps = 42, NetUpMbps = 3.1, Online = true,
    };

    static readonly Brush Wallpaper = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x23, 0x3A, 0x5E), 0), new GradientStop(Color.FromRgb(0x4B, 0x6C, 0x8F), 0.55), new GradientStop(Color.FromRgb(0x9B, 0x7E, 0x9A), 1)],
        new Point(0, 0), new Point(1, 1)));
    static readonly Brush TaskbarBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x26)));
    static readonly Brush LightTaskbarBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEF, 0xF2)));
    static readonly Brush IconBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x7C)));

    [Fact]
    public void Render()
    {
        string? root = Environment.GetEnvironmentVariable("CLUSTER_DOCS");
        if (string.IsNullOrEmpty(root)) return;
        string docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(docs);
        Sta(() =>
        {
            Save(Taskbar(1700), Path.Combine(docs, "taskbar.png"));
            Theme.Apply(Palette.Light());
            try { Save(Taskbar(1700, light: true), Path.Combine(docs, "taskbar-light.png")); }
            finally { Theme.Apply(Palette.Dark()); }
            Save(Docked(1700), Path.Combine(docs, "docked.png"));
            Save(Overlay(1100, 110), Path.Combine(docs, "overlay.png"));
            Save(Column(), Path.Combine(docs, "column.png"));
            WriteIcon(Path.Combine(root, "src", "Cluster", "Assets", "Cluster.ico"));
        });
    }

    // --- scenes ---------------------------------------------------------------------------------

    /// <summary>"In the taskbar": the cluster in the empty row left of a centred Start button.</summary>
    static FrameworkElement Taskbar(double width, bool light = false)
    {
        var canvas = new Canvas { Width = width, Height = 48 + 80, Background = Wallpaper };
        var bar = TaskbarRow(width, out _, light);
        Canvas.SetTop(bar, 80);
        canvas.Children.Add(bar);
        var view = Live(new ClusterView(vertical: false, compact: true));
        Canvas.SetLeft(view, 12);
        Canvas.SetTop(view, 80 + 0.5);
        view.Height = 48;
        canvas.Children.Add(view);
        return canvas;
    }

    /// <summary>Docked bar: a real app bar above the taskbar; the desktop shrinks to make room.</summary>
    static FrameworkElement Docked(double width)
    {
        var canvas = new Canvas { Width = width, Height = 60 + 48 + 48, Background = Wallpaper };
        var strip = new Grid { Width = width, Height = 48, Background = Theme.BarBackground };
        strip.Children.Add(new Border { BorderBrush = Theme.BarEdge, BorderThickness = new Thickness(0, 1, 0, 0) });
        strip.Children.Add(Live(new ClusterView(vertical: false)));
        Canvas.SetTop(strip, 60);
        canvas.Children.Add(strip);
        var bar = TaskbarRow(width, out _);
        Canvas.SetTop(bar, 108);
        canvas.Children.Add(bar);
        return canvas;
    }

    /// <summary>Overlay: a floating pill in a corner, over whatever is on screen.</summary>
    static FrameworkElement Overlay(double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, Background = Wallpaper };
        var pill = new Border
        {
            Background = Theme.BarBackground,
            BorderBrush = Theme.BarEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 0, 14, 0),
            Height = 50,
            Opacity = 0.92,
            Child = Live(new ClusterView(vertical: false)),
        };
        pill.Measure(new Size(double.PositiveInfinity, 50));
        Canvas.SetLeft(pill, width - 16 - pill.DesiredSize.Width);
        Canvas.SetTop(pill, 16);
        canvas.Children.Add(pill);
        return canvas;
    }

    /// <summary>Left/right edge: the same gauges as a column.</summary>
    static FrameworkElement Column()
    {
        var grid = new Grid { Width = 144, Height = 260, Background = Theme.BarBackground };
        grid.Children.Add(new Border { BorderBrush = Theme.BarEdge, BorderThickness = new Thickness(1, 0, 0, 0) });
        grid.Children.Add(Live(new ClusterView(vertical: true)));
        return grid;
    }

    static FrameworkElement TaskbarRow(double width, out double startLeft, bool light = false)
    {
        var bar = new Canvas { Width = width, Height = 48, Background = light ? LightTaskbarBrush : TaskbarBrush };
        const int icons = 7, size = 24, pitch = 44;
        startLeft = width / 2 - icons * pitch / 2.0;
        for (int i = 0; i < icons; i++)
        {
            var icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(5), Background = i == 0 ? Theme.Needle : IconBrush, Opacity = i == 0 ? 0.9 : 1 };
            Canvas.SetLeft(icon, startLeft + i * pitch + (pitch - size) / 2);
            Canvas.SetTop(icon, 12);
            bar.Children.Add(icon);
        }
        var clock = new TextBlock { Text = "12:00\n1/1/2026", FontSize = 11, Foreground = light ? Brushes.Black : Brushes.White, TextAlignment = TextAlignment.Right, FontFamily = Theme.TextFont };
        Canvas.SetLeft(clock, width - 80);
        Canvas.SetTop(clock, 8);
        bar.Children.Add(clock);
        return bar;
    }

    static ClusterView Live(ClusterView view)
    {
        view.Show(new Snapshot(DateTime.UtcNow, Sample));
        return view;
    }

    // --- output ---------------------------------------------------------------------------------

    static void Save(FrameworkElement scene, string path)
    {
        scene.Measure(new Size(scene.Width, scene.Height));
        scene.Arrange(new Rect(0, 0, scene.Width, scene.Height));
        scene.UpdateLayout();
        foreach (var dial in Dials(scene)) for (int i = 0; i < 300; i++) dial.Step(1.0 / 60);
        const double scale = 2; // crisp on high-DPI screens; the README sets display width
        var bmp = new RenderTargetBitmap((int)(scene.Width * scale), (int)(scene.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(scene);
        WritePng(bmp, path);
    }

    static void WriteIcon(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = sizes.Select(s =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(s / DialElement.Size, s / DialElement.Size));
                dc.DrawDrawing(DialElement.BuildIcon(GaugeSpec.Cpu, 0.62));
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            using var ms = new MemoryStream();
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            png.Save(ms);
            return ms.ToArray();
        }).ToArray();

        // ICO container with PNG-compressed entries (supported since Windows Vista).
        using var file = new BinaryWriter(File.Create(path));
        file.Write((short)0); file.Write((short)1); file.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            file.Write((byte)0); file.Write((byte)0);
            file.Write((short)1); file.Write((short)32);
            file.Write(images[i].Length);
            file.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) file.Write(image);
    }

    static void WritePng(BitmapSource bmp, string path)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        png.Save(file);
    }

    static IEnumerable<DialElement> Dials(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is DialElement d) yield return d;
            foreach (var x in Dials(child)) yield return x;
        }
    }

    static void Sta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Exception("Render failed", error);
    }

    static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
