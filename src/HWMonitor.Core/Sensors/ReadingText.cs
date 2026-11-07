// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace HWMonitor.Core.Sensors;

public static class ReadingText
{
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
