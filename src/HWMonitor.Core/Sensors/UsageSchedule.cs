// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Options;

namespace HWMonitor.Core.Sensors;

public static class FailureBackoff
{
    public static int DelayMs(int failures, int initialMs, int maxMs)
    {
        if (failures <= 0 || initialMs <= 0 || maxMs <= 0)
        {
            return 0;
        }

        if (initialMs >= maxMs)
        {
            return maxMs;
        }

        var delay = initialMs;
        var steps = Math.Min(failures - 1, 16);
        for (var i = 0; i < steps; i++)
        {
            if (delay > maxMs / 2)
            {
                return maxMs;
            }

            delay *= 2;
        }

        return delay > maxMs ? maxMs : delay;
    }
}

public readonly record struct UsageSchedule(int RefreshMs, long NextGpuMs, int FailureStreak)
{
    public const int SlowSampleMs = 20;
    public const int MaxGapMs = 30_000;
    public const int DutyMultiplier = 50;
    private const int MaxFailureStreak = 6;

    public static UsageSchedule Start(int requestedMs, long nowMs)
    {
        var refresh = MonitorOptions.ClampRefresh(requestedMs);
        return new UsageSchedule(refresh, nowMs, 0);
    }

    public bool IncludeGpu(long nowMs) => nowMs >= NextGpuMs;

    public UsageSchedule AfterSample(long nowMs, long durationMs, bool includedGpu, bool failed)
    {
        if (!includedGpu)
        {
            return this;
        }

        if (durationMs < 0)
        {
            durationMs = 0;
        }

        if (!failed)
        {
            return new UsageSchedule(RefreshMs, nowMs + DutyPeriod(durationMs), 0);
        }

        var streak = Math.Min(FailureStreak + 1, MaxFailureStreak);
        var retry = FailureBackoff.DelayMs(streak + 1, RefreshMs, MaxGapMs);
        var period = Math.Max(retry, DutyPeriod(durationMs));
        return new UsageSchedule(RefreshMs, nowMs + period, streak);
    }

    private long DutyPeriod(long durationMs)
    {
        if (durationMs <= SlowSampleMs)
        {
            return RefreshMs;
        }

        var scaled = durationMs > MaxGapMs / DutyMultiplier
            ? MaxGapMs
            : durationMs * DutyMultiplier;
        if (scaled < RefreshMs)
        {
            return RefreshMs;
        }

        return scaled > MaxGapMs ? MaxGapMs : scaled;
    }
}
