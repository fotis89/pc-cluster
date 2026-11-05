using System.Diagnostics;
using Cluster.Model;
using Cluster.Native;
using Cluster.Settings;

namespace Cluster.Sensors;

/// <summary>Reads every sensor once a second on its own background thread and raises <see cref="Sampled"/>.</summary>
internal sealed class Sampler : IDisposable
{
    const int PeriodMs = 1000;

    readonly ManualResetEventSlim stop = new();
    readonly Thread thread;

    public event Action<Snapshot>? Sampled;

    public Sampler()
    {
        thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Cluster sampler",
            Priority = ThreadPriority.BelowNormal,
        };
    }

    public void Start() => thread.Start();

    void Loop()
    {
        using var query = new PdhQuery();
        var cpu = new CpuSensor(query);
        var memory = new MemorySensor();
        var disk = new DiskSensor(query);
        using var network = new NetworkSensor();

        query.Collect(); // rate counters need two collections
        network.Read();
        var clock = Stopwatch.StartNew();

        while (!stop.Wait(PeriodMs - (int)(clock.ElapsedMilliseconds % PeriodMs)))
        {
            try
            {
                bool collected = query.Collect();
                var (cpuPct, ghz) = collected ? cpu.Read() : (null, null);
                var (ramPct, used, total) = memory.Read();
                var (diskPct, diskName, volumes) = collected ? disk.Read() : (null, null, null);
                var (down, up, online, adapter) = network.Read();

                var readings = new Readings
                {
                    CpuPercent = cpuPct, CpuGhz = ghz,
                    RamPercent = ramPct, RamUsedGb = used, RamTotalGb = total,
                    DiskPercent = diskPct, DiskName = diskName, DiskVolumes = volumes,
                    NetDownMbps = down, NetUpMbps = up, Online = online, NetAdapter = adapter,
                };
                Sampled?.Invoke(new Snapshot(DateTime.UtcNow, readings));
            }
            catch (Exception ex)
            {
                Log.Write("Sampler", ex);
            }
        }
    }

    public void Dispose()
    {
        stop.Set();
        if (thread.ThreadState != System.Threading.ThreadState.Unstarted) thread.Join(2000);
        stop.Dispose();
    }
}
