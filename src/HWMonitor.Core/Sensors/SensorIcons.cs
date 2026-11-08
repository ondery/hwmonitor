// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Colors;

namespace HWMonitor.Core.Sensors;

public static class SensorIcons
{
    // IconInfo has no foreground color. Command Palette tints an icon when the
    // icon string uses this host protocol. Font glyphs stay neutral.
    public const string ThemedPrefix = "|ThemedSvg|";

    private const string CpuSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"{{AccentColor}}\" fill-rule=\"evenodd\" d=\"M9 1h2v3H9zm4 0h2v3h-2zM9 20h2v3H9zm4 0h2v3h-2zM1 9h3v2H1zm0 4h3v2H1zm19-4h3v2h-3zm0 4h3v2h-3zM6 5h12v14H6zm3 3h6v8H9z\"/></svg>";

    private const string GpuSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"{{AccentColor}}\" fill-rule=\"evenodd\" d=\"M2 6h20v12H2zm3 3h8v6H5zm11 1h3v2h-3zm0 3h3v2h-3z\"/></svg>";

    private const string CoolOnSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{{AccentColor}}\" stroke-width=\"2.4\" stroke-linecap=\"round\"><path d=\"M12 3v18M4.8 7.5l14.4 9M19.2 7.5l-14.4 9\"/></svg>";

    private const string CoolOffSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{{AccentColor}}\" stroke-width=\"2.4\" stroke-linecap=\"round\"><circle cx=\"12\" cy=\"12\" r=\"7.5\"/><path d=\"M8 16.5L16 7.5\"/></svg>";

    public static string? Temperature(DeviceKind kind, RgbColor? color, bool enabled)
    {
        if (!enabled || color is null)
        {
            return null;
        }

        return Tint(color.Value, kind == DeviceKind.Cpu ? CpuSvg : GpuSvg);
    }

    public static string Cool(bool on, RgbColor onColor) =>
        Tint(on ? onColor : ColorPresets.OffGray, on ? CoolOnSvg : CoolOffSvg);

    private static string Tint(RgbColor color, string svg) => ThemedPrefix + color.ToHex() + "|" + svg;
}
