// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace HWMonitor.Core.Sensors;

public readonly record struct GpuEngineInstance(
    int ProcessId,
    uint LuidHigh,
    uint LuidLow,
    int PhysicalAdapter,
    int EngineIndex,
    string EngineType);

public sealed record EngineSample(string InstanceName, double UtilizationPercent);

public static partial class GpuEngineUsage
{
    public static bool TryParse(string instanceName, out GpuEngineInstance instance)
    {
        instance = default;
        var match = InstancePattern().Match(instanceName);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["pid"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            || !uint.TryParse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            || !uint.TryParse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)
            || !int.TryParse(match.Groups["phys"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var phys)
            || !int.TryParse(match.Groups["eng"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eng))
        {
            return false;
        }

        instance = new GpuEngineInstance(pid, high, low, phys, eng, match.Groups["type"].Value);
        return true;
    }

    public static IReadOnlyList<AdapterUsage> Aggregate(
        IReadOnlyList<EngineSample> samples,
        IReadOnlyList<AdapterDescriptor> adapters)
    {
        var byEngine = new Dictionary<(uint High, uint Low, int Phys, int Eng), double>();
        foreach (var sample in samples)
        {
            if (!TryParse(sample.InstanceName, out var parsed)
                || !parsed.EngineType.Equals("3D", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = (parsed.LuidHigh, parsed.LuidLow, parsed.PhysicalAdapter, parsed.EngineIndex);
            byEngine[key] = byEngine.GetValueOrDefault(key) + Math.Max(0, sample.UtilizationPercent);
        }

        var byAdapter = new Dictionary<(uint High, uint Low), double>();
        foreach (var (key, value) in byEngine)
        {
            var adapterKey = (key.High, key.Low);
            var capped = Math.Clamp(value, 0, 100);
            byAdapter[adapterKey] = Math.Max(byAdapter.GetValueOrDefault(adapterKey), capped);
        }

        var results = new List<AdapterUsage>();
        var seen = new HashSet<(uint, uint)>();
        foreach (var adapter in adapters)
        {
            var key = (adapter.LuidHigh, adapter.LuidLow);
            seen.Add(key);
            byAdapter.TryGetValue(key, out var usage);
            var known = byAdapter.ContainsKey(key);
            results.Add(new AdapterUsage(adapter.LuidLow, adapter.LuidHigh, adapter.Name, adapter.GpuClass, known ? usage : null));
        }

        foreach (var (key, usage) in byAdapter)
        {
            if (seen.Contains(key))
            {
                continue;
            }

            results.Add(new AdapterUsage(key.Low, key.High, "Graphics adapter", GpuClass.None, usage));
        }

        return results;
    }

    [GeneratedRegex(
        @"^pid_(?<pid>\d+)_luid_0x(?<high>[0-9A-Fa-f]+)_0x(?<low>[0-9A-Fa-f]+)_phys_(?<phys>\d+)_eng_(?<eng>\d+)_engtype_(?<type>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();
}
