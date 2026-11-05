using System.Windows;
using System.Windows.Media;
using Cluster.Model;

namespace Cluster.Ui;

/// <summary>
/// Design B's 270° dial with the unused bottom cropped (Option A, <c>tq()</c> in slim-options.html).
/// The face is built once into a frozen drawing; only the needle's RotateTransform changes, so WPF
/// re-composes without calling OnRender again.
/// </summary>
internal sealed class DialElement : FrameworkElement
{
    public const double Size = 52;
    static readonly double R = Size / 2 - 1.5, C = Size / 2;
    public static readonly double CroppedHeight = Math.Ceiling(C + R * 0.72 + 3); // 47

    static readonly Pen NeedlePen = Frozen(new Pen(Theme.Needle, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Geometry FaceClip = Frozen(new RectangleGeometry(new Rect(0, 0, Size, CroppedHeight)));

    readonly Drawing face;
    readonly RotateTransform rotation = new(GaugeSpec.StartDeg, C, C);
    readonly NeedleDamper damper = new();

    public DialElement(GaugeSpec spec)
    {
        face = BuildFace(spec);
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
        DrawNeedle(dc, rotation);
        dc.Pop();
    }

    static void DrawNeedle(DrawingContext dc, Transform rotation)
    {
        dc.PushTransform(rotation);
        dc.DrawLine(NeedlePen, new Point(C, C), new Point(C + R - 3, C));
        dc.Pop();
        dc.DrawEllipse(Theme.Hub, null, new Point(C, C), 2.2, 2.2);
    }

    public static Drawing BuildFace(GaugeSpec spec)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var center = new Point(C, C);
            dc.DrawEllipse(Theme.Face, null, center, R, R);
            dc.DrawEllipse(null, new Pen(Theme.Chrome, 1.2), center, R, R);

            foreach (var zone in spec.Zones)
                dc.DrawGeometry(null, new Pen(zone.Level == Level.Red ? Theme.Red : Theme.Amber, 2.5), Arc(R - 2.5, zone.From, zone.To));

            var major = new Pen(Theme.TickMajor, 1.1);
            var minor = new Pen(Theme.TickMinor, 0.8);
            for (int i = 0; i <= 10; i++)
            {
                bool isMajor = i % 5 == 0;
                double angle = GaugeSpec.Angle(i / 10.0);
                dc.DrawLine(isMajor ? major : minor, Polar(R - 1, angle), Polar(R - (isMajor ? 6 : 4), angle));
            }
        }
        group.Freeze();
        return group;
    }

    /// <summary>A whole (uncropped) dial with the needle at <paramref name="position"/>, for the tray icon.</summary>
    public static Drawing BuildIcon(GaugeSpec spec, double position)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawDrawing(BuildFace(spec));
            DrawNeedle(dc, new RotateTransform(GaugeSpec.Angle(position), C, C));
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
