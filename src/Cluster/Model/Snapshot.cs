namespace Cluster.Model;

/// <summary>Raw values from one sample. Null means the sensor could not be read (the dial shows a dash).</summary>
public readonly record struct Readings
{
    public double? CpuPercent { get; init; }
    public double? CpuGhz { get; init; }
    public double? RamPercent { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
    public double? DiskPercent { get; init; }
    public string? DiskName { get; init; }
    public string? DiskVolumes { get; init; }
    public double? NetDownMbps { get; init; }
    public double? NetUpMbps { get; init; }
    public bool Online { get; init; }
    public string? NetAdapter { get; init; }
}

public sealed record Snapshot(DateTime Time, Readings Readings);
