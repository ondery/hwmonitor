// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Sensors;

public readonly record struct GpuClassSignals(
    string HardwareType,
    string RuntimeTypeName,
    string HardwareId,
    ulong? DedicatedVideoBytes);

public static class GpuClassResolver
{
    public const ulong IntegratedMemoryCeilingBytes = 1024L * 1024 * 1024;

    public static GpuClass Resolve(GpuClassSignals signals)
    {
        var typeName = signals.RuntimeTypeName ?? string.Empty;
        var hardwareId = signals.HardwareId ?? string.Empty;
        if (Contains(typeName, "Integrated") || Contains(hardwareId, "integrated"))
        {
            return GpuClass.Integrated;
        }

        if (Contains(typeName, "Discrete"))
        {
            return GpuClass.Discrete;
        }

        var hardwareType = signals.HardwareType ?? string.Empty;
        var mayBeIntegrated = hardwareType.Equals("GpuIntel", StringComparison.OrdinalIgnoreCase)
            || hardwareType.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase);
        if (mayBeIntegrated
            && signals.DedicatedVideoBytes is ulong bytes
            && bytes < IntegratedMemoryCeilingBytes)
        {
            return GpuClass.Integrated;
        }

        if (hardwareType.Equals("GpuNvidia", StringComparison.OrdinalIgnoreCase)
            || hardwareType.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase)
            || hardwareType.Equals("GpuIntel", StringComparison.OrdinalIgnoreCase))
        {
            return GpuClass.Discrete;
        }

        return GpuClass.None;
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
