// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Options;
using HWMonitor.Core.Sensors;
using Xunit;

namespace HWMonitor.Tests;

public class UsageSamplingTests
{
    [Fact]
    public void Hundred_nanosecond_timer_is_the_share_of_busy_time()
    {
        var result = CounterDelta.Compute(Timer(0, 1_000), Timer(5_000_000, 1_000 + 10_000_000));
        Assert.True(result.Supported);
        Assert.Equal(50, result.Value, 3);
    }

    [Fact]
    public void Inverse_timer_treats_the_raw_delta_as_idle_time()
    {
        var result = CounterDelta.Compute(Idle(0, 0), Idle(2_000_000, 10_000_000));
        Assert.Equal(80, result.Value, 3);
        var idle = CounterDelta.Compute(Idle(0, 0), Idle(10_000_000, 10_000_000));
        Assert.True(idle.HasValue);
        Assert.Equal(0, idle.Value, 3);
    }

    [Fact]
    public void Precision_timer_uses_the_base_counter_delta()
    {
        var previous = new RawCounterPoint(0, 0, 0, 0, 0, 0, CounterDelta.PerfPrecision100NsTimer);
        var current = new RawCounterPoint(150, 100, 0, 0, 0, 0, CounterDelta.PerfPrecision100NsTimer);
        var result = CounterDelta.Compute(previous, current);
        Assert.Equal(150, result.Value, 3);
    }

    [Fact]
    public void Raw_fraction_does_not_need_an_earlier_sample()
    {
        var current = new RawCounterPoint(1, 4, 0, 0, 0, 0, CounterDelta.PerfRawFraction);
        var result = CounterDelta.Compute(default, current);
        Assert.Equal(25, result.Value, 3);
        Assert.False(CounterDelta.Compute(default, current with { BaseValue = 0 }).HasValue);
    }

    [Fact]
    public void A_reset_stalled_or_absurd_counter_does_not_invent_a_percent()
    {
        Assert.False(CounterDelta.Compute(Timer(50, 100), Timer(40, 200)).HasValue);
        Assert.True(CounterDelta.Compute(Timer(50, 100), Timer(40, 200)).Supported);
        Assert.False(CounterDelta.Compute(Timer(0, 100), Timer(10, 100)).HasValue);
        Assert.False(CounterDelta.Compute(Timer(0, 0), Timer(1_000_000, 1)).HasValue);
        var mismatched = CounterDelta.Compute(Timer(0, 0), Idle(0, 10));
        Assert.True(mismatched.Supported);
        Assert.False(mismatched.HasValue);
        var unknown = new RawCounterPoint(0, 0, 0, 0, 0, 0, 42);
        Assert.False(CounterDelta.Compute(unknown, unknown with { RawValue = 1 }).Supported);
    }

    [Fact]
    public void Engine_deltas_sum_per_process_from_consecutive_snapshots()
    {
        var previous = Timer(0, 1_000);
        var first = Timer(2_500_000, 1_000 + 10_000_000);
        var second = Timer(4_000_000, 1_000 + 10_000_000);
        var usage = GpuEngineUsage.Aggregate(
            [
                new EngineSample("pid_1_luid_0x00000000_0x0000C581_phys_0_eng_0_engtype_3D", CounterDelta.Compute(previous, first).Value),
                new EngineSample("pid_2_luid_0x00000000_0x0000C581_phys_0_eng_0_engtype_3D", CounterDelta.Compute(previous, second).Value),
            ],
            [new AdapterDescriptor(0xC581, 0, "Integrated graphics", GpuClass.Integrated)]);
        Assert.Equal(65, usage[0].UsagePercent);
    }

    [Fact]
    public void Many_engines_take_one_delta_each_and_drop_departed_instances()
    {
        var prior = Timer(0, 1_000);
        var previous = new Dictionary<string, RawCounterPoint>(StringComparer.OrdinalIgnoreCase);
        var current = new List<RawEngineReading>();
        for (var i = 0; i < 200; i++)
        {
            var name = "eng-" + i.ToString(CultureInfo.InvariantCulture);
            previous[name] = prior;
            current.Add(new RawEngineReading(name, Timer(2_500, 1_000 + 10_000)));
        }

        previous["gone"] = prior;
        var samples = new List<EngineSample>();
        var next = EngineBaselines.Advance(previous, current, samples);
        Assert.Equal(200, samples.Count);
        Assert.Equal(200, next.Count);
        Assert.DoesNotContain("gone", next.Keys);
        Assert.All(samples, sample => Assert.Equal(25, sample.UtilizationPercent, 3));
    }

    [Fact]
    public void New_instances_wait_for_a_second_snapshot_and_names_ignore_case()
    {
        var previous = new Dictionary<string, RawCounterPoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eng"] = Timer(0, 0),
        };
        var samples = new List<EngineSample>();
        var next = EngineBaselines.Advance(previous, [new RawEngineReading("eng", Timer(10_000_000, 10_000_000))], samples);
        Assert.Equal(100, Assert.Single(samples).UtilizationPercent, 3);

