// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;

namespace HWMonitor.Core.Sensors;

public enum VisibilityChoice
{
    Default,
    Show,
    Hide,
}

public sealed record DevicePresentationEntry(
    string Id,
    string DefaultShort,
    string DefaultFull,
    string? ShortLabel,
    string? FullName,
    VisibilityChoice List,
    VisibilityChoice Dock);

public sealed class PresentationMap
{
    public const int MaxShortLabelLength = 32;
    public const int MaxFullNameLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<string, DevicePresentationEntry> _rows;

    private PresentationMap(IReadOnlyList<DevicePresentationEntry> entries)
    {
        Entries = entries;
        _rows = new Dictionary<string, DevicePresentationEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            _rows[entry.Id] = entry;
        }
    }

    public static PresentationMap Empty { get; } = new([]);

    public IReadOnlyList<DevicePresentationEntry> Entries { get; }

    public static PresentationMap FromEntries(IEnumerable<DevicePresentationEntry> entries)
    {
        var clean = new List<DevicePresentationEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !seen.Add(entry.Id))
            {
                continue;
            }

            clean.Add(entry with
            {
                DefaultShort = Clean(entry.DefaultShort, MaxShortLabelLength) ?? string.Empty,
                DefaultFull = Clean(entry.DefaultFull, MaxFullNameLength) ?? string.Empty,
                ShortLabel = Clean(entry.ShortLabel, MaxShortLabelLength),
                FullName = Clean(entry.FullName, MaxFullNameLength),
            });
        }

        return new PresentationMap(clean);
    }

    public static PresentationMap Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            var file = JsonSerializer.Deserialize<PresentationFile>(json, JsonOptions);
            return FromEntries(file?.Devices ?? []);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string Serialize()
    {
        var file = new PresentationFile { Devices = Entries.ToList() };
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    public static VisibilityChoice ParseVisibility(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "show" => VisibilityChoice.Show,
        "hide" => VisibilityChoice.Hide,
        _ => VisibilityChoice.Default,
    };

    public static string VisibilityToken(VisibilityChoice choice) => choice switch
    {
        VisibilityChoice.Show => "show",
        VisibilityChoice.Hide => "hide",
        _ => "default",
    };

    public string ResolveShort(string id, string fallback) =>
        _rows.TryGetValue(id, out var entry) && !string.IsNullOrWhiteSpace(entry.ShortLabel)
            ? entry.ShortLabel!
            : fallback;

    public string ResolveFull(string id, string fallback) =>
        _rows.TryGetValue(id, out var entry) && !string.IsNullOrWhiteSpace(entry.FullName)
            ? entry.FullName!
            : fallback;

    public bool AppearsInList(string id, bool defaultVisible) => Appears(_rows.TryGetValue(id, out var entry) ? entry.List : VisibilityChoice.Default, defaultVisible);

    public bool AppearsInDock(string id, bool defaultVisible) => Appears(_rows.TryGetValue(id, out var entry) ? entry.Dock : VisibilityChoice.Default, defaultVisible);

    public SensorReading Apply(SensorReading reading) => reading with
    {
        ShortLabel = ResolveShort(reading.Id, reading.ShortLabel),
        HardwareName = ResolveFull(reading.Id, reading.HardwareName),
    };

    private static bool Appears(VisibilityChoice choice, bool defaultVisible) => choice switch
    {
        VisibilityChoice.Show => true,
        VisibilityChoice.Hide => false,
        _ => defaultVisible,
    };

    private static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().Replace("\r", string.Empty).Replace("\n", " ");
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private sealed class PresentationFile
    {
        public List<DevicePresentationEntry> Devices { get; set; } = [];
    }
}
