// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Colors;
using HWMonitor.Core.Text;

namespace HWMonitor.Core.Sensors;

public readonly record struct StatusBadge(string Text, RgbColor Background, RgbColor Foreground)
{
    public static StatusBadge Create(string text, RgbColor background) =>
        new(text, background, background.ContrastText());
}

public static class StatusBadges
{
    public static StatusBadge? ForTemperature(TemperatureBand band, RgbColor color)
    {
        var text = band switch
        {
            TemperatureBand.Blue => UiText.TagBlue,
            TemperatureBand.Yellow => UiText.TagYellow,
            TemperatureBand.Orange => UiText.TagOrange,
            TemperatureBand.Red => UiText.TagRed,
            _ => null,
        };
        return text is null ? null : StatusBadge.Create(text, color);
    }

    public static StatusBadge ForCool(bool on, RgbColor onColor) =>
        StatusBadge.Create(on ? UiText.CoolOn : UiText.CoolOff, on ? onColor : ColorPresets.OffGray);
}
