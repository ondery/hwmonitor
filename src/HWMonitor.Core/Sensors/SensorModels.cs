// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Sensors;

public enum DeviceKind
{
    Cpu,
    Gpu,
}

public enum GpuClass
{
    None,
    Integrated,
    Discrete,
}

public enum MetricKind
{
    Load,
    Temperature,
}

public enum SensorRole
{
    Package,
    HottestCore,
    Core,
    Adapter,
    CpuGraphics,
    SharedPackage,
}

public sealed record RawSensor(
    string HardwareId,
    string HardwareName,
    DeviceKind Kind,
    GpuClass GpuClass,
    string SensorName,
    MetricKind Metric,
    double Value);

public sealed record AdapterUsage(
    uint LuidLow,
    uint LuidHigh,
    string Name,
    GpuClass GpuClass,
    double? UsagePercent);

public sealed record AdapterDescriptor(
    uint LuidLow,
    uint LuidHigh,
    string Name,
    GpuClass GpuClass,
    ulong DedicatedVideoBytes = 0,
    uint VendorId = 0);

public sealed record SensorReading(
    string Id,
    string HardwareId,
    DeviceKind Kind,
    GpuClass GpuClass,
    bool IsPrimary,
    int? CoreNumber,
    string HardwareName,
    string ShortLabel,
    SensorRole Role,
    double? UsagePercent,
    double? TemperatureCelsius,
    string UsageSource,
    string TemperatureSource);

public static class SensorSources
{
    public const string CpuTotal = "cpu-total";
    public const string CoreAverage = "core-average";
    public const string ProcessorUtility = "processor-utility";
    public const string GpuCore = "gpu-core";
    public const string D3D = "d3d-3d";
    public const string GpuEngine = "gpu-engine";
    public const string Package = "package";
    public const string CoreMax = "core-max";
    public const string HottestCore = "hottest-core";
    public const string HotSpot = "hot-spot";
    public const string CpuGraphics = "cpu-graphics";
    public const string SharedPackage = "cpu-package";
    public const string Unavailable = "unavailable";
}

public static class DisplayVendors
{
    public const uint Nvidia = 0x10DE;
    public const uint Amd = 0x1002;
    public const uint Intel = 0x8086;
}
