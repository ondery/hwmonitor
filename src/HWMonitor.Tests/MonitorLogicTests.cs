// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using Xunit;

namespace HWMonitor.Tests;

public class MonitorLogicTests
{
    [Fact]
    public void Formats_the_compact_reading()
    {
        var text = ReadingText.Format("{0} {1} · {2}", "CPU", 27, 58, "n/a", CultureInfo.InvariantCulture);
        Assert.Equal("CPU 27% · 58°C", text);
        Assert.Equal("55% · 72°C", ReadingText.Value(55, 72, "n/a", false, CultureInfo.InvariantCulture));
        Assert.Equal("55%", ReadingText.Value(55, null, "n/a", true, CultureInfo.InvariantCulture));
        Assert.Equal("CPU 55%", ReadingText.ListLine("CPU", 55, null, "n/a", true, CultureInfo.InvariantCulture));
        Assert.Equal("n/a · 51°C", ReadingText.Value(null, 51, "n/a", false, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void English_resources_explain_the_sensor_service()
    {
        Assert.Equal("HW Monitor", UiText.AppName);
        Assert.Equal("Install sensor service for CPU temperature", UiText.StatusServiceTitle);
        Assert.Equal("Package (shared die)", UiText.RoleSharedPackage);
        Assert.Equal("Microsoft.CommandPalette.Dock.TitleWidth", DockProperties.TitleWidth);
        Assert.Equal("Microsoft.CommandPalette.Dock.SubtitleWidth", DockProperties.SubtitleWidth);
        Assert.Equal("Microsoft.CommandPalette.Dock.TabularDigits", DockProperties.TabularDigits);
        Assert.Equal("text:100% · 100°C", DockProperties.Sample(ReadingText.SensorTitleSample));
        Assert.DoesNotContain("18ch", DockProperties.Sample(ReadingText.SensorTitleSample), StringComparison.Ordinal);
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
    public void Compose_keeps_package_cores_and_both_gpu_classes()
    {
        var readings = SensorCatalog.Compose(SampleMachine(), [], null);
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
        var samples = SampleMachine().Where(item => item.SensorName != "CPU Package").ToArray();
        var cpu = SensorCatalog.Compose(samples, [], null).Single(item => item.IsPrimary && item.Kind == DeviceKind.Cpu);
        Assert.Equal(61, cpu.TemperatureCelsius);
        Assert.Equal(SensorRole.HottestCore, cpu.Role);
    }

    [Fact]
    public void Unrecognized_sensor_names_do_not_become_package_temperature()
    {
        var readings = SensorCatalog.Compose(
            [Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "VRM", MetricKind.Temperature, 40)],
            [],
            null);
        var cpu = readings.Single(item => item.IsPrimary);
        Assert.Null(cpu.TemperatureCelsius);
        Assert.Equal("CPU", cpu.ShortLabel);
    }

    [Fact]
    public void Package_synonyms_are_accepted()
    {
        var readings = SensorCatalog.Compose(
            [Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Tctl", MetricKind.Temperature, 66)],
            [],
            null);
        Assert.Equal(66, readings.Single().TemperatureCelsius);
        Assert.Equal(SensorSources.Package, readings.Single().TemperatureSource);
    }

    [Fact]
    public void Integrated_graphics_without_a_sensor_use_the_cpu_package()
    {
        var samples = SampleMachine().Where(item => item.SensorName != "CPU Graphics").ToArray();
        var igpu = SensorCatalog.Compose(samples, [], null).Single(item => item.ShortLabel == "iGPU");
        Assert.Equal(58, igpu.TemperatureCelsius);
        Assert.Equal(SensorSources.SharedPackage, igpu.TemperatureSource);
        Assert.Equal(SensorRole.SharedPackage, igpu.Role);
        Assert.True(TemperatureGaps.IsSharedDieGap(igpu with { TemperatureCelsius = null }));
        Assert.False(TemperatureGaps.IsSharedDieGap(igpu));
    }

    [Fact]
    public void A_discrete_adapter_keeps_a_missing_temperature_empty()
    {
        var samples = SampleMachine().Where(item => item.Metric != MetricKind.Temperature || item.Kind != DeviceKind.Gpu || item.GpuClass != GpuClass.Discrete).ToArray();
        var gpu = SensorCatalog.Compose(samples, [], null).Single(item => item.ShortLabel == "GPU");
        Assert.Null(gpu.TemperatureCelsius);
        Assert.Equal(12, gpu.UsagePercent);
    }

    [Fact]
    public void Extra_adapters_are_added_when_the_sensor_library_did_not_see_them()
    {
        var cpuOnly = SampleMachine().Where(item => item.Kind == DeviceKind.Cpu && item.SensorName != "CPU Graphics").ToArray();
        var adapters = new[]
        {
            new AdapterUsage(1, 0, "Integrated graphics", GpuClass.Integrated, 6),
            new AdapterUsage(2, 0, "Discrete graphics", GpuClass.Discrete, 15),
        };
        var readings = SensorCatalog.Compose(cpuOnly, adapters, null);
        var igpu = readings.Single(item => item.ShortLabel == "iGPU");
        var gpu = readings.Single(item => item.ShortLabel == "GPU");
        Assert.Equal(6, igpu.UsagePercent);
        Assert.Equal(58, igpu.TemperatureCelsius);
        Assert.Equal(SensorSources.SharedPackage, igpu.TemperatureSource);
        Assert.Equal(15, gpu.UsagePercent);
        Assert.Null(gpu.TemperatureCelsius);
    }

    [Fact]
    public void Several_devices_keep_the_first_label_bare()
    {
        var samples = new List<RawSensor>
        {
            Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Total", MetricKind.Load, 10),
            Sample("/cpu/1", "Second processor", DeviceKind.Cpu, GpuClass.None, "CPU Total", MetricKind.Load, 20),
            Sample("/gpu/integrated/0", "Integrated graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 1),
            Sample("/gpu/integrated/1", "Second integrated graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 2),
            Sample("/gpu/0", "Discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Load, 3),
            Sample("/gpu/1", "Second discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Load, 4),
        };
        var readings = SensorCatalog.Compose(samples, [], null);
        Assert.Equal(["CPU", "CPU 2"], readings.Where(item => item.Kind == DeviceKind.Cpu && item.IsPrimary).Select(item => item.ShortLabel).ToArray());
        Assert.Equal(["iGPU", "iGPU 2"], readings.Where(item => item.GpuClass == GpuClass.Integrated).Select(item => item.ShortLabel).ToArray());
        Assert.Equal(["GPU", "GPU 2"], readings.Where(item => item.GpuClass == GpuClass.Discrete).Select(item => item.ShortLabel).ToArray());
    }

    [Fact]
    public void Each_integrated_gpu_follows_the_matching_processor_package()
    {
        var samples = new[]
        {
            Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Package", MetricKind.Temperature, 40),
            Sample("/cpu/1", "Second processor", DeviceKind.Cpu, GpuClass.None, "CPU Package", MetricKind.Temperature, 70),
            Sample("/gpu/integrated/0", "Integrated graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 1),
            Sample("/gpu/integrated/1", "Second integrated graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 2),
        };
        var readings = SensorCatalog.Compose(samples, [], null);
        Assert.Equal(40, readings.Single(item => item.ShortLabel == "iGPU").TemperatureCelsius);
        Assert.Equal(70, readings.Single(item => item.ShortLabel == "iGPU 2").TemperatureCelsius);
    }

    [Fact]
    public void Performance_counters_fill_missing_usage()
    {
        var samples = SampleMachine().Where(item =>
            item.SensorName is not "CPU Total" and not "D3D 3D"
            && !(item.Metric == MetricKind.Load && item.SensorName.StartsWith("CPU Core", StringComparison.Ordinal))).ToList();
        samples.Add(Sample("/gpu/integrated/0", "Intel Graphics", DeviceKind.Gpu, GpuClass.Integrated, "GPU Core", MetricKind.Temperature, 49));
        var adapters = new[]
        {
            new AdapterUsage(0xC581, 0, "Intel Graphics", GpuClass.Integrated, 6),
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
            [new AdapterDescriptor(0xC581, 0, "Integrated graphics", GpuClass.Integrated)]);
        Assert.Equal(65, usage[0].UsagePercent);
    }

    [Fact]
    public void Presentation_overrides_labels_and_visibility()
    {
        var id = "cpu:/cpu/0";
        var map = PresentationMap.FromEntries(
        [
            new DevicePresentationEntry(id, "CPU", "Processor", "Pkg", "Main processor", VisibilityChoice.Hide, VisibilityChoice.Show),
            new DevicePresentationEntry("cpu:/cpu/0:core:1", "Core 1", "Processor", "  ", null, VisibilityChoice.Show, VisibilityChoice.Default),
        ]);
        Assert.Equal("Pkg", map.ResolveShort(id, "CPU"));
        Assert.Equal("Main processor", map.ResolveFull(id, "Processor"));
        Assert.False(map.AppearsInList(id, true));
        Assert.True(map.AppearsInDock(id, false));
        Assert.Equal("Core 1", map.ResolveShort("cpu:/cpu/0:core:1", "Core 1"));
        Assert.True(map.AppearsInList("cpu:/cpu/0:core:1", false));
        Assert.False(map.AppearsInDock("cpu:/cpu/0:core:1", false));

        var reading = SensorCatalog.Compose(SampleMachine(), [], null).Single(item => item.IsPrimary && item.Kind == DeviceKind.Cpu);
        var shown = map.Apply(reading with { Id = id });
        Assert.Equal("Pkg", shown.ShortLabel);
        Assert.Equal("Main processor", shown.HardwareName);

        var roundTrip = PresentationMap.Deserialize(map.Serialize());
        Assert.Equal("Pkg", roundTrip.ResolveShort(id, "CPU"));
        Assert.Equal(VisibilityChoice.Hide, roundTrip.Entries.Single(entry => entry.Id == id).List);
        Assert.Equal(PresentationMap.Empty.Entries.Count, PresentationMap.Deserialize("not json").Entries.Count);
    }

    [Fact]
    public void Long_custom_labels_are_trimmed()
    {
        var map = PresentationMap.FromEntries(
        [
            new DevicePresentationEntry("cpu:/cpu/0", "CPU", "Processor", new string('A', 80), new string('B', 200), VisibilityChoice.Default, VisibilityChoice.Default),
        ]);
        Assert.Equal(PresentationMap.MaxShortLabelLength, map.ResolveShort("cpu:/cpu/0", "CPU").Length);
        Assert.Equal(PresentationMap.MaxFullNameLength, map.ResolveFull("cpu:/cpu/0", "Processor").Length);
    }

    [Fact]
    public void Gpu_class_uses_library_type_and_dedicated_memory()
    {
        Assert.Equal(GpuClass.Integrated, GpuClassResolver.Resolve(new GpuClassSignals("GpuIntel", "IntelIntegratedGpu", "/gpu/0", null)));
        Assert.Equal(GpuClass.Integrated, GpuClassResolver.Resolve(new GpuClassSignals("GpuAmd", "AmdGpu", "/gpu-integrated/0", null)));
        Assert.Equal(GpuClass.Discrete, GpuClassResolver.Resolve(new GpuClassSignals("GpuIntel", "IntelDiscreteGpu", "/gpu/1", null)));
        Assert.Equal(GpuClass.Integrated, GpuClassResolver.Resolve(new GpuClassSignals("GpuAmd", "AmdGpu", "/gpu-amd/0", 256L * 1024 * 1024)));
        Assert.Equal(GpuClass.Discrete, GpuClassResolver.Resolve(new GpuClassSignals("GpuAmd", "AmdGpu", "/gpu-amd/1", 8L * 1024 * 1024 * 1024)));
        Assert.Equal(GpuClass.Discrete, GpuClassResolver.Resolve(new GpuClassSignals("GpuNvidia", "NvidiaGpu", "/gpu-nvidia/0", 0)));
        Assert.Equal(GpuClass.None, GpuClassResolver.Resolve(new GpuClassSignals("Motherboard", "Board", "/board/0", 0)));
    }

    [Fact]
    public void Adapter_memory_matches_by_vendor()
    {
        var adapters = new[]
        {
            new AdapterDescriptor(1, 0, "First discrete", GpuClass.Discrete, 8L * 1024 * 1024 * 1024, DisplayVendors.Nvidia),
            new AdapterDescriptor(2, 0, "Integrated graphics", GpuClass.Integrated, 0, DisplayVendors.Amd),
        };
        var match = LibraryAdapterMatch.Match("GpuAmd", "Graphics", adapters);
        Assert.NotNull(match);
        Assert.Equal(DisplayVendors.Amd, match.VendorId);
        Assert.Equal(0ul, match.DedicatedVideoBytes);
    }

    [Fact]
    public void Pipe_get_round_trips_and_rejects_other_operations()
    {
        var request = SensorPipeProtocol.EncodeGetRequest();
        var response = SensorPipeProtocol.Handle(request, () => new SensorReadResult(SampleMachine().Take(1).ToArray(), null));
        Assert.True(SensorPipeProtocol.TryParseSnapshot(response, out var snapshot, out var error));
        Assert.Null(error);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.Ok);
        Assert.Equal(SensorServiceContract.PipeName, "Ondery.HWMonitor.Sensors");
        Assert.Equal("HWMonitorSensorService", SensorServiceContract.ServiceName);
        Assert.Equal("HW Monitor Sensor Service", SensorServiceContract.DisplayName);
        Assert.Single(snapshot.Sensors);
        Assert.Equal(27, snapshot.Sensors[0].Value);

        var denied = Encoding.UTF8.GetBytes("{\"v\":1,\"op\":\"set\"}");
        var deniedResponse = SensorPipeProtocol.Handle(denied, () => throw new InvalidOperationException("must not read"));
        Assert.True(SensorPipeProtocol.TryParseSnapshot(deniedResponse, out var failed, out _));
        Assert.False(failed!.Ok);
        Assert.Equal(SensorServiceContract.UnsupportedOperation, failed.Error);
        Assert.Empty(failed.Sensors);

        using var stream = new MemoryStream();
        SensorPipeFrames.Write(stream, request);
        stream.Position = 0;
        Assert.True(SensorPipeFrames.TryRead(stream, out var frame, out var frameError));
        Assert.Null(frameError);
        Assert.Equal(request, frame);

        using var broken = new MemoryStream();
        broken.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F });
        broken.Position = 0;
        Assert.False(SensorPipeFrames.TryRead(broken, out _, out var lengthError));
        Assert.False(string.IsNullOrWhiteSpace(lengthError));
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

    private static IReadOnlyList<RawSensor> SampleMachine() =>
    [
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Total", MetricKind.Load, 27),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Package", MetricKind.Temperature, 58),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Core #1", MetricKind.Load, 22),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Core #1", MetricKind.Temperature, 55),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Core #2", MetricKind.Load, 31),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Core #2", MetricKind.Temperature, 61),
        Sample("/cpu/0", "Processor", DeviceKind.Cpu, GpuClass.None, "CPU Graphics", MetricKind.Temperature, 49),
        Sample("/gpu/integrated/0", "Integrated graphics", DeviceKind.Gpu, GpuClass.Integrated, "D3D 3D", MetricKind.Load, 4),
        Sample("/gpu/0", "Discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Load, 12),
        Sample("/gpu/0", "Discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "D3D 3D", MetricKind.Load, 8),
        Sample("/gpu/0", "Discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "GPU Core", MetricKind.Temperature, 51),
        Sample("/gpu/0", "Discrete graphics", DeviceKind.Gpu, GpuClass.Discrete, "GPU Hot Spot", MetricKind.Temperature, 63),
    ];

    private static RawSensor Sample(string id, string name, DeviceKind kind, GpuClass gpuClass, string sensor, MetricKind metric, double value) =>
        new(id, name, kind, gpuClass, sensor, metric, value);
}
