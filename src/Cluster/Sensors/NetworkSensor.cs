using System.Diagnostics;
using System.Net.NetworkInformation;
using Cluster.Native;

namespace Cluster.Sensors;

/// <summary>
/// Bytes in/out on the one adapter that carries the default route only (on a Hyper-V host that is the
/// vEthernet adapter), so traffic bridged through the virtual switch isn't counted twice.
/// </summary>
internal sealed class NetworkSensor : IDisposable
{
    static readonly long ResolveEvery = Stopwatch.Frequency * 10;

    NetworkInterface? nic;
    uint nicIndex;
    long lastRx, lastTx, lastTicks, nextResolve;
    bool haveBaseline;
    volatile bool dirty = true;

    public NetworkSensor()
    {
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnChanged;
    }

    void OnChanged(object? sender, EventArgs e) => dirty = true;

    public (double? DownMbps, double? UpMbps, bool Online, string? Adapter) Read()
    {
        long now = Stopwatch.GetTimestamp();
        if (dirty || now >= nextResolve) Resolve(now);
        if (nic is null) return (0, 0, false, null);

        try
        {
            var stats = nic.GetIPStatistics();
            long rx = stats.BytesReceived, tx = stats.BytesSent;
            double? down = 0, up = 0;
            if (haveBaseline)
            {
                double seconds = (now - lastTicks) / (double)Stopwatch.Frequency;
                if (seconds > 0.05)
                {
                    down = Math.Max(0, rx - lastRx) * 8 / seconds / 1e6;
                    up = Math.Max(0, tx - lastTx) * 8 / seconds / 1e6;
                }
            }
            lastRx = rx; lastTx = tx; lastTicks = now; haveBaseline = true;
            bool online = nic.OperationalStatus == OperationalStatus.Up;
            return (down, up, online, nic.Name);
        }
        catch (NetworkInformationException)
        {
            dirty = true;
            return (null, null, true, nic.Name);
        }
    }

    void Resolve(long now)
    {
        dirty = false;
        nextResolve = now + ResolveEvery;
        // 0.0.0.0 => the interface of the default route. A routing-table lookup; nothing is sent.
        if (IpHlpApi.GetBestInterface(0, out uint index) != 0)
        {
            nic = null;
            haveBaseline = false;
            return;
        }
        if (nic is not null && index == nicIndex) return;

        nic = null;
        haveBaseline = false;
        foreach (var candidate in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (IndexOf(candidate) == index)
            {
                nic = candidate;
                nicIndex = index;
                break;
            }
        }
    }

    static uint? IndexOf(NetworkInterface candidate)
    {
        try
        {
            var props = candidate.GetIPProperties();
            if (candidate.Supports(NetworkInterfaceComponent.IPv4)) return (uint)props.GetIPv4Properties().Index;
            if (candidate.Supports(NetworkInterfaceComponent.IPv6)) return (uint)props.GetIPv6Properties().Index;
        }
        catch (NetworkInformationException) { }
        return null;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnChanged;
    }
}
