// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using HWMonitor.Core.Sensors;
using HWMonitor.Hardware;

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
        _adapters = DisplayAdapters.TryEnumerate(ExtensionLog.Write);
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
