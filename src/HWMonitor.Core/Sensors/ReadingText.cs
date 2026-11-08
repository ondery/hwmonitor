// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace HWMonitor.Core.Sensors;

public static class ReadingText
{
    public const string SensorTitleSample = "100% · 100\u00B0C";

    public static string Format(
        string format,
        string label,
        double? usagePercent,
        double? temperatureCelsius,
        string unavailable,
        IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var usage = usagePercent is null ? unavailable : string.Format(provider, "{0:0}%", usagePercent.Value);
        var temperature = temperatureCelsius is null ? unavailable : string.Format(provider, "{0:0}\u00B0C", temperatureCelsius.Value);
        return string.Format(provider, format, label, usage, temperature);
    }

    public static string Value(
        double? usagePercent,
        double? temperatureCelsius,
        string unavailable,
        bool omitMissingTemperature,
        IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var usage = usagePercent is null ? unavailable : string.Format(provider, "{0:0}%", usagePercent.Value);
        if (temperatureCelsius is null && omitMissingTemperature)
        {
            return usage;
        }

        var temperature = temperatureCelsius is null ? unavailable : string.Format(provider, "{0:0}\u00B0C", temperatureCelsius.Value);
        return string.Format(provider, "{0} \u00B7 {1}", usage, temperature);
    }

    public static string ListLine(
        string label,
        double? usagePercent,
        double? temperatureCelsius,
        string unavailable,
        bool omitMissingTemperature,
        IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        return string.Format(
            provider,
            "{0} {1}",
            label,
            Value(usagePercent, temperatureCelsius, unavailable, omitMissingTemperature, provider));
    }
}

public static class SensorIds
{
    public static string ToCommandId(string readingId)
    {
        var builder = new System.Text.StringBuilder(readingId.Length);
        var pendingDash = false;
        foreach (var ch in readingId.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(ch);
            }
            else
            {
                pendingDash = true;
            }
        }

        return "com.ondery.hwmonitor.sensor." + builder;
    }
}

public static class DockProperties
{
    // Some Command Palette builds crash while measuring these keys.
    // The names stay so tests can prove the extension does not send them.
    public const string TabularDigits = "Microsoft.CommandPalette.Dock.TabularDigits";
    public const string TitleWidth = "Microsoft.CommandPalette.Dock.TitleWidth";
    public const string SubtitleWidth = "Microsoft.CommandPalette.Dock.SubtitleWidth";
    public const string SamplePrefix = "text:";

    public static string Sample(string text) => SamplePrefix + text;
}

public static class TemperatureGaps
{
    public static bool IsSharedDieGap(SensorReading reading) =>
        reading.IsPrimary
        && reading.TemperatureCelsius is null
        && (reading.Kind == DeviceKind.Cpu || reading.GpuClass == GpuClass.Integrated);
}
