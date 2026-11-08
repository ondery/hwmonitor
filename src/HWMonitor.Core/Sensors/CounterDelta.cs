// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Sensors;

public readonly record struct RawCounterPoint(
    long RawValue,
    long BaseValue,
    long TimeStamp,
    long TimeStamp100Ns,
    long CounterFrequency,
    long SystemFrequency,
    int CounterType);

public readonly record struct CounterDeltaResult(bool Supported, bool HasValue, double Value)
{
    public static CounterDeltaResult Unsupported { get; } = new(false, false, 0);

    public static CounterDeltaResult Skip { get; } = new(true, false, 0);

    public static CounterDeltaResult From(double value) => new(true, true, value);
}

public readonly record struct RawEngineReading(string InstanceName, RawCounterPoint Point);

public static class CounterDelta
{
    public const int Perf100NsTimer = 542180608;
    public const int Perf100NsTimerInverse = 558957824;
    public const int PerfCounterTimer = 541132032;
    public const int PerfCounterTimerInverse = 557909248;
    public const int PerfPrecision100NsTimer = 542573824;
    public const int PerfPrecisionSystemTimer = 541525248;
    public const int PerfRawFraction = 537003008;
    public const double MaxPercent = 10_000;

    public static CounterDeltaResult Compute(in RawCounterPoint previous, in RawCounterPoint current)
    {
        if (current.CounterType != PerfRawFraction && previous.CounterType != current.CounterType)
        {
            return CounterDeltaResult.Skip;
        }

        return current.CounterType switch
        {
            PerfRawFraction => Fraction(current.RawValue, current.BaseValue),
            Perf100NsTimer => Percent(current.RawValue - previous.RawValue, current.TimeStamp100Ns - previous.TimeStamp100Ns, inverse: false),
            Perf100NsTimerInverse => Percent(current.RawValue - previous.RawValue, current.TimeStamp100Ns - previous.TimeStamp100Ns, inverse: true),
            PerfCounterTimer => Percent(current.RawValue - previous.RawValue, current.TimeStamp - previous.TimeStamp, inverse: false),
            PerfCounterTimerInverse => Percent(current.RawValue - previous.RawValue, current.TimeStamp - previous.TimeStamp, inverse: true),
            PerfPrecision100NsTimer or PerfPrecisionSystemTimer => Percent(current.RawValue - previous.RawValue, current.BaseValue - previous.BaseValue, inverse: false),
            _ => CounterDeltaResult.Unsupported,
        };
    }

    private static CounterDeltaResult Fraction(long numerator, long denominator)
    {
        if (denominator <= 0 || numerator < 0)
        {
            return CounterDeltaResult.Skip;
        }

        return Finish(numerator / (double)denominator);
    }

    private static CounterDeltaResult Percent(long numerator, long denominator, bool inverse)
    {
        if (denominator <= 0)
        {
            return CounterDeltaResult.Skip;
        }

        var ratio = numerator / (double)denominator;
        if (inverse)
        {
            ratio = 1d - ratio;
        }

        return Finish(ratio);
    }

    private static CounterDeltaResult Finish(double ratio)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0)
        {
            return CounterDeltaResult.Skip;
        }

        var percent = ratio * 100d;
        return percent > MaxPercent ? CounterDeltaResult.Skip : CounterDeltaResult.From(percent);
    }
}

public static class EngineBaselines
{
    public static Dictionary<string, RawCounterPoint> Advance(
        IReadOnlyDictionary<string, RawCounterPoint> previous,
        IReadOnlyList<RawEngineReading> current,
        ICollection<EngineSample> samples,
        Func<string, RawCounterPoint, RawCounterPoint, double?>? unsupported = null)
    {
        var next = new Dictionary<string, RawCounterPoint>(current.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var reading in current)
        {
            if (string.IsNullOrEmpty(reading.InstanceName) || !next.TryAdd(reading.InstanceName, reading.Point))
            {
                continue;
            }

            if (!previous.TryGetValue(reading.InstanceName, out var prior))
            {
                continue;
            }

            var delta = CounterDelta.Compute(prior, reading.Point);
            if (delta.HasValue)
            {
                samples.Add(new EngineSample(reading.InstanceName, delta.Value));
                continue;
            }

            if (delta.Supported || unsupported is null)
            {
                continue;
            }

            var fallback = unsupported(reading.InstanceName, prior, reading.Point);
            if (fallback is double value && IsUsable(value))
            {
                samples.Add(new EngineSample(reading.InstanceName, value));
            }
        }

        return next;
    }

    private static bool IsUsable(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= CounterDelta.MaxPercent;
}
