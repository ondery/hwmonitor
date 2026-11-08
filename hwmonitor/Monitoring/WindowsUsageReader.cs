// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using HWMonitor.Core.Sensors;
using HWMonitor.Hardware;

namespace HWMonitor;

internal readonly record struct UsageRead(
    double? CpuPercent,
    IReadOnlyList<AdapterUsage> Adapters,
    bool GpuUpdated,
    bool GpuFailed);

// CPU is one total counter and one read per sample. GPU utilization comes
// from a single category snapshot; NextValue on each engine repeats that
// read, so engine counters are not kept. Departed engines drop their
// baseline. Display adapters are refreshed about once a minute.
internal sealed class WindowsUsageReader : IDisposable
{
    private const long AdapterRefreshMs = 45_000;
    private static readonly IReadOnlyList<AdapterUsage> NoAdapters = [];

    private readonly object _gate = new();
    private readonly Dictionary<string, GpuBaseline> _baselines = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceCounter? _cpu;
    private IReadOnlyList<AdapterDescriptor> _adapters = [];
    private long _adaptersStamp;
    private bool _adaptersKnown;
    private bool _cpuPrimed;
    private int _cpuFailures;
    private long _cpuBlockedUntil;
    private int _gpuFailuresLogged;
    private int _unsupportedLogged;
    private bool _disposed;

    public UsageRead Sample(bool includeGpu)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return new UsageRead(null, NoAdapters, false, false);
            }

            var cpu = ReadCpu();
            if (!includeGpu)
            {
                return new UsageRead(cpu, NoAdapters, false, false);
            }

            try
            {
                var (adapters, updated) = ReadGpu();
                _gpuFailuresLogged = 0;
                return new UsageRead(cpu, adapters, updated, false);
            }
            catch (Exception ex)
            {
                if (_gpuFailuresLogged < 3)
                {
                    _gpuFailuresLogged++;
                    ExtensionLog.Write(ex.Message);
                }

                return new UsageRead(cpu, NoAdapters, false, true);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _baselines.Clear();
            _cpu?.Dispose();
            _cpu = null;
        }
    }

    private (IReadOnlyList<AdapterUsage> Adapters, bool Updated) ReadGpu()
    {
        var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
        var utilization = FindUtilization(data)
            ?? throw new InvalidOperationException("GPU Engine utilization counter is unavailable.");
        var retained = new Dictionary<string, CounterSample>(utilization.Count, StringComparer.OrdinalIgnoreCase);
        var readings = new List<RawEngineReading>(utilization.Count);
        foreach (var value in utilization.Values)
        {
            if (value is not InstanceData instance)
            {
                continue;
            }

            var name = instance.InstanceName;
            if (!GpuEngineUsage.Is3DEngine(name) || !retained.TryAdd(name, instance.Sample))
            {
                continue;
            }

            readings.Add(new RawEngineReading(name, ToPoint(instance.Sample)));
        }

        var previous = new Dictionary<string, RawCounterPoint>(_baselines.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _baselines)
        {
            previous.Add(pair.Key, pair.Value.Point);
        }

        var samples = new List<EngineSample>();
        var nextPoints = EngineBaselines.Advance(
            previous,
            readings,
            samples,
            (name, _, _) => FormatUnsupported(name, retained));
        var next = new Dictionary<string, GpuBaseline>(nextPoints.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nextPoints)
        {
            if (retained.TryGetValue(pair.Key, out var sample))
            {
                next.Add(pair.Key, new GpuBaseline(pair.Value, sample));
            }
        }

        _baselines.Clear();
        foreach (var pair in next)
        {
            _baselines.Add(pair.Key, pair.Value);
        }

        // The first snapshot of a live engine is only a baseline. Publishing
        // it would wipe the last real percentage. An empty engine list is real.
        var publish = samples.Count > 0 || readings.Count == 0;
        return (GpuEngineUsage.Aggregate(samples, Adapters()), publish);
    }

    private double? FormatUnsupported(string name, Dictionary<string, CounterSample> retained)
    {
        if (!_baselines.TryGetValue(name, out var baseline) || !retained.TryGetValue(name, out var sample))
        {
            return null;
        }

        try
        {
            var formatted = CounterSample.Calculate(baseline.Sample, sample);
            if (float.IsNaN(formatted) || float.IsInfinity(formatted) || formatted < 0)
            {
                return null;
            }

            return formatted;
        }
        catch (Exception)
        {
            LogUnsupported((int)sample.CounterType);
            return null;
        }
    }

    private double? ReadCpu()
    {
        var now = Environment.TickCount64;
        if (now < _cpuBlockedUntil)
        {
            return null;
        }

        try
        {
            _cpu ??= OpenCpu();
            if (_cpu is null)
            {
                NoteCpuFailure(now);
                return null;
            }

            var value = _cpu.NextValue();
            _cpuFailures = 0;
            _cpuBlockedUntil = 0;
            if (!_cpuPrimed)
            {
                _cpuPrimed = true;
                return null;
            }

            return value;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            _cpu?.Dispose();
            _cpu = null;
            _cpuPrimed = false;
            NoteCpuFailure(now);
            return null;
        }
    }

    private void NoteCpuFailure(long now)
    {
        _cpuFailures = Math.Min(_cpuFailures + 1, 8);
        _cpuBlockedUntil = now + FailureBackoff.DelayMs(_cpuFailures, 1_000, 15_000);
    }

    private IReadOnlyList<AdapterDescriptor> Adapters()
    {
        var now = Environment.TickCount64;
        if (_adaptersKnown && now - _adaptersStamp < AdapterRefreshMs)
        {
            return _adapters;
        }

        _adapters = DisplayAdapters.TryEnumerate(ExtensionLog.Write);
        _adaptersStamp = now;
        _adaptersKnown = true;
        return _adapters;
    }

    private void LogUnsupported(int counterType)
    {
        if (Interlocked.Exchange(ref _unsupportedLogged, 1) != 0)
        {
            return;
        }

        ExtensionLog.Write($"GPU utilization counter type {counterType} is not supported.");
    }

    private static InstanceDataCollection? FindUtilization(InstanceDataCollectionCollection data)
    {
        foreach (var value in data.Values)
        {
            if (value is InstanceDataCollection collection
                && string.Equals(collection.CounterName, "Utilization Percentage", StringComparison.OrdinalIgnoreCase))
            {
                return collection;
            }
        }

        return null;
    }

    private static RawCounterPoint ToPoint(CounterSample sample) => new(
        sample.RawValue,
        sample.BaseValue,
        sample.TimeStamp,
        sample.TimeStamp100nSec,
        sample.CounterFrequency,
        sample.SystemFrequency,
        (int)sample.CounterType);

    private static PerformanceCounter? OpenCpu()
    {
        var utility = Open("Processor Information", "% Processor Utility");
        if (utility is not null)
        {
            return utility;
        }

        return Open("Processor", "% Processor Time");
    }

    private static PerformanceCounter? Open(string category, string counter)
    {
        try
        {
            return new PerformanceCounter(category, counter, "_Total", readOnly: true);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return null;
        }
    }

    private readonly record struct GpuBaseline(RawCounterPoint Point, CounterSample Sample);
}
