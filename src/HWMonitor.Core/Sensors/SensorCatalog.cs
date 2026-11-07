// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace HWMonitor.Core.Sensors;

public static partial class SensorCatalog
{
    public static IReadOnlyList<SensorReading> Compose(
        IReadOnlyList<RawSensor> sensors,
        IReadOnlyList<AdapterUsage> adapters,
        double? processorUtilityPercent)
    {
        var groups = new List<HardwareGroup>();
        foreach (var sensor in sensors)
        {
            var group = groups.FirstOrDefault(item => item.Id == sensor.HardwareId && item.Kind == sensor.Kind);
            if (group is null)
            {
                group = new HardwareGroup(sensor.HardwareId, sensor.HardwareName, sensor.Kind, sensor.GpuClass);
                groups.Add(group);
            }

            group.Samples.Add(sensor);
        }

        var devices = new List<DeviceDraft>();
        foreach (var group in groups)
        {
            if (group.Kind == DeviceKind.Cpu)
            {
                devices.Add(BuildCpu(group));
            }
            else if (group.Kind == DeviceKind.Gpu)
            {
                devices.Add(BuildGpu(group));
            }
        }

        ApplyCpuGraphicsTemperature(devices);
        ApplyUsageFallbacks(devices, adapters, processorUtilityPercent);

        if (devices.Count == 0)
        {
            devices.AddRange(SyntheticDevices(adapters, processorUtilityPercent));
        }

        AssignLabels(devices);
        return devices.SelectMany(device => device.Published).ToArray();
    }

    private static DeviceDraft BuildCpu(HardwareGroup group)
    {
        var cores = new SortedDictionary<int, (double? Load, double? Temp)>();
        double? total = Find(group, MetricKind.Load, "CPU Total");
        double? package = Find(group, MetricKind.Temperature, "CPU Package");
        var packageSource = package is null ? SensorSources.Unavailable : SensorSources.Package;
        if (package is null)
        {
            package = Find(group, MetricKind.Temperature, "Core Max")
                ?? Find(group, MetricKind.Temperature, "CPU Core Max");
            if (package is not null)
            {
                packageSource = SensorSources.CoreMax;
            }
        }

        foreach (var sample in group.Samples)
        {
            var match = CoreName().Match(sample.SensorName);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            cores.TryGetValue(number, out var pair);
            if (sample.Metric == MetricKind.Load)
            {
                pair.Load = SanitizeUsage(sample.Value);
            }
            else if (sample.Metric == MetricKind.Temperature)
            {
                pair.Temp = SanitizeTemperature(sample.Value);
            }

            cores[number] = pair;
        }

        var usageSource = SensorSources.Unavailable;
        if (total is null && cores.Values.Any(pair => pair.Load is not null))
        {
            total = cores.Values.Where(pair => pair.Load is not null).Average(pair => pair.Load!.Value);
            usageSource = SensorSources.CoreAverage;
        }
        else if (total is not null)
        {
            usageSource = SensorSources.CpuTotal;
        }

        if (package is null)
        {
            var known = cores.Values.Where(pair => pair.Temp is not null).Select(pair => pair.Temp!.Value).ToArray();
            if (known.Length > 0)
            {
                package = known.Max();
                packageSource = SensorSources.HottestCore;
            }
        }

        var role = packageSource == SensorSources.HottestCore ? SensorRole.HottestCore : SensorRole.Package;
        var readings = new List<ReadingDraft>
        {
            new(true, null, role, SanitizeUsage(total), SanitizeTemperature(package), usageSource, packageSource),
        };

        foreach (var (number, pair) in cores)
        {
            readings.Add(new ReadingDraft(
                false,
                number,
                SensorRole.Core,
                pair.Load,
                pair.Temp,
                pair.Load is null ? SensorSources.Unavailable : SensorSources.CpuTotal,
                pair.Temp is null ? SensorSources.Unavailable : SensorSources.Package));
        }

        var draft = new DeviceDraft(group.Id, group.Name, DeviceKind.Cpu, GpuClass.None, readings)
        {
            CpuGraphicsCelsius = SanitizeTemperature(
                Find(group, MetricKind.Temperature, "CPU Graphics")
                ?? Find(group, MetricKind.Temperature, "GPU Graphics")),
        };
        return draft;
    }

