namespace Cluster.Model;

public enum Level { Normal, Amber, Red }

public readonly record struct Zone(double From, double To, Level Level);

/// <summary>Dial geometry and printed zones. Positions are 0..1 along a 270° sweep from 135° to 405°.</summary>
public sealed class GaugeSpec
{
    public const double StartDeg = 135, SweepDeg = 270;
    public const double NetMinMbps = 0.1, NetDecades = 4; // 0.1 Mbps .. 1 Gbps

    public required string Label { get; init; }
    public double? AmberAt { get; init; }
    public double? RedAt { get; init; }
    public bool LogScale { get; init; }

    public static readonly GaugeSpec Cpu = new() { Label = "CPU", AmberAt = 80, RedAt = 90 };
    public static readonly GaugeSpec Ram = new() { Label = "RAM", AmberAt = 85, RedAt = 95 };
    public static readonly GaugeSpec Disk = new() { Label = "DISK", AmberAt = 80, RedAt = 90 };
    public static readonly GaugeSpec Net = new() { Label = "NET", LogScale = true };

    public IReadOnlyList<Zone> Zones =>
        AmberAt is double a && RedAt is double r
            ? [new Zone(a / 100, r / 100, Level.Amber), new Zone(r / 100, 1, Level.Red)]
            : [];

    /// <summary>Needle position for a value: percent for linear dials, Mbps for the log dial.</summary>
    public double Position(double value) => LogScale ? NetPosition(value) : Clamp01(value / 100);

    public Level LevelFor(double value) =>
        RedAt is double r && value >= r ? Level.Red :
        AmberAt is double a && value >= a ? Level.Amber :
        Level.Normal;

    public static double NetPosition(double mbps) =>
        !(mbps > NetMinMbps) ? 0 : Math.Min(1, Math.Log10(mbps / NetMinMbps) / NetDecades);

    /// <summary>Screen angle in degrees (clockwise from +x, y down) for a 0..1 position; clamped like a needle stop pin.</summary>
    public static double Angle(double position) => StartDeg + SweepDeg * Clamp01(position);

    static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
}
