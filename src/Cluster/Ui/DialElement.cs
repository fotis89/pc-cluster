using System.Windows;
using System.Windows.Media;
using Cluster.Model;

namespace Cluster.Ui;

/// <summary>
/// The flat Fluent 270° dial (design option A) with the unused bottom cropped.
/// The face is built once into a frozen drawing; only the needle's RotateTransform changes, so WPF
/// re-composes without calling OnRender again.
/// </summary>
internal sealed class DialElement : FrameworkElement
{
    public const double Size = 52;
    static readonly double R = Size / 2 - 1.5, C = Size / 2;
    static readonly double TrackR = R - 3;
    public static readonly double CroppedHeight = Math.Ceiling(C + R * 0.72 + 3); // 47

    static readonly Geometry FaceClip = Frozen(new RectangleGeometry(new Rect(0, 0, Size, CroppedHeight)));

    readonly Palette palette;
    readonly Drawing face;
    readonly Pen needlePen, hubPen;
    readonly RotateTransform rotation = new(GaugeSpec.StartDeg, C, C);
    readonly NeedleDamper damper = new();

    public DialElement(GaugeSpec spec)
    {
        palette = Theme.Current;
        face = BuildFace(spec, palette);
        (needlePen, hubPen) = NeedlePens(palette);
        Width = Size;
        Height = CroppedHeight;
        SnapsToDevicePixels = false;
    }

    /// <summary>Sets where the needle should go (0..1); the damper swings it there.</summary>
    public void SetTarget(double position)
    {
        if (Math.Abs(damper.Target - position) < 1e-4) return;
        damper.Target = position;
        NeedleAnimator.Kick(this);
    }

    /// <summary>One animation frame. Returns false when the needle has come to rest.</summary>
    public bool Step(double seconds)
    {
        bool moving = damper.Step(seconds);
        rotation.Angle = GaugeSpec.Angle(damper.Position);
        return moving;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(FaceClip);
        dc.DrawDrawing(face);
        DrawNeedle(dc, rotation, palette, needlePen, hubPen);
        dc.Pop();
    }

    static (Pen Needle, Pen Hub) NeedlePens(Palette p) => (
        Frozen(new Pen(p.Accent, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }),
        Frozen(new Pen(p.Accent, 1.4)));

    static void DrawNeedle(DrawingContext dc, Transform rotation, Palette p, Pen needle, Pen hub)
    {
        dc.PushTransform(rotation);
        dc.DrawLine(needle, new Point(C - 3, C), new Point(C + R - 4, C));
        dc.Pop();
        dc.DrawEllipse(p.BarBackground, hub, new Point(C, C), 3, 3);
    }

    public static Drawing BuildFace(GaugeSpec spec, Palette p)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var center = new Point(C, C);
            dc.DrawEllipse(p.DialFace, new Pen(p.DialRim, 1), center, R, R);

            var track = new Pen(p.DialTrack, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, track, Arc(TrackR, 0, 1));

            foreach (var zone in spec.Zones)
            {
                var pen = new Pen(zone.Level == Level.Red ? p.Red : p.Amber, 2)
                {
                    EndLineCap = zone.To >= 1 ? PenLineCap.Round : PenLineCap.Flat,
                };
                dc.DrawGeometry(null, pen, Arc(TrackR, zone.From, zone.To));
            }

            var tick = new Pen(p.Tick, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int i = 0; i <= 4; i++)
            {
                double angle = GaugeSpec.Angle(i / 4.0);
                dc.DrawLine(tick, Polar(R - 6, angle), Polar(R - 8.5, angle));
            }
        }
        group.Freeze();
        return group;
    }

    /// <summary>A whole (uncropped) dial with the needle at <paramref name="position"/>, for the tray icon. Always the fixed icon palette.</summary>
    public static Drawing BuildIcon(GaugeSpec spec, double position)
    {
        var p = Theme.Icon;
        var (needle, hub) = NeedlePens(p);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawEllipse(p.BarBackground, null, new Point(C, C), R, R);
            dc.DrawDrawing(BuildFace(spec, p));
            DrawNeedle(dc, new RotateTransform(GaugeSpec.Angle(position), C, C), p, needle, hub);
        }
        group.Freeze();
        return group;
    }

    static Point Polar(double radius, double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(C + radius * Math.Cos(a), C + radius * Math.Sin(a));
    }

    static Geometry Arc(double radius, double from, double to)
    {
        double a0 = GaugeSpec.Angle(from), a1 = GaugeSpec.Angle(to);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Polar(radius, a0), isFilled: false, isClosed: false);
            ctx.ArcTo(Polar(radius, a1), new Size(radius, radius), 0, a1 - a0 > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>Drives needle swings from CompositionTarget.Rendering, subscribed only while a needle moves.</summary>
internal static class NeedleAnimator
{
    static readonly List<DialElement> active = [];
    static TimeSpan? last;
    static bool hooked;

    public static void Kick(DialElement dial)
    {
        if (!active.Contains(dial)) active.Add(dial);
        if (hooked) return;
        last = null;
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        hooked = true;
    }

    static void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        double dt = last is TimeSpan l ? (time - l).TotalSeconds : 1.0 / 60;
        if (dt <= 0) return; // Rendering can fire more than once per frame
        last = time;

        for (int i = active.Count - 1; i >= 0; i--)
            if (!active[i].Step(dt)) active.RemoveAt(i);

        if (active.Count == 0)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            hooked = false;
        }
    }

    public static void Forget(DialElement dial) => active.Remove(dial);
}
