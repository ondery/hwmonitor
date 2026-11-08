// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Colors;

public sealed record NamedColor(string Id, RgbColor Color);

public sealed record ColorChoice(string PresetId, string CustomHex, RgbColor Color);

public static class ColorPresets
{
    public const string CustomId = "custom";

    // A stored hex within this distance of a named color is treated as that color.
    // Anything farther stays a custom color so an intentional hex is not replaced.
    public const int NearestDistance = 18;

    public static IReadOnlyList<NamedColor> Palette { get; } =
    [
        new("blue", RgbColor.Blue),
        new("sky", new RgbColor(0x0E, 0xA5, 0xE9)),
        new("cyan", new RgbColor(0x06, 0xB6, 0xD4)),
        new("teal", new RgbColor(0x14, 0xB8, 0xA6)),
        new("green", new RgbColor(0x22, 0xC5, 0x5E)),
        new("lime", new RgbColor(0x84, 0xCC, 0x16)),
        new("yellow", RgbColor.Yellow),
        new("amber", new RgbColor(0xF5, 0x9E, 0x0B)),
        new("orange", RgbColor.Orange),
        new("red", RgbColor.Red),
        new("rose", new RgbColor(0xF4, 0x3F, 0x5E)),
        new("pink", new RgbColor(0xEC, 0x48, 0x99)),
        new("slate", new RgbColor(0x64, 0x74, 0x8B)),
    ];

    public static RgbColor OffGray { get; } = new(0x64, 0x74, 0x8B);

    public static NamedColor? FindId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var key = id.Trim();
        foreach (var color in Palette)
        {
            if (string.Equals(color.Id, key, StringComparison.OrdinalIgnoreCase))
            {
                return color;
            }
        }

        return null;
    }

    public static ColorChoice FromStored(string? text, RgbColor fallback)
    {
        var parsed = RgbColor.TryParse(text, out var color) ? color : fallback;
        var exact = FindExact(parsed);
        if (exact is not null)
        {
            return Named(exact);
        }

        var nearest = Palette[0];
        var nearestDistance = Distance(nearest.Color, parsed);
        foreach (var candidate in Palette)
        {
            var distance = Distance(candidate.Color, parsed);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearestDistance <= NearestDistance)
        {
            return Named(nearest);
        }

        return new ColorChoice(CustomId, parsed.ToHex(), parsed);
    }

    public static ColorChoice FromPreset(string? presetId, RgbColor fallback)
    {
        var match = FindId(presetId);
        return match is null ? FromStored(fallback.ToHex(), fallback) : Named(match);
    }

    public static int Distance(RgbColor left, RgbColor right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return (int)Math.Round(Math.Sqrt((dr * dr) + (dg * dg) + (db * db)));
    }

    private static NamedColor? FindExact(RgbColor color)
    {
        foreach (var candidate in Palette)
        {
            if (candidate.Color == color)
            {
                return candidate;
            }
        }

        return null;
    }

    private static ColorChoice Named(NamedColor color) =>
        new(color.Id, color.Color.ToHex(), color.Color);
}
