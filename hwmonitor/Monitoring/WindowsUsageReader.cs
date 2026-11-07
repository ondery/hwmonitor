// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using HWMonitor.Core.Sensors;

namespace HWMonitor;

internal sealed class WindowsUsageReader : IDisposable
{
    private readonly Dictionary<string, PerformanceCounter> _engines = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceCounter? _cpu;
    private IReadOnlyList<AdapterDescriptor> _adapters = [];
    private bool _ready;

    public (double? CpuPercent, IReadOnlyList<AdapterUsage> Adapters) Sample()
    {
        Ensure();
        double? cpu = null;
        try
        {
            if (_cpu is not null)
            {
                cpu = _cpu.NextValue();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            _cpu?.Dispose();
            _cpu = null;
        }

        var samples = new List<EngineSample>();
        string[] names = [];
        try
        {
            names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            {
                wanted.Add(name);
            }
        }

        foreach (var stale in _engines.Keys.Where(key => !wanted.Contains(key)).ToArray())
        {
            _engines[stale].Dispose();
            _engines.Remove(stale);
        }

        foreach (var name in wanted)
        {
            if (!_engines.TryGetValue(name, out var counter))
            {
                try
                {
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
                    _engines.Add(name, counter);
                }
                catch (Exception ex)
                {
                    ExtensionLog.Write(ex.Message);
                    continue;
                }
            }

            try
            {
                samples.Add(new EngineSample(name, counter.NextValue()));
            }
            catch (Exception ex)
            {
                ExtensionLog.Write(ex.Message);
            }
        }

        return (cpu, GpuEngineUsage.Aggregate(samples, _adapters));
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        foreach (var counter in _engines.Values)
        {
            counter.Dispose();
        }

        _engines.Clear();
    }

    private void Ensure()
    {
        if (_ready)
        {
            return;
        }

        _ready = true;
        _cpu = OpenCpu();
        _adapters = DxgiAdapters.Enumerate();
        try
        {
            _cpu?.NextValue();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }
    }

    private static PerformanceCounter? OpenCpu()
    {
        try
        {
            return new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }

        try
        {
            return new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return null;
        }
    }
}

internal static class DxgiAdapters
{
    public static IReadOnlyList<AdapterDescriptor> Enumerate()
    {
        var results = new List<AdapterDescriptor>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory == 0)
        {
            return results;
        }

        try
        {
            var enumerate = GetDelegate<EnumAdapters1Delegate>(factory, 12);
            var release = GetDelegate<ReleaseDelegate>(factory, 2);
            for (uint index = 0; ; index++)
            {
                var hr = enumerate(factory, index, out var adapter);
                if (hr < 0 || adapter == 0)
                {
                    break;
                }

                try
                {
                    if (TryDescribe(adapter, out var descriptor))
                    {
                        results.Add(descriptor);
                    }
                }
                finally
                {
                    GetDelegate<ReleaseDelegate>(adapter, 2)(adapter);
                }
            }

            release(factory);
            factory = 0;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }
        finally
        {
            if (factory != 0)
            {
                Marshal.Release(factory);
            }
        }

        return results;
    }

    private static bool TryDescribe(nint adapter, out AdapterDescriptor descriptor)
    {
        descriptor = new AdapterDescriptor(0, 0, string.Empty, GpuClass.None);
        var getDesc = GetDelegate<GetDesc1Delegate>(adapter, 10);
        var size = Marshal.SizeOf<DxgiAdapterDesc1>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (getDesc(adapter, buffer) < 0)
            {
                return false;
            }

            var desc = Marshal.PtrToStructure<DxgiAdapterDesc1>(buffer);
            if ((desc.Flags & 2) != 0 || desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            descriptor = new AdapterDescriptor(
                desc.LuidLow,
                unchecked((uint)desc.LuidHigh),
                desc.Description.Trim(),
                ClassFromVendor(desc.VendorId, desc.Description));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static GpuClass ClassFromVendor(uint vendor, string description)
    {
        var arc = description.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        return vendor switch
        {
            0x10DE => GpuClass.Discrete,
            0x1002 => GpuClass.Discrete,
            0x8086 => arc ? GpuClass.Discrete : GpuClass.Integrated,
            _ => GpuClass.None,
        };
    }

    private static T GetDelegate<T>(nint instance, int slot)
        where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, slot * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint self, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint self, nint desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint self);
}
