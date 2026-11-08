// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;

namespace HWMonitor.Core.Options;

public sealed record CoolOptions(int MaximumProcessorState, bool LimitBoost, int BoostValue)
{
    public static CoolOptions Default { get; } = new(99, true, PowerGuids.BoostDisabled);

    public static CoolOptions Normalize(int maximumProcessorState, bool limitBoost, int boostValue) =>
        new(Math.Clamp(maximumProcessorState, 1, 100), limitBoost, boostValue == PowerGuids.BoostEfficient ? PowerGuids.BoostEfficient : PowerGuids.BoostDisabled);
}

public sealed record MonitorOptions(
    int RefreshIntervalMs,
    bool Compact,
    ColorScale Colors,
    CoolOptions Cool,
    AutoCoolOptions AutoCool,
    PresentationMap Devices)
{
    public const int DefaultRefreshMs = 1000;

    public static MonitorOptions CreateDefault() => new(
        DefaultRefreshMs,
        false,
        ColorScale.Default,
        CoolOptions.Default,
        AutoCoolOptions.Default,
        PresentationMap.Empty);

    public static int ParseInt(string? text, int fallback)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public static int ClampRefresh(int milliseconds) => Math.Clamp(milliseconds, 500, 3000);
}
