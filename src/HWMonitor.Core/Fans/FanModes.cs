// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace HWMonitor.Core.Fans;

public sealed record FanModeDefinition(string Name, int DutyPercent, bool Enabled);

public sealed record FanModeSet(IReadOnlyList<FanModeDefinition> Modes)
{
    public const int MinimumEnabled = 2;

    public const string TooFewEnabledMessage = "At least two fan modes must stay enabled.";

    public static FanModeSet Default { get; } = new(
    [
        new FanModeDefinition("Silent", 20, true),
        new FanModeDefinition("Quiet", 40, true),
        new FanModeDefinition("Balanced", 60, true),
        new FanModeDefinition("Performance", 80, true),
        new FanModeDefinition("Max", 100, true),
    ]);

    public IReadOnlyList<int> EnabledIndexes()
    {
        var indexes = new List<int>();
        for (var i = 0; i < Modes.Count; i++)
        {
            if (Modes[i].Enabled)
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    public static bool TryCreate(IReadOnlyList<(string? Name, string? Duty, bool Enabled)> inputs, out FanModeSet set, out string? error)
    {
        var modes = new List<FanModeDefinition>();
        var count = Math.Min(5, inputs.Count);
        for (var i = 0; i < count; i++)
        {
            var (name, dutyText, enabled) = inputs[i];
            var trimmed = string.IsNullOrWhiteSpace(name) ? $"Mode {i + 1}" : name.Trim();
            if (trimmed.Length > 24)
            {
                trimmed = trimmed[..24];
            }

            var duty = Default.Modes[Math.Min(i, Default.Modes.Count - 1)].DutyPercent;
            if (int.TryParse(dutyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                duty = Math.Clamp(parsed, 0, 100);
            }

            modes.Add(new FanModeDefinition(trimmed, duty, enabled));
        }

        while (modes.Count < 5)
        {
            var template = Default.Modes[modes.Count];
            modes.Add(template);
        }

        if (modes.Count(mode => mode.Enabled) < MinimumEnabled)
        {
            set = Default;
            error = TooFewEnabledMessage;
            return false;
        }

        set = new FanModeSet(modes);
        error = null;
        return true;
    }

    public static int Advance(int currentIndex, FanModeSet set)
    {
        var enabled = set.EnabledIndexes();
        if (enabled.Count == 0)
        {
            return -1;
        }

        if (currentIndex < 0)
        {
            return enabled[0];
        }

        var position = enabled.ToList().IndexOf(currentIndex);
        if (position < 0 || position >= enabled.Count - 1)
        {
            return -1;
        }

        return enabled[position + 1];
    }
}

public sealed record FanControlState(bool SoftwareControl, int ModeIndex)
{
    public static FanControlState Automatic { get; } = new(false, -1);
}
