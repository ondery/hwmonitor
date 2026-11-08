// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Sensors;

public static class LibraryAdapterMatch
{
    public static uint? VendorIdForHardwareType(string? hardwareType) => hardwareType switch
    {
        "GpuNvidia" => DisplayVendors.Nvidia,
        "GpuAmd" => DisplayVendors.Amd,
        "GpuIntel" => DisplayVendors.Intel,
        _ => null,
    };

    public static AdapterDescriptor? Match(
        string hardwareType,
        string hardwareName,
        IReadOnlyList<AdapterDescriptor> adapters,
        ISet<int>? used = null)
    {
        used ??= new HashSet<int>();
        var vendor = VendorIdForHardwareType(hardwareType);
        var best = -1;
        var bestScore = 0;
        for (var i = 0; i < adapters.Count; i++)
        {
            if (used.Contains(i))
            {
                continue;
            }

            var score = 0;
            if (vendor is uint id && adapters[i].VendorId == id)
            {
                score += 10;
            }

            score += SharedTokens(hardwareName, adapters[i].Name) * 3;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best >= 0 && bestScore >= 10)
        {
            used.Add(best);
            return adapters[best];
        }

        var vendorMatches = Enumerable.Range(0, adapters.Count)
            .Where(index => !used.Contains(index) && vendor is uint id && adapters[index].VendorId == id)
            .ToArray();
        if (vendorMatches.Length == 1)
        {
            used.Add(vendorMatches[0]);
            return adapters[vendorMatches[0]];
        }

        return null;
    }

    private static int SharedTokens(string left, string right)
    {
        var rightTokens = Tokens(right);
        return Tokens(left).Count(token => rightTokens.Contains(token));
    }

    private static HashSet<string> Tokens(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '-', '_', '(', ')', '/', '\\', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet();
}
