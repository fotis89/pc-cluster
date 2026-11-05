using Cluster.Native;

namespace Cluster.Sensors;

/// <summary>CPU utilisation as Task Manager shows it, plus effective clock = base frequency × % performance.</summary>
internal sealed class CpuSensor
{
    readonly PdhCounter? utility, performance, frequency;

    public CpuSensor(PdhQuery query)
    {
        utility = query.TryAdd(@"\Processor Information(_Total)\% Processor Utility")
                  ?? query.TryAdd(@"\Processor Information(_Total)\% Processor Time");
        performance = query.TryAdd(@"\Processor Information(_Total)\% Processor Performance");
        frequency = query.TryAdd(@"\Processor Information(_Total)\Processor Frequency");
    }

    public (double? Percent, double? Ghz) Read()
    {
        double? pct = utility?.Read() is double u ? Math.Clamp(u, 0, 100) : null;
        double? ghz = frequency?.Read() is double mhz && performance?.Read() is double perf && mhz > 0
            ? mhz * perf / 100 / 1000
            : null;
        return (pct, ghz);
    }
}

internal sealed class MemorySensor
{
    public (double? Percent, double? UsedGb, double? TotalGb) Read()
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Kernel32.MEMORYSTATUSEX>() };
        if (!Kernel32.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return (null, null, null);
        const double Gb = 1024.0 * 1024 * 1024;
        double total = status.ullTotalPhys / Gb;
        double used = (status.ullTotalPhys - status.ullAvailPhys) / Gb;
        return (used / total * 100, used, total);
    }
}

/// <summary>Busiest physical disk: active = 100 − % Idle Time, which never exceeds 100% (raw % Disk Time can).</summary>
internal sealed class DiskSensor
{
    readonly PdhCounter? idle;
    readonly List<(string Name, double Value)> items = [];

    public DiskSensor(PdhQuery query) => idle = query.TryAdd(@"\PhysicalDisk(*)\% Idle Time");

    public (double? Percent, string? Name, string? Volumes) Read()
    {
        if (idle is null || !idle.ReadArray(items)) return (null, null, null);
        double best = -1;
        string? bestName = null;
        foreach (var (name, value) in items)
        {
            if (name == "_Total") continue;
            double active = Math.Clamp(100 - value, 0, 100);
            if (active > best) { best = active; bestName = name; }
        }
        if (bestName is null) return (null, null, null);
        // Instance names look like "0 C: D:" — disk number, then its volumes.
        int space = bestName.IndexOf(' ');
        string number = space < 0 ? bestName : bestName[..space];
        string volumes = space < 0 ? "" : bestName[(space + 1)..].Trim();
        return (best, "Disk " + number, volumes);
    }
}
