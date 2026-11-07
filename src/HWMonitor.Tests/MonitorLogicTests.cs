// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using Xunit;
using HWMonitor.Core.Fans;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;

namespace HWMonitor.Tests;

public class MonitorLogicTests
{
    [Fact]
    public void Formats_the_compact_reading()
    {
        var text = ReadingText.Format("{0} {1} · {2}", "CPU", 27, 58, "n/a", CultureInfo.InvariantCulture);
        Assert.Equal("CPU 27% · 58°C", text);
        Assert.Equal("CPU 27% · n/a", ReadingText.Format("{0} {1} · {2}", "CPU", 27, null, "n/a", CultureInfo.InvariantCulture));
        Assert.Equal("GPU n/a · 51°C", ReadingText.Format("{0} {1} · {2}", "GPU", null, 51, "n/a", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void English_resources_include_the_fan_message()
    {
        Assert.Equal("HW Monitor", UiText.AppName);
        Assert.Equal("Fan control is not supported on this device", UiText.FanUnsupported);
        Assert.Equal(MsiEcPolicy.UnsupportedMessage, UiText.FanUnsupported);
    }

    [Fact]
    public void Color_bands_follow_the_default_thresholds()
    {
        var scale = ColorScale.Default;
        Assert.Equal(TemperatureBand.Blue, scale.BandFor(59));
        Assert.Equal(TemperatureBand.Yellow, scale.BandFor(60));
        Assert.Equal(TemperatureBand.Orange, scale.BandFor(75));
        Assert.Equal(TemperatureBand.Red, scale.BandFor(90));
        Assert.Equal(TemperatureBand.Unknown, scale.BandFor(null));
        Assert.Equal("#3B82F6", scale.Blue.ToHex());
        Assert.Equal("#EAB308", scale.Yellow.ToHex());
        Assert.Equal("#F97316", scale.Orange.ToHex());
        Assert.Equal("#EF4444", scale.Red.ToHex());
    }

    [Fact]
    public void Color_thresholds_stay_ordered()
    {
        var scale = ColorScale.Normalize(70, 60, 75, 90, null, null, null, null);
        Assert.Equal(70, scale.BlueBelow);
        Assert.Equal(70, scale.YellowFrom);
        Assert.Equal(75, scale.OrangeFrom);
        Assert.Equal(90, scale.RedFrom);
        Assert.True(RgbColor.TryParse("#abc", out var shortHex));
        Assert.Equal("#AABBCC", shortHex.ToHex());
    }

    [Fact]
    public void Refresh_interval_is_clamped()
    {
        Assert.Equal(500, MonitorOptions.ClampRefresh(100));
        Assert.Equal(3000, MonitorOptions.ClampRefresh(9000));
        Assert.Equal(1000, MonitorOptions.ClampRefresh(1000));
    }

    [Fact]
    public void Target_laptop_sensors_keep_package_cores_and_both_gpus()
    {
        var readings = SensorCatalog.Compose(LaptopSamples(), [], null);
        var cpu = readings.Single(item => item.IsPrimary && item.Kind == DeviceKind.Cpu);
        Assert.Equal("CPU", cpu.ShortLabel);
        Assert.Equal(27, cpu.UsagePercent);
        Assert.Equal(58, cpu.TemperatureCelsius);
        Assert.Equal(SensorSources.Package, cpu.TemperatureSource);
        Assert.Equal(2, readings.Count(item => item.Role == SensorRole.Core));

        var igpu = readings.Single(item => item.ShortLabel == "iGPU");
        Assert.Equal(4, igpu.UsagePercent);
        Assert.Equal(49, igpu.TemperatureCelsius);
        Assert.Equal(SensorSources.CpuGraphics, igpu.TemperatureSource);

        var gpu = readings.Single(item => item.ShortLabel == "GPU");
        Assert.Equal(12, gpu.UsagePercent);
        Assert.Equal(51, gpu.TemperatureCelsius);
        Assert.Equal(SensorSources.GpuCore, gpu.UsageSource);

        var compact = readings.Where(item => item.IsPrimary).Select(item => item.ShortLabel).ToArray();
        Assert.Equal(["CPU", "iGPU", "GPU"], compact);
        Assert.Equal("CPU 27% · 58°C", ReadingText.Format(UiText.ReadingFormat, cpu.ShortLabel, cpu.UsagePercent, cpu.TemperatureCelsius, UiText.Unavailable, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Missing_package_temperature_uses_the_hottest_core()
    {
        var samples = LaptopSamples().Where(item => item.SensorName != "CPU Package").ToArray();
        var cpu = SensorCatalog.Compose(samples, [], null).Single(item => item.IsPrimary && item.Kind == DeviceKind.Cpu);
        Assert.Equal(61, cpu.TemperatureCelsius);
        Assert.Equal(SensorRole.HottestCore, cpu.Role);
    }

    [Fact]
    public void Performance_counters_fill_missing_usage()
    {
        var samples = LaptopSamples().Where(item =>
            item.SensorName is not "CPU Total" and not "D3D 3D"
            && !(item.Metric == MetricKind.Load && item.SensorName.StartsWith("CPU Core", StringComparison.Ordinal))).ToList();
        samples.Add(Sample("/gpu-intel-integrated/0", "Intel UHD Graphics", DeviceKind.Gpu, GpuClass.Integrated, "GPU Core", MetricKind.Temperature, 49));
        var adapters = new[]
        {
            new AdapterUsage(0xC581, 0, "Intel(R) UHD Graphics", GpuClass.Integrated, 6),
        };
        var readings = SensorCatalog.Compose(samples, adapters, 33);
        var cpu = readings.Single(item => item.IsPrimary && item.Kind == DeviceKind.Cpu);
        Assert.Equal(33, cpu.UsagePercent);
        var igpu = readings.Single(item => item.ShortLabel == "iGPU");
        Assert.Equal(6, igpu.UsagePercent);
        Assert.Equal(SensorSources.GpuEngine, igpu.UsageSource);
    }

    [Fact]
    public void Gpu_engine_instances_sum_per_process_then_take_the_busiest_engine()
    {
        Assert.True(GpuEngineUsage.TryParse(
            "pid_42_luid_0x00000000_0x0000C581_phys_0_eng_0_engtype_3D",
            out var parsed));
        Assert.Equal(0xC581u, parsed.LuidLow);
        Assert.Equal("3D", parsed.EngineType);

        var usage = GpuEngineUsage.Aggregate(
            [
                new EngineSample("pid_1_luid_0x00000000_0x0000C581_phys_0_eng_0_engtype_3D", 40),
                new EngineSample("pid_2_luid_0x00000000_0x0000C581_phys_0_eng_0_engtype_3D", 25),
                new EngineSample("pid_1_luid_0x00000000_0x0000C581_phys_0_eng_1_engtype_Copy", 90),
            ],
            [new AdapterDescriptor(0xC581, 0, "Intel UHD", GpuClass.Integrated)]);
        Assert.Equal(65, usage[0].UsagePercent);
    }

    [Fact]
    public void Cool_mode_saves_the_original_scheme_once()
    {
        var live = new CoolSnapshot("381b4222-f694-41f0-9685-ff5bb260df2e", 100, 100, 2, 2);
        var (state, write) = CoolModePlanner.Enable(CoolState.Idle, live, 99, true, PowerGuids.BoostDisabled, false);
        Assert.True(state.Applied);
        Assert.Equal(100, state.AcThrottleMax);
        Assert.Equal(99, write!.AcThrottleMax);
        Assert.Equal(PowerGuids.BoostDisabled, write.AcBoost);

        var alreadyLimited = live with { AcThrottleMax = 99, DcThrottleMax = 99, AcBoost = 0, DcBoost = 0 };
        var (again, rewrite) = CoolModePlanner.Enable(state, alreadyLimited, 99, true, PowerGuids.BoostDisabled, true);
        Assert.Equal(100, again.AcThrottleMax);
        Assert.Equal(99, rewrite!.AcThrottleMax);

        var (restored, restoreWrite) = CoolModePlanner.Disable(again);
        Assert.False(restored.Applied);
        Assert.Equal(100, restoreWrite!.AcThrottleMax);
        Assert.Equal(2, restoreWrite.AcBoost);
    }

    [Fact]
    public void Powercfg_text_parses_scheme_and_indexes()
    {
        const string scheme = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)";
        Assert.True(PowerCfgText.TryParseActiveScheme(scheme, out var id, out var name));
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", id);
        Assert.Equal("Balanced", name);

        const string query = """
            Current AC Power Setting Index: 0x00000064
            Current DC Power Setting Index: 0x0000005A
            """;
        Assert.True(PowerCfgText.TryParseIndexes(query, out var ac, out var dc));
        Assert.Equal(100, ac);
        Assert.Equal(90, dc);
    }

    [Fact]
    public void Auto_cool_waits_out_the_hysteresis_window()
    {
        var options = AutoCoolOptions.Default with { Enabled = true };
        var controller = new AutoCoolController();
        var start = DateTimeOffset.Parse("2026-11-07T12:00:00Z", CultureInfo.InvariantCulture);
        Assert.Equal(AutoCoolAction.None, controller.Evaluate(options, false, false, 86, start));
        Assert.Equal(AutoCoolAction.None, controller.Evaluate(options, false, false, 86, start.AddSeconds(9)));
        Assert.Equal(AutoCoolAction.Activate, controller.Evaluate(options, false, false, 86, start.AddSeconds(10)));

        Assert.Equal(AutoCoolAction.None, controller.Evaluate(options, true, true, 69, start.AddSeconds(11)));
        Assert.Equal(AutoCoolAction.Deactivate, controller.Evaluate(options, true, true, 69, start.AddSeconds(21)));
        Assert.Equal(AutoCoolAction.None, controller.Evaluate(options, true, false, 40, start.AddSeconds(40)));
    }

    [Fact]
    public void Fan_cycle_skips_disabled_modes_and_returns_to_auto()
    {
        Assert.True(FanModeSet.TryCreate(
            [
                ("Silent", "20", true),
                ("Quiet", "40", false),
                ("Balanced", "60", true),
                ("Performance", "80", true),
                ("Max", "100", false),
            ],
            out var set,
            out var error));
        Assert.Null(error);
        Assert.Equal(0, FanModeSet.Advance(-1, set));
        Assert.Equal(2, FanModeSet.Advance(0, set));
        Assert.Equal(3, FanModeSet.Advance(2, set));
        Assert.Equal(-1, FanModeSet.Advance(3, set));
    }

    [Fact]
    public void Fan_modes_require_two_enabled_entries()
    {
        var ok = FanModeSet.TryCreate(
            [("Only", "10", true), ("Off", "20", false), ("Off 2", "30", false), ("Off 3", "40", false), ("Off 4", "50", false)],
            out _,
            out var error);
        Assert.False(ok);
        Assert.Equal(FanModeSet.TooFewEnabledMessage, error);
    }

    [Fact]
    public void Msi_creator_z16_a11uet_is_not_allow_listed_for_duty_writes()
    {
        var decision = MsiEcPolicy.Evaluate(true, "Creator Z16 A11UET");
        Assert.Equal(MsiEcDecisionKind.NotListed, decision.Kind);
        Assert.Contains("A11UET", decision.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("write register", decision.Detail, StringComparison.OrdinalIgnoreCase);

        var off = MsiEcPolicy.Evaluate(false, "Creator Z16 A11UET");
        Assert.Equal(MsiEcDecisionKind.Disabled, off.Kind);

        var related = MsiEcPolicy.Evaluate(true, "MSI Creator Z16 A11UE");
        Assert.Equal(MsiEcDecisionKind.DutyUnsupported, related.Kind);
    }

    private static IReadOnlyList<RawSensor> LaptopSamples() =>
    [
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Total", MetricKind.Load, 27),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Package", MetricKind.Temperature, 58),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Core #1", MetricKind.Load, 22),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Core #1", MetricKind.Temperature, 55),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Core #2", MetricKind.Load, 31),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Core #2", MetricKind.Temperature, 61),
        Sample("/intelcpu/0", "Intel Core i7-11800H", DeviceKind.Cpu, GpuClass.None, "CPU Graphics", MetricKind.Temperature, 49),
        Sample("/gpu-intel-integrated/0", "Intel UHD Graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 4),
        Sample("/nvidiagpu/0", "NVIDIA GeForce RTX 3060 Laptop GPU", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Load, 12),
        Sample("/nvidiagpu/0", "NVIDIA GeForce RTX 3060 Laptop GPU", DeviceKind.Gpu, GpuClass.Discrete, "D3D 3D", MetricKind.Load, 8),
        Sample("/nvidiagpu/0", "NVIDIA GeForce RTX 3060 Laptop GPU", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Temperature, 51),
        Sample("/nvidiagpu/0", "NVIDIA GeForce RTX 3060 Laptop GPU", DeviceKind.Gpu, GpuClass.Discrete, "GPU Hot Spot", MetricKind.Temperature, 63),
    ];

    private static RawSensor Sample(string id, string name, DeviceKind kind, GpuClass gpuClass, string sensor, MetricKind metric, double value) =>
        new(id, name, kind, gpuClass, sensor, metric, value);
}
