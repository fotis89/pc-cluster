using System.Runtime.InteropServices;

namespace Cluster.Native;

internal static class Pdh
{
    public const uint ERROR_SUCCESS = 0, PDH_MORE_DATA = 0x800007D2;
    public const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;
    public const uint PDH_CSTATUS_VALID_DATA = 0, PDH_CSTATUS_NEW_DATA = 1;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);
    // English counter paths work whatever the Windows display language is.
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);
    [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] public static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);
    [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(nint query);
}

/// <summary>One PDH query; all counters are collected together once per sample.</summary>
internal sealed class PdhQuery : IDisposable
{
    nint handle;

    public PdhQuery()
    {
        if (Pdh.PdhOpenQueryW(null, 0, out handle) != Pdh.ERROR_SUCCESS)
            handle = 0;
    }

    public PdhCounter? TryAdd(string englishPath)
    {
        if (handle == 0) return null;
        return Pdh.PdhAddEnglishCounterW(handle, englishPath, 0, out var counter) == Pdh.ERROR_SUCCESS
            ? new PdhCounter(counter)
            : null;
    }

    public bool Collect() => handle != 0 && Pdh.PdhCollectQueryData(handle) == Pdh.ERROR_SUCCESS;

    public void Dispose()
    {
        if (handle != 0) Pdh.PdhCloseQuery(handle);
        handle = 0;
    }
}

internal sealed class PdhCounter
{
    const int ItemSize = 24; // PDH_FMT_COUNTERVALUE_ITEM_W on x64: name pointer + 16-byte value
    readonly nint handle;
    nint buffer;
    uint bufferSize;

    public PdhCounter(nint handle) => this.handle = handle;

    /// <summary>Formatted value, or null when PDH has no valid data (for example the first rate sample).</summary>
    public double? Read()
    {
        uint status = Pdh.PdhGetFormattedCounterValue(handle, Pdh.PDH_FMT_DOUBLE | Pdh.PDH_FMT_NOCAP100, out _, out var value);
        if (status != Pdh.ERROR_SUCCESS || value.CStatus > Pdh.PDH_CSTATUS_NEW_DATA) return null;
        return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
    }

    /// <summary>Reads every instance of a wildcard counter into <paramref name="into"/>. Returns false on failure.</summary>
    public bool ReadArray(List<(string Name, double Value)> into)
    {
        into.Clear();
        const uint format = Pdh.PDH_FMT_DOUBLE | Pdh.PDH_FMT_NOCAP100;
        uint size = bufferSize;
        uint status = Pdh.PdhGetFormattedCounterArrayW(handle, format, ref size, out uint count, buffer);
        if (status == Pdh.PDH_MORE_DATA)
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            bufferSize = size + 512;
            buffer = Marshal.AllocHGlobal((int)bufferSize);
            size = bufferSize;
            status = Pdh.PdhGetFormattedCounterArrayW(handle, format, ref size, out count, buffer);
        }
        if (status != Pdh.ERROR_SUCCESS) return false;

        for (int i = 0; i < count; i++)
        {
            nint item = buffer + i * ItemSize;
            uint cstatus = (uint)Marshal.ReadInt32(item, 8);
            if (cstatus > Pdh.PDH_CSTATUS_NEW_DATA) continue;
            string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
            double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
            if (double.IsFinite(value)) into.Add((name, value));
        }
        return true;
    }
}
