// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using HWMonitor.Core.Options;
using HWMonitor.Core.Sensors;

namespace HWMonitor;

// Counter reads stay off the dock timer. Publish uses the last finished sample,
// and a second read cannot start while one is still running.
internal sealed class UsageSampler : IDisposable
{
    private readonly WindowsUsageReader _reader = new();
    private readonly object _gate = new();
    private readonly Timer _timer;
    private UsageSchedule _schedule = UsageSchedule.Start(MonitorOptions.DefaultRefreshMs, 0);
    private UsageSnapshot _latest = UsageSnapshot.Empty;
    private int _generation;
    private int _busy;
    private int _disposed;

    public UsageSampler()
    {
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public UsageSnapshot Latest => Volatile.Read(ref _latest);

    public void Start(int requestedMs)
    {
        ReplaceSchedule(requestedMs);
        Schedule(0, allowImmediate: true);
    }

    public void SetInterval(int requestedMs)
    {
        var delay = ReplaceSchedule(requestedMs);
        if (Volatile.Read(ref _busy) == 1 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Schedule(delay, allowImmediate: false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _timer.Dispose();
        _reader.Dispose();
    }

    private int ReplaceSchedule(int requestedMs)
    {
        lock (_gate)
        {
            _generation++;
            _schedule = UsageSchedule.Start(requestedMs, Environment.TickCount64);
            return _schedule.RefreshMs;
        }
    }

    private void OnTimer(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        var delay = MonitorOptions.DefaultRefreshMs;
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            delay = SampleOnce();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            if (Volatile.Read(ref _disposed) == 0)
            {
                Schedule(delay, allowImmediate: false);
            }
        }
    }

    private int SampleOnce()
    {
        UsageSchedule schedule;
        int generation;
        lock (_gate)
        {
            schedule = _schedule;
            generation = _generation;
        }

        var now = Environment.TickCount64;
        var includeGpu = schedule.IncludeGpu(now);
        var started = Stopwatch.GetTimestamp();
        UsageRead read;
        try
        {
            read = _reader.Sample(includeGpu);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            read = new UsageRead(null, [], false, includeGpu);
        }

        Publish(read);
        var duration = ElapsedMilliseconds(started);
        lock (_gate)
        {
            if (generation == _generation)
            {
                _schedule = schedule.AfterSample(now, duration, includeGpu, read.GpuFailed);
            }

            return _schedule.RefreshMs;
        }
    }

    private void Publish(UsageRead read)
    {
        var previous = Latest;
        var cpu = read.CpuPercent ?? previous.CpuPercent;
        var adapters = read.GpuUpdated ? read.Adapters : previous.Adapters;
        Volatile.Write(ref _latest, new UsageSnapshot(cpu, adapters));
    }

    private void Schedule(int delayMs, bool allowImmediate)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!allowImmediate && delayMs < 500)
        {
            delayMs = 500;
        }

        if (delayMs < 0)
        {
            delayMs = 0;
        }

        try
        {
            _timer.Change(delayMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static long ElapsedMilliseconds(long started)
    {
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (milliseconds <= 0 || double.IsNaN(milliseconds))
        {
            return 0;
        }

        if (milliseconds >= UsageSchedule.MaxGapMs)
        {
            return UsageSchedule.MaxGapMs;
        }

        return (long)milliseconds;
    }
}

internal sealed record UsageSnapshot(double? CpuPercent, IReadOnlyList<AdapterUsage> Adapters)
{
    public static UsageSnapshot Empty { get; } = new(null, []);
}
