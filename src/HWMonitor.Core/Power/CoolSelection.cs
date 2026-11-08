// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Power;

public enum CoolSelection
{
    Auto,
    On,
    Off,
}

public static class CoolSelectionCycle
{
    public static CoolSelection Next(CoolSelection current) => current switch
    {
        CoolSelection.Auto => CoolSelection.On,
        CoolSelection.On => CoolSelection.Off,
        _ => CoolSelection.Auto,
    };

    public static string Token(CoolSelection selection) => selection switch
    {
        CoolSelection.On => "on",
        CoolSelection.Off => "off",
        _ => "auto",
    };

    public static CoolSelection Parse(string? text, bool? legacyAutoEnabled)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "on":
                return CoolSelection.On;
            case "off":
                return CoolSelection.Off;
            case "auto":
                return CoolSelection.Auto;
        }

        return legacyAutoEnabled == true ? CoolSelection.Auto : CoolSelection.Off;
    }
}