    private static DeviceDraft BuildGpu(HardwareGroup group)
    {
        var coreLoad = Find(group, MetricKind.Load, "GPU Core");
        var d3dLoad = Find(group, MetricKind.Load, "D3D 3D");
        var load = coreLoad ?? d3dLoad;
        var loadSource = coreLoad is not null
            ? SensorSources.GpuCore
            : d3dLoad is not null ? SensorSources.D3D : SensorSources.Unavailable;

        var coreTemp = Find(group, MetricKind.Temperature, "GPU Core");
        var hotSpot = Find(group, MetricKind.Temperature, "GPU Hot Spot");
        var temp = coreTemp ?? hotSpot;
        var tempSource = coreTemp is not null
            ? SensorSources.GpuCore
            : hotSpot is not null ? SensorSources.HotSpot : SensorSources.Unavailable;

        return new DeviceDraft(
            group.Id,
            group.Name,
            DeviceKind.Gpu,
            group.GpuClass,
            [
                new ReadingDraft(true, null, SensorRole.Adapter, SanitizeUsage(load), SanitizeTemperature(temp), loadSource, tempSource),
            ]);
    }

    private static void ApplyCpuGraphicsTemperature(List<DeviceDraft> devices)
    {
        var graphics = devices
            .Where(device => device.Kind == DeviceKind.Cpu)
            .Select(device => device.CpuGraphicsCelsius)
            .FirstOrDefault(value => value is not null);

        if (graphics is null)
        {
            return;
        }

        foreach (var device in devices.Where(item => item.Kind == DeviceKind.Gpu && item.GpuClass == GpuClass.Integrated))
        {
            var primary = device.Readings[0];
            if (primary.TemperatureCelsius is null)
            {
                device.Readings[0] = primary with
                {
                    TemperatureCelsius = graphics,
                    TemperatureSource = SensorSources.CpuGraphics,
                    Role = SensorRole.CpuGraphics,
                };
            }
        }
    }

    private static void ApplyUsageFallbacks(
        List<DeviceDraft> devices,
        IReadOnlyList<AdapterUsage> adapters,
        double? processorUtilityPercent)
    {
        var utility = SanitizeUsage(processorUtilityPercent, allowTurbo: true);
        foreach (var device in devices.Where(item => item.Kind == DeviceKind.Cpu))
        {
            var primary = device.Readings[0];
            if (primary.UsagePercent is null && utility is not null)
            {
                device.Readings[0] = primary with
                {
                    UsagePercent = utility,
                    UsageSource = SensorSources.ProcessorUtility,
                };
            }
        }

        var used = new HashSet<int>();
        var gpus = devices.Where(item => item.Kind == DeviceKind.Gpu).ToArray();
        foreach (var gpu in gpus)
        {
            var primary = gpu.Readings[0];
            if (primary.UsagePercent is not null)
            {
                continue;
            }

            var index = MatchAdapter(gpu.Name, gpu.GpuClass, adapters, used);
            if (index < 0)
            {
                continue;
            }

            used.Add(index);
            var usage = SanitizeUsage(adapters[index].UsagePercent);
            if (usage is null)
            {
                continue;
            }

            gpu.Readings[0] = primary with
            {
                UsagePercent = usage,
                UsageSource = SensorSources.GpuEngine,
            };
        }
    }

