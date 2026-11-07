// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Power;

public enum AutoCoolAction
{
    None,
    Activate,
    Deactivate,
}

public sealed record AutoCoolOptions(bool Enabled, int ActivateCelsius, int ActivateSeconds, int DeactivateCelsius, int DeactivateSeconds)
{
    public static AutoCoolOptions Default { get; } = new(false, 85, 10, 70, 10);

    public static AutoCoolOptions Normalize(bool enabled, int activateCelsius, int activateSeconds, int deactivateCelsius, int deactivateSeconds)
    {
        var activate = Math.Clamp(activateCelsius, 1, 125);
        var deactivate = Math.Clamp(deactivateCelsius, 0, 124);
        if (deactivate >= activate)
        {
            deactivate = Math.Max(0, activate - 1);
        }

        return new AutoCoolOptions(
            enabled,
            activate,
            Math.Clamp(activateSeconds, 1, 600),
            deactivate,
            Math.Clamp(deactivateSeconds, 1, 600));
    }
}

public sealed class AutoCoolController
{
    private DateTimeOffset? _hotSince;
    private DateTimeOffset? _coolSince;

    public AutoCoolAction Evaluate(AutoCoolOptions options, bool coolModeOn, bool activatedByAuto, double? cpuCelsius, DateTimeOffset now)
    {
        if (!options.Enabled || cpuCelsius is null)
        {
            _hotSince = null;
            _coolSince = null;
            return AutoCoolAction.None;
        }

        if (!coolModeOn)
        {
            _coolSince = null;
            if (cpuCelsius.Value >= options.ActivateCelsius)
            {
                _hotSince ??= now;
                if (now - _hotSince.Value >= TimeSpan.FromSeconds(options.ActivateSeconds))
                {
                    _hotSince = null;
                    return AutoCoolAction.Activate;
                }
            }
            else
            {
                _hotSince = null;
            }

            return AutoCoolAction.None;
        }

        _hotSince = null;
        if (!activatedByAuto)
        {
            _coolSince = null;
            return AutoCoolAction.None;
        }

        if (cpuCelsius.Value <= options.DeactivateCelsius)
        {
            _coolSince ??= now;
            if (now - _coolSince.Value >= TimeSpan.FromSeconds(options.DeactivateSeconds))
            {
                _coolSince = null;
                return AutoCoolAction.Deactivate;
            }
        }
        else
        {
            _coolSince = null;
        }

        return AutoCoolAction.None;
    }
}
