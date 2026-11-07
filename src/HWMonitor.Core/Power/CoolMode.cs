// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Power;

public static class PowerGuids
{
    public const string ProcessorSubgroup = "54533251-82be-4824-96c1-47b60b740d00";

    public const string MaxProcessorState = "bc5038f7-23e0-4960-96da-33abaf5935ec";

    public const string BoostMode = "be337238-0d82-4146-a960-4f3749d470c7";

    public const int BoostDisabled = 0;

    public const int BoostEfficient = 3;
}

public sealed record CoolSnapshot(
    string SchemeId,
    int AcThrottleMax,
    int DcThrottleMax,
    int? AcBoost,
    int? DcBoost);

public sealed record CoolState(
    bool Applied,
    string? SchemeId,
    int? AcThrottleMax,
    int? DcThrottleMax,
    int? AcBoost,
    int? DcBoost,
    bool BoostCaptured,
    bool ActivatedByAuto)
{
    public static CoolState Idle { get; } = new(false, null, null, null, null, null, false, false);
}

public sealed record CoolWrite(
    string SchemeId,
    int AcThrottleMax,
    int DcThrottleMax,
    int? AcBoost,
    int? DcBoost);

public static class CoolModePlanner
{
    public static (CoolState State, CoolWrite? Write) Enable(
        CoolState current,
        CoolSnapshot live,
        int maximumProcessorState,
        bool limitBoost,
        int boostValue,
        bool activatedByAuto)
    {
        var limit = Math.Clamp(maximumProcessorState, 1, 100);
        var saved = current.Applied && current.SchemeId is not null && current.AcThrottleMax is not null && current.DcThrottleMax is not null
            ? current with { ActivatedByAuto = activatedByAuto }
            : new CoolState(
                true,
                live.SchemeId,
                live.AcThrottleMax,
                live.DcThrottleMax,
                limitBoost ? live.AcBoost : null,
                limitBoost ? live.DcBoost : null,
                limitBoost && live.AcBoost is not null && live.DcBoost is not null,
                activatedByAuto);

        var write = new CoolWrite(
            saved.SchemeId!,
            limit,
            limit,
            saved.BoostCaptured ? boostValue : null,
            saved.BoostCaptured ? boostValue : null);
        return (saved with { Applied = true }, write);
    }

    public static (CoolState State, CoolWrite? Write) Disable(CoolState current)
    {
        if (!current.Applied || current.SchemeId is null || current.AcThrottleMax is null || current.DcThrottleMax is null)
        {
            return (CoolState.Idle, null);
        }

        var write = new CoolWrite(
            current.SchemeId,
            current.AcThrottleMax.Value,
            current.DcThrottleMax.Value,
            current.BoostCaptured ? current.AcBoost : null,
            current.BoostCaptured ? current.DcBoost : null);
        return (CoolState.Idle, write);
    }
}