        samples.Clear();
        EngineBaselines.Advance(next, [new RawEngineReading("other", Timer(10_000_000, 20_000_000))], samples);
        Assert.Empty(samples);
    }

    [Fact]
    public void Duplicate_instances_count_once_and_unknown_types_can_fall_back()
    {
        var previous = new Dictionary<string, RawCounterPoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Timer(0, 0),
            ["b"] = new RawCounterPoint(0, 0, 0, 0, 0, 0, 42),
        };
        var samples = new List<EngineSample>();
        EngineBaselines.Advance(
            previous,
            [
                new RawEngineReading("a", Timer(5_000_000, 10_000_000)),
                new RawEngineReading("a", Timer(9_000_000, 10_000_000)),
                new RawEngineReading("b", new RawCounterPoint(1, 0, 0, 0, 0, 0, 42)),
            ],
            samples,
            (_, _, _) => 99);
        Assert.Equal(2, samples.Count);
        Assert.Equal(50, samples[0].UtilizationPercent, 3);
        Assert.Equal(99, samples[1].UtilizationPercent, 3);

        samples.Clear();
        EngineBaselines.Advance(
            previous,
            [new RawEngineReading("b", new RawCounterPoint(1, 0, 0, 0, 0, 0, 42))],
            samples,
            (_, _, _) => -5);
        Assert.Empty(samples);
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    [InlineData(3000, 3000)]
    [InlineData(9000, 3000)]
    public void Refresh_interval_has_the_same_floor_as_settings(int requested, int expected)
    {
        Assert.Equal(expected, MonitorOptions.ClampRefresh(requested));
        var schedule = UsageSchedule.Start(requested, 250);
        Assert.Equal(expected, schedule.RefreshMs);
        Assert.True(schedule.IncludeGpu(250));
        Assert.False(schedule.IncludeGpu(249));
    }

    [Theory]
    [InlineData(0, 1000, false, 1000)]
    [InlineData(20, 1000, false, 1000)]
    [InlineData(21, 1000, false, 1050)]
    [InlineData(40, 1000, false, 2000)]
    [InlineData(400, 1000, false, 20000)]
    [InlineData(1000, 1000, false, 30000)]
    [InlineData(5, 1000, true, 2000)]
    [InlineData(400, 1000, true, 20000)]
    [InlineData(5, 500, true, 1000)]
    public void Gpu_gap_tracks_sample_cost_and_failures(long duration, int refresh, bool failed, long expectedOffset)
    {
        var next = UsageSchedule.Start(refresh, 0).AfterSample(0, duration, includedGpu: true, failed);
        Assert.Equal(expectedOffset, next.NextGpuMs);
        Assert.Equal(MonitorOptions.ClampRefresh(refresh), next.RefreshMs);
        Assert.False(next.IncludeGpu(expectedOffset - 1));
        Assert.True(next.IncludeGpu(expectedOffset));
    }

    [Fact]
    public void A_fast_sample_clears_failure_backoff_and_cpu_only_wakes_wait()
    {
        var failed = UsageSchedule.Start(1000, 0).AfterSample(0, 5, includedGpu: true, failed: true);
        Assert.Equal(1, failed.FailureStreak);
        Assert.False(failed.IncludeGpu(failed.NextGpuMs - 1));

        var recovered = failed.AfterSample(failed.NextGpuMs, 5, includedGpu: true, failed: false);
        Assert.Equal(0, recovered.FailureStreak);
        Assert.Equal(failed.NextGpuMs + 1000, recovered.NextGpuMs);

        var held = recovered.AfterSample(recovered.NextGpuMs - 100, 500, includedGpu: false, failed: true);
        Assert.Equal(recovered, held);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(3, 8000)]
    [InlineData(4, 15000)]
    [InlineData(8, 15000)]
    public void Pipe_failures_wait_longer_then_stop_growing(int failures, int expected) =>
        Assert.Equal(expected, FailureBackoff.DelayMs(failures, 2_000, 15_000));

    [Fact]
    public void Backoff_respects_a_caller_maximum()
    {
        Assert.Equal(1000, FailureBackoff.DelayMs(4, 5_000, 1_000));
        Assert.Equal(0, FailureBackoff.DelayMs(0, 2_000, 15_000));
    }

    private static RawCounterPoint Timer(long raw, long timestamp) =>
        new(raw, 0, 0, timestamp, 0, 0, CounterDelta.Perf100NsTimer);

    private static RawCounterPoint Idle(long raw, long timestamp) =>
        new(raw, 0, 0, timestamp, 0, 0, CounterDelta.Perf100NsTimerInverse);
}