    private static int MatchAdapter(string hardwareName, GpuClass gpuClass, IReadOnlyList<AdapterUsage> adapters, HashSet<int> used)
    {
        var bestIndex = -1;
        var bestScore = 0;
        for (var i = 0; i < adapters.Count; i++)
        {
            if (used.Contains(i) || adapters[i].UsagePercent is null)
            {
                continue;
            }

            var score = Score(hardwareName, gpuClass, adapters[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0 && bestScore >= 5)
        {
            return bestIndex;
        }

        var leftovers = Enumerable.Range(0, adapters.Count)
            .Where(index => !used.Contains(index) && adapters[index].UsagePercent is not null)
            .ToArray();
        return leftovers.Length == 1 ? leftovers[0] : -1;
    }

    private static int Score(string hardwareName, GpuClass gpuClass, AdapterUsage adapter)
    {
        var score = gpuClass != GpuClass.None && gpuClass == adapter.GpuClass ? 5 : 0;
        var hardwareTokens = Tokens(hardwareName);
        var adapterTokens = Tokens(adapter.Name);
        score += hardwareTokens.Intersect(adapterTokens).Count() * 3;
        var hardwareVendor = Vendor(hardwareTokens);
        var adapterVendor = Vendor(adapterTokens);
        if (hardwareVendor is not null && hardwareVendor == adapterVendor)
        {
            score += 10;
        }

        return score;
    }

    private static IEnumerable<DeviceDraft> SyntheticDevices(IReadOnlyList<AdapterUsage> adapters, double? processorUtilityPercent)
    {
        var utility = SanitizeUsage(processorUtilityPercent, allowTurbo: true);
        if (utility is not null)
        {
            yield return new DeviceDraft(
                "cpu:usage",
                "Processor",
                DeviceKind.Cpu,
                GpuClass.None,
                [new ReadingDraft(true, null, SensorRole.Package, utility, null, SensorSources.ProcessorUtility, SensorSources.Unavailable)]);
        }

        for (var i = 0; i < adapters.Count; i++)
        {
            var adapter = adapters[i];
            yield return new DeviceDraft(
                $"gpu:luid:{adapter.LuidHigh:X8}:{adapter.LuidLow:X8}",
                string.IsNullOrWhiteSpace(adapter.Name) ? "Graphics adapter" : adapter.Name,
                DeviceKind.Gpu,
                adapter.GpuClass,
                [new ReadingDraft(true, null, SensorRole.Adapter, SanitizeUsage(adapter.UsagePercent), null, adapter.UsagePercent is null ? SensorSources.Unavailable : SensorSources.GpuEngine, SensorSources.Unavailable)]);
        }
    }

    private static void AssignLabels(List<DeviceDraft> devices)
    {
        var cpus = devices.Where(device => device.Kind == DeviceKind.Cpu).ToArray();
        var integrated = devices.Where(device => device.Kind == DeviceKind.Gpu && device.GpuClass == GpuClass.Integrated).ToArray();
        var discrete = devices.Where(device => device.Kind == DeviceKind.Gpu && device.GpuClass != GpuClass.Integrated).ToArray();
        LabelGroup(cpus, "CPU");
        LabelGroup(integrated, "iGPU");
        LabelGroup(discrete, "GPU");
    }

    private static void LabelGroup(IReadOnlyList<DeviceDraft> devices, string stem)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            var label = devices.Count == 1 ? stem : $"{stem} {i + 1}";
            var readings = new List<SensorReading>();
            foreach (var draft in devices[i].Readings)
            {
                var shortLabel = draft.Role == SensorRole.Core ? $"Core {draft.CoreNumber}" : label;
                readings.Add(new SensorReading(
                    draft.Role == SensorRole.Core
                        ? $"{(devices[i].Kind == DeviceKind.Cpu ? "cpu" : "gpu")}:{devices[i].Id}:core:{draft.CoreNumber}"
                        : $"{(devices[i].Kind == DeviceKind.Cpu ? "cpu" : "gpu")}:{devices[i].Id}",
                    devices[i].Id,
                    devices[i].Kind,
                    devices[i].GpuClass,
                    draft.IsPrimary,
                    draft.CoreNumber,
                    devices[i].Name,
                    shortLabel,
                    draft.Role,
                    draft.UsagePercent,
                    draft.TemperatureCelsius,
                    draft.UsageSource,
                    draft.TemperatureSource));
            }

            devices[i].Published = readings;
        }
    }

    private static double? Find(HardwareGroup group, MetricKind metric, string name)
    {
        var sample = group.Samples.FirstOrDefault(item =>
            item.Metric == metric && item.SensorName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return sample is null ? null : sample.Value;
    }

    private static double? SanitizeTemperature(double? value)
    {
        if (value is null || value < -20 || value > 150)
        {
            return null;
        }

        return value;
    }

    private static double? SanitizeUsage(double? value, bool allowTurbo = false)
    {
        if (value is null)
        {
            return null;
        }

        if (value < 0)
        {
            return 0;
        }

        var ceiling = allowTurbo ? 400 : 100;
        return value > ceiling ? null : value;
    }

    private static HashSet<string> Tokens(string text) =>
        TokenSplit().Split(text.ToLowerInvariant()).Where(token => token.Length >= 3 && token is not "laptop" and not "graphics" and not "adapter" and not "device").ToHashSet();

    private static string? Vendor(HashSet<string> tokens)
    {
        if (tokens.Overlaps(["nvidia", "geforce", "quadro"]))
        {
            return "nvidia";
        }

        if (tokens.Overlaps(["amd", "radeon"]))
        {
            return "amd";
        }

        if (tokens.Overlaps(["intel", "uhd", "iris"]))
        {
            return "intel";
        }

        if (tokens.Contains("arc"))
        {
            return "intel-arc";
        }

        return null;
    }

    [GeneratedRegex(@"^CPU Core #(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoreName();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplit();

    private sealed class HardwareGroup(string id, string name, DeviceKind kind, GpuClass gpuClass)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public DeviceKind Kind { get; } = kind;

        public GpuClass GpuClass { get; } = gpuClass;

        public List<RawSensor> Samples { get; } = [];
    }

    private sealed class DeviceDraft(string id, string name, DeviceKind kind, GpuClass gpuClass, List<ReadingDraft> readings)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public DeviceKind Kind { get; } = kind;

        public GpuClass GpuClass { get; } = gpuClass;

        public List<ReadingDraft> Readings { get; } = readings;

        public double? CpuGraphicsCelsius { get; set; }

        public List<SensorReading> Published { get; set; } = [];
    }

    private sealed record ReadingDraft(
        bool IsPrimary,
        int? CoreNumber,
        SensorRole Role,
        double? UsagePercent,
        double? TemperatureCelsius,
        string UsageSource,
        string TemperatureSource);
}
