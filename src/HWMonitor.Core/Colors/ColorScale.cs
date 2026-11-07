// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Colors;

public enum TemperatureBand
{
    Unknown,
    Blue,
    Yellow,
    Orange,
    Red,
}

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Blue { get; } = new(0x3B, 0x82, 0xF6);

    public static RgbColor Yellow { get; } = new(0xEA, 0xB3, 0x08);

    public static RgbColor Orange { get; } = new(0xF9, 0x73, 0x16);

    public static RgbColor Red { get; } = new(0xEF, 0x44, 0x44);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public RgbColor ContrastText()
    {
        var luminance = (0.299 * R) + (0.587 * G) + (0.114 * B);
        return luminance >= 150 ? new RgbColor(17, 24, 39) : new RgbColor(255, 255, 255);
    }

    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length == 3)
        {
            value = string.Concat(value.Select(ch => $"{ch}{ch}"));
        }

        if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var packed))
        {
            return false;
        }

        color = new RgbColor((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
        return true;
    }
}

public sealed record ColorScale(
    int BlueBelow,
    int YellowFrom,
    int OrangeFrom,
    int RedFrom,
    RgbColor Blue,
    RgbColor Yellow,
    RgbColor Orange,
    RgbColor Red)
{
    public static ColorScale Default { get; } = new(
        60,
        60,
        75,
        90,
        RgbColor.Blue,
        RgbColor.Yellow,
        RgbColor.Orange,
        RgbColor.Red);

    public static ColorScale Normalize(
        int blueBelow,
        int yellowFrom,
        int orangeFrom,
        int redFrom,
        RgbColor? blue,
        RgbColor? yellow,
        RgbColor? orange,
        RgbColor? red)
    {
        var blueEdge = Math.Clamp(blueBelow, 0, 120);
        var yellowEdge = Math.Clamp(Math.Max(yellowFrom, blueEdge), 0, 121);
        var orangeEdge = Math.Clamp(Math.Max(orangeFrom, yellowEdge + 1), 1, 124);
        var redEdge = Math.Clamp(Math.Max(redFrom, orangeEdge + 1), 2, 125);

        return new ColorScale(
            yellowEdge,
            yellowEdge,
            orangeEdge,
            redEdge,
            blue ?? RgbColor.Blue,
            yellow ?? RgbColor.Yellow,
            orange ?? RgbColor.Orange,
            red ?? RgbColor.Red);
    }

    public TemperatureBand BandFor(double? celsius)
    {
        if (celsius is null || double.IsNaN(celsius.Value))
        {
            return TemperatureBand.Unknown;
        }

        if (celsius.Value >= RedFrom)
        {
            return TemperatureBand.Red;
        }

        if (celsius.Value >= OrangeFrom)
        {
            return TemperatureBand.Orange;
        }

        if (celsius.Value >= YellowFrom)
        {
            return TemperatureBand.Yellow;
        }

        return TemperatureBand.Blue;
    }

    public RgbColor ColorFor(TemperatureBand band) => band switch
    {
        TemperatureBand.Yellow => Yellow,
        TemperatureBand.Orange => Orange,
        TemperatureBand.Red => Red,
        _ => Blue,
    };
}
