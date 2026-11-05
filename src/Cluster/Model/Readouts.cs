using System.Globalization;

namespace Cluster.Model;

/// <summary>What one gauge tile shows: big value + unit, a small sub-reading, colour level and needle position.</summary>
public readonly record struct Readout(string Value, string Unit, string Sub, Level Level, double Position, string Tooltip);

public static class Readouts
{
    public const string Dash = "—";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Readout Cpu(in Readings r)
    {
        if (r.CpuPercent is not double cpu) return Missing("CPU");
        string ghz = r.CpuGhz is double g ? g.ToString("0.00", Inv) + " GHz" : "";
        return new(Pct(cpu), "%", ghz, GaugeSpec.Cpu.LevelFor(Shown(cpu)), GaugeSpec.Cpu.Position(cpu),
            $"CPU {Pct(cpu)}%" + (ghz.Length > 0 ? " · " + ghz : ""));
    }

    public static Readout Ram(in Readings r)
    {
        if (r.RamPercent is not double ram) return Missing("Memory");
        string sub = r.RamUsedGb is double u && r.RamTotalGb is double t
            ? u.ToString("0.0", Inv) + "/" + t.ToString("0.0", Inv) + " GB" : "";
        return new(Pct(ram), "%", sub, GaugeSpec.Ram.LevelFor(Shown(ram)), GaugeSpec.Ram.Position(ram),
            $"Memory {Pct(ram)}% in use" + (sub.Length > 0 ? " · " + sub : ""));
    }

    public static Readout Disk(in Readings r)
    {
        if (r.DiskPercent is not double disk) return Missing("Disk");
        string name = r.DiskName ?? "";
        string tip = $"Busiest disk {Pct(disk)}% active" + (name.Length > 0 ? " · " + name : "") +
                     (string.IsNullOrEmpty(r.DiskVolumes) ? "" : " (" + r.DiskVolumes + ")");
        return new(Pct(disk), "%", name, GaugeSpec.Disk.LevelFor(Shown(disk)), GaugeSpec.Disk.Position(disk), tip);
    }

    public static Readout Net(in Readings r)
    {
        if (r.NetDownMbps is not double down) return Missing("Network");
        var (dv, du) = Rate(down);
        string sub = r.NetUpMbps is double up ? "↑ " + Join(Rate(up)) : "";
        string tip = $"Network ↓ {dv} {du}" + (r.NetUpMbps is double u2 ? $" · ↑ {Join(Rate(u2))}" : "") +
                     (r.Online ? (r.NetAdapter is null ? "" : " · " + r.NetAdapter) : " · offline");
        return new(dv, du, sub, Level.Normal, GaugeSpec.Net.Position(down), tip);
    }

    /// <summary>Formats a bit rate given in Mbps: 0.0–9.9 and 10–999 Mbps, then Gbps.</summary>
    public static (string Value, string Unit) Rate(double mbps)
    {
        if (!(mbps > 0)) return ("0.0", "Mbps");
        if (mbps >= 999.5) return ((mbps / 1000).ToString(mbps >= 9995 ? "0" : "0.0", Inv), "Gbps");
        if (mbps >= 9.95) return (mbps.ToString("0", Inv), "Mbps");
        return (mbps.ToString("0.0", Inv), "Mbps");
    }

    public static string TrayTip(in Readings r)
    {
        string Part(string label, double? v) => v is double d ? $"{label} {Pct(d)}%" : $"{label} {Dash}";
        string net = r.NetDownMbps is double d ? $"↓{Join(Rate(d))}" + (r.NetUpMbps is double u ? $" ↑{Join(Rate(u))}" : "") : $"Net {Dash}";
        return $"Cluster\n{Part("CPU", r.CpuPercent)} · {Part("RAM", r.RamPercent)} · {Part("Disk", r.DiskPercent)}\n{net}";
    }

    static string Join((string Value, string Unit) rate) => rate.Value + " " + rate.Unit;
    // Colour follows the digits shown, so "80" is never white while "79.6" would be.
    static double Shown(double v) => Math.Round(Math.Clamp(v, 0, 100));
    static string Pct(double v) => Shown(v).ToString("0", Inv);
    static Readout Missing(string what) => new(Dash, "", "", Level.Normal, 0, what + ": sensor unavailable");
}
