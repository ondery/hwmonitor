// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace HWMonitor.Core.Power;

public static partial class PowerCfgText
{
    public static bool TryParseActiveScheme(string text, out string schemeId, out string? name)
    {
        schemeId = string.Empty;
        name = null;
        var match = SchemePattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        schemeId = match.Groups["id"].Value.ToLowerInvariant();
        name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : null;
        return true;
    }

    public static bool TryParseIndexes(string text, out int ac, out int dc)
    {
        ac = 0;
        dc = 0;
        var acMatch = IndexPattern("AC").Match(text);
        var dcMatch = IndexPattern("DC").Match(text);
        if (!acMatch.Success || !dcMatch.Success)
        {
            return false;
        }

        return TryParseIndex(acMatch.Groups["value"].Value, out ac)
            && TryParseIndex(dcMatch.Groups["value"].Value, out dc);
    }

    private static bool TryParseIndex(string text, out int value)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static Regex IndexPattern(string power) => new(
        $@"Current {power} Power Setting Index:\s*(?<value>0x[0-9A-Fa-f]+|\d+)",
        RegexOptions.CultureInvariant);

    [GeneratedRegex(
        @"Power Scheme GUID:\s*(?<id>[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12})(?:\s+\((?<name>[^)]+)\))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();
}
