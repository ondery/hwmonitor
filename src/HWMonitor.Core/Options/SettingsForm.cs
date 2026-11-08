// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;

namespace HWMonitor.Core.Options;

public static class SettingsKeys
{
    public const string Refresh = "hwmonitor.refreshIntervalMs";
    public const string Compact = "hwmonitor.compactMode";
    public const string DockColorIcons = "hwmonitor.dock.colorIcons";
    public const string BlueBelow = "hwmonitor.color.blueBelow";
    public const string YellowFrom = "hwmonitor.color.yellowFrom";
    public const string OrangeFrom = "hwmonitor.color.orangeFrom";
    public const string RedFrom = "hwmonitor.color.redFrom";
    public const string Blue = "hwmonitor.color.blue";
    public const string Yellow = "hwmonitor.color.yellow";
    public const string Orange = "hwmonitor.color.orange";
    public const string Red = "hwmonitor.color.red";
    public const string CoolSelection = "hwmonitor.cool.selection";
    public const string CoolMax = "hwmonitor.cool.maxProcessorState";
    public const string CoolBoost = "hwmonitor.cool.limitBoost";
    public const string BoostMode = "hwmonitor.cool.boostMode";
    public const string Auto = "hwmonitor.autoCool.enabled";
    public const string AutoOn = "hwmonitor.autoCool.activateCelsius";
    public const string AutoOnSeconds = "hwmonitor.autoCool.activateSeconds";
    public const string AutoOff = "hwmonitor.autoCool.deactivateCelsius";
    public const string AutoOffSeconds = "hwmonitor.autoCool.deactivateSeconds";
    public const string Pick = "hwmonitor.pick";

    public static string Preset(string colorKey) => colorKey + ".preset";

    public static string Custom(string colorKey) => colorKey + ".custom";

    public static string Device(string id, string part)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return $"hwmonitor.device.{hash[..20]}.{part}";
    }

    public static string CoresFold(string packageId) => "cores-" + Device(packageId, "fold").Split('.')[2];
}

public sealed record DeviceSettingSeed(
    string Id,
    string DefaultShort,
    string DefaultFull,
    string? ShortLabel,
    string? FullName,
    VisibilityChoice List,
    VisibilityChoice Dock);

public sealed record DeviceFormState(
    string Id,
    string DefaultShort,
    string DefaultFull,
    string ShortLabel,
    string FullName,
    VisibilityChoice List,
    VisibilityChoice Dock);

public sealed record SettingsFormState(
    int RefreshIntervalMs,
    bool Compact,
    bool ColorDockIcons,
    int BlueBelow,
    int YellowFrom,
    int OrangeFrom,
    int RedFrom,
    ColorChoice Blue,
    ColorChoice Yellow,
    ColorChoice Orange,
    ColorChoice Red,
    int CoolMaximum,
    bool LimitBoost,
    int BoostValue,
    CoolSelection Selection,
    bool AutoEnabled,
    int AutoOnCelsius,
    int AutoOnSeconds,
    int AutoOffCelsius,
    int AutoOffSeconds,
    IReadOnlyList<DeviceFormState> Devices)
{
    public ColorScale Colors => new(BlueBelow, YellowFrom, OrangeFrom, RedFrom, Blue.Color, Yellow.Color, Orange.Color, Red.Color);

    public MonitorOptions ToOptions()
    {
        var entries = Devices.Select(device => new DevicePresentationEntry(
            device.Id,
            device.DefaultShort,
            device.DefaultFull,
            Blank(device.ShortLabel),
            Blank(device.FullName),
            device.List,
            device.Dock));
        return new MonitorOptions(
            RefreshIntervalMs,
            Compact,
            ColorDockIcons,
            Colors,
            new CoolOptions(CoolMaximum, LimitBoost, BoostValue),
            Selection,
            new AutoCoolOptions(Selection == CoolSelection.Auto, AutoOnCelsius, AutoOnSeconds, AutoOffCelsius, AutoOffSeconds),
            PresentationMap.FromEntries(entries));
    }

    public Dictionary<string, string> ToStoredValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SettingsKeys.Refresh] = RefreshIntervalMs.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.Compact] = Flag(Compact),
            [SettingsKeys.DockColorIcons] = Flag(ColorDockIcons),
            [SettingsKeys.BlueBelow] = BlueBelow.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.YellowFrom] = YellowFrom.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.OrangeFrom] = OrangeFrom.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.RedFrom] = RedFrom.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.Blue] = Blue.Color.ToHex(),
            [SettingsKeys.Yellow] = Yellow.Color.ToHex(),
            [SettingsKeys.Orange] = Orange.Color.ToHex(),
            [SettingsKeys.Red] = Red.Color.ToHex(),
            [SettingsKeys.CoolMax] = CoolMaximum.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.CoolBoost] = Flag(LimitBoost),
            [SettingsKeys.BoostMode] = BoostValue == PowerGuids.BoostEfficient ? "3" : "0",
            [SettingsKeys.CoolSelection] = CoolSelectionCycle.Token(Selection),
            [SettingsKeys.Auto] = Flag(Selection == CoolSelection.Auto),
            [SettingsKeys.AutoOn] = AutoOnCelsius.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.AutoOnSeconds] = AutoOnSeconds.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.AutoOff] = AutoOffCelsius.ToString(CultureInfo.InvariantCulture),
            [SettingsKeys.AutoOffSeconds] = AutoOffSeconds.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var device in Devices)
        {
            values[SettingsKeys.Device(device.Id, "short")] = device.ShortLabel;
            values[SettingsKeys.Device(device.Id, "full")] = device.FullName;
            values[SettingsKeys.Device(device.Id, "list")] = PresentationMap.VisibilityToken(device.List);
            values[SettingsKeys.Device(device.Id, "dock")] = PresentationMap.VisibilityToken(device.Dock);
        }

        return values;
    }

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string Flag(bool value) => value ? "true" : "false";
}

public static class SettingsPayload
{
    public static Dictionary<string, string?> Read(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj)
            {
                return result;
            }

            foreach (var pair in obj)
            {
                result[pair.Key] = ToText(pair.Value);
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private static string? ToText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonValue value)
        {
            return node.ToJsonString();
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag ? "true" : "false";
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetValue<double>(out var real) && !double.IsNaN(real) && !double.IsInfinity(real))
        {
            return Math.Round(real).ToString(CultureInfo.InvariantCulture);
        }

        return node.ToJsonString();
    }
}

public sealed record SettingsApplyResult(SettingsFormState State, IReadOnlyList<string> Notices);

public static class SettingsFormStateFactory
{
    public static SettingsApplyResult Apply(IReadOnlyDictionary<string, string?> submitted, SettingsFormState current)
    {
        var notices = new List<string>();
        NoteNumber(notices, submitted, SettingsKeys.Refresh, UiText.SettingRefreshShort, 500, 3000);
        NoteNumber(notices, submitted, SettingsKeys.CoolMax, UiText.SettingCoolMaxShort, 1, 100);
        NoteInvalidHex(notices, submitted, SettingsKeys.Blue, UiText.TagBlue);
        NoteInvalidHex(notices, submitted, SettingsKeys.Yellow, UiText.TagYellow);
        NoteInvalidHex(notices, submitted, SettingsKeys.Orange, UiText.TagOrange);
        NoteInvalidHex(notices, submitted, SettingsKeys.Red, UiText.TagRed);
        var state = FromSubmission(submitted, current);
        NoteBands(notices, submitted, state);
        if (SubmittedInt(submitted, SettingsKeys.AutoOff) is int off && off != state.AutoOffCelsius)
        {
            notices.Add(UiText.SettingsAutoOffLowered(state.AutoOffCelsius));
        }

        return new SettingsApplyResult(state, notices);
    }

    public static SettingsFormState FromStored(
        IReadOnlyDictionary<string, string?> stored,
        IReadOnlyList<DeviceSettingSeed> devices)
    {
        var blue = ColorPresets.FromStored(Read(stored, SettingsKeys.Blue), RgbColor.Blue);
        var yellow = ColorPresets.FromStored(Read(stored, SettingsKeys.Yellow), RgbColor.Yellow);
        var orange = ColorPresets.FromStored(Read(stored, SettingsKeys.Orange), RgbColor.Orange);
        var red = ColorPresets.FromStored(Read(stored, SettingsKeys.Red), RgbColor.Red);
        var colors = ColorScale.Normalize(
            ReadInt(stored, SettingsKeys.BlueBelow, 60),
            ReadInt(stored, SettingsKeys.YellowFrom, 60),
            ReadInt(stored, SettingsKeys.OrangeFrom, 75),
            ReadInt(stored, SettingsKeys.RedFrom, 90),
            blue.Color,
            yellow.Color,
            orange.Color,
            red.Color);
        var cool = CoolOptions.Normalize(
            ReadInt(stored, SettingsKeys.CoolMax, 99),
            ReadBool(stored, SettingsKeys.CoolBoost, true),
            Read(stored, SettingsKeys.BoostMode) == "3" ? PowerGuids.BoostEfficient : PowerGuids.BoostDisabled);
        var selection = CoolSelectionCycle.Parse(
            Read(stored, SettingsKeys.CoolSelection),
            stored.ContainsKey(SettingsKeys.Auto) ? ReadBool(stored, SettingsKeys.Auto, false) : null);
        var auto = AutoCoolOptions.Normalize(
            selection == CoolSelection.Auto,
            ReadInt(stored, SettingsKeys.AutoOn, 85),
            ReadInt(stored, SettingsKeys.AutoOnSeconds, 10),
            ReadInt(stored, SettingsKeys.AutoOff, 70),
            ReadInt(stored, SettingsKeys.AutoOffSeconds, 10));
        return new SettingsFormState(
            MonitorOptions.ClampRefresh(ReadInt(stored, SettingsKeys.Refresh, MonitorOptions.DefaultRefreshMs)),
            ReadBool(stored, SettingsKeys.Compact, false),
            ReadBool(stored, SettingsKeys.DockColorIcons, true),
            colors.BlueBelow,
            colors.YellowFrom,
            colors.OrangeFrom,
            colors.RedFrom,
            ColorPresets.FromStored(colors.Blue.ToHex(), RgbColor.Blue),
            ColorPresets.FromStored(colors.Yellow.ToHex(), RgbColor.Yellow),
            ColorPresets.FromStored(colors.Orange.ToHex(), RgbColor.Orange),
            ColorPresets.FromStored(colors.Red.ToHex(), RgbColor.Red),
            cool.MaximumProcessorState,
            cool.LimitBoost,
            cool.BoostValue,
            selection,
            selection == CoolSelection.Auto,
            auto.ActivateCelsius,
            auto.ActivateSeconds,
            auto.DeactivateCelsius,
            auto.DeactivateSeconds,
            devices.Select(CleanSeed).ToArray());
    }

    public static SettingsFormState FromSubmission(
        IReadOnlyDictionary<string, string?> submitted,
        SettingsFormState current)
    {
        var blue = ReadColor(submitted, SettingsKeys.Blue, current.Blue);
        var yellow = ReadColor(submitted, SettingsKeys.Yellow, current.Yellow);
        var orange = ReadColor(submitted, SettingsKeys.Orange, current.Orange);
        var red = ReadColor(submitted, SettingsKeys.Red, current.Red);
        var colors = ColorScale.Normalize(
            ReadInt(submitted, SettingsKeys.BlueBelow, current.BlueBelow),
            ReadInt(submitted, SettingsKeys.YellowFrom, current.YellowFrom),
            ReadInt(submitted, SettingsKeys.OrangeFrom, current.OrangeFrom),
            ReadInt(submitted, SettingsKeys.RedFrom, current.RedFrom),
            blue.Color,
            yellow.Color,
            orange.Color,
            red.Color);
        var boostText = Read(submitted, SettingsKeys.BoostMode);
        var boost = boostText is null
            ? current.BoostValue
            : boostText == "3" ? PowerGuids.BoostEfficient : PowerGuids.BoostDisabled;
        var cool = CoolOptions.Normalize(
            ReadInt(submitted, SettingsKeys.CoolMax, current.CoolMaximum),
            ReadBool(submitted, SettingsKeys.CoolBoost, current.LimitBoost),
            boost);
        var selectionText = Read(submitted, SettingsKeys.CoolSelection);
        var selection = string.IsNullOrWhiteSpace(selectionText)
            ? current.Selection
            : CoolSelectionCycle.Parse(selectionText, null);
        var auto = AutoCoolOptions.Normalize(
            selection == CoolSelection.Auto,
            ReadInt(submitted, SettingsKeys.AutoOn, current.AutoOnCelsius),
            ReadInt(submitted, SettingsKeys.AutoOnSeconds, current.AutoOnSeconds),
            ReadInt(submitted, SettingsKeys.AutoOff, current.AutoOffCelsius),
            ReadInt(submitted, SettingsKeys.AutoOffSeconds, current.AutoOffSeconds));
        return new SettingsFormState(
            MonitorOptions.ClampRefresh(ReadInt(submitted, SettingsKeys.Refresh, current.RefreshIntervalMs)),
            ReadBool(submitted, SettingsKeys.Compact, current.Compact),
            ReadBool(submitted, SettingsKeys.DockColorIcons, current.ColorDockIcons),
            colors.BlueBelow,
            colors.YellowFrom,
            colors.OrangeFrom,
            colors.RedFrom,
            ColorPresets.FromStored(colors.Blue.ToHex(), RgbColor.Blue),
            ColorPresets.FromStored(colors.Yellow.ToHex(), RgbColor.Yellow),
            ColorPresets.FromStored(colors.Orange.ToHex(), RgbColor.Orange),
            ColorPresets.FromStored(colors.Red.ToHex(), RgbColor.Red),
            cool.MaximumProcessorState,
            cool.LimitBoost,
            cool.BoostValue,
            selection,
            selection == CoolSelection.Auto,
            auto.ActivateCelsius,
            auto.ActivateSeconds,
            auto.DeactivateCelsius,
            auto.DeactivateSeconds,
            current.Devices.Select(device => CleanSubmission(submitted, device)).ToArray());
    }

    private static ColorChoice ReadColor(IReadOnlyDictionary<string, string?> submitted, string colorKey, ColorChoice current)
    {
        if (submitted.ContainsKey(SettingsKeys.Preset(colorKey)))
        {
            var preset = Read(submitted, SettingsKeys.Preset(colorKey));
            if (!string.Equals(preset?.Trim(), ColorPresets.CustomId, StringComparison.OrdinalIgnoreCase))
            {
                var match = ColorPresets.FindId(preset);
                if (match is not null)
                {
                    return new ColorChoice(match.Id, match.Color.ToHex(), match.Color);
                }
            }
        }

        if (submitted.ContainsKey(SettingsKeys.Custom(colorKey))
            && RgbColor.TryParse(Read(submitted, SettingsKeys.Custom(colorKey)), out var parsed)
            && parsed != current.Color)
        {
            return new ColorChoice(ColorPresets.CustomId, parsed.ToHex(), parsed);
        }

        return current;
    }

    private static DeviceFormState CleanSeed(DeviceSettingSeed seed) =>
        Clean(new DeviceFormState(
            seed.Id,
            seed.DefaultShort,
            seed.DefaultFull,
            seed.ShortLabel ?? string.Empty,
            seed.FullName ?? string.Empty,
            seed.List,
            seed.Dock));

    private static DeviceFormState CleanSubmission(IReadOnlyDictionary<string, string?> submitted, DeviceFormState device)
    {
        var listKey = SettingsKeys.Device(device.Id, "list");
        var dockKey = SettingsKeys.Device(device.Id, "dock");
        return Clean(device with
        {
            ShortLabel = submitted.TryGetValue(SettingsKeys.Device(device.Id, "short"), out var shortLabel) ? shortLabel ?? string.Empty : device.ShortLabel,
            FullName = submitted.TryGetValue(SettingsKeys.Device(device.Id, "full"), out var fullName) ? fullName ?? string.Empty : device.FullName,
            List = submitted.ContainsKey(listKey) ? PresentationMap.ParseVisibility(Read(submitted, listKey)) : device.List,
            Dock = submitted.ContainsKey(dockKey) ? PresentationMap.ParseVisibility(Read(submitted, dockKey)) : device.Dock,
        });
    }

    private static DeviceFormState Clean(DeviceFormState device)
    {
        var map = PresentationMap.FromEntries(
        [
            new DevicePresentationEntry(
                device.Id,
                device.DefaultShort,
                device.DefaultFull,
                Blank(device.ShortLabel),
                Blank(device.FullName),
                device.List,
                device.Dock),
        ]);
        if (map.Entries.Count == 0)
        {
            return device with { ShortLabel = string.Empty, FullName = string.Empty };
        }

        var entry = map.Entries[0];
        return device with
        {
            DefaultShort = entry.DefaultShort,
            DefaultFull = entry.DefaultFull,
            ShortLabel = entry.ShortLabel ?? string.Empty,
            FullName = entry.FullName ?? string.Empty,
            List = entry.List,
            Dock = entry.Dock,
        };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static void NoteNumber(List<string> notices, IReadOnlyDictionary<string, string?> submitted, string key, string label, int min, int max)
    {
        if (!submitted.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            notices.Add(UiText.SettingsNumberIgnored(label));
            return;
        }

        if (parsed < min || parsed > max)
        {
            notices.Add(UiText.SettingsNumberLimited(label, Math.Clamp(parsed, min, max), min, max));
        }
    }

    private static void NoteInvalidHex(List<string> notices, IReadOnlyDictionary<string, string?> submitted, string colorKey, string band)
    {
        var key = SettingsKeys.Custom(colorKey);
        if (!submitted.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text) || RgbColor.TryParse(text, out _))
        {
            return;
        }

        notices.Add(UiText.SettingsHexIgnored(band));
    }

    private static void NoteBands(List<string> notices, IReadOnlyDictionary<string, string?> submitted, SettingsFormState state)
    {
        var cool = SubmittedInt(submitted, SettingsKeys.BlueBelow);
        var warm = SubmittedInt(submitted, SettingsKeys.YellowFrom);
        var hot = SubmittedInt(submitted, SettingsKeys.OrangeFrom);
        var critical = SubmittedInt(submitted, SettingsKeys.RedFrom);
        if (cool is null && warm is null && hot is null && critical is null)
        {
            return;
        }

        var changed = (cool is int coolValue && coolValue != state.BlueBelow)
            || (warm is int warmValue && warmValue != state.YellowFrom)
            || (hot is int hotValue && hotValue != state.OrangeFrom)
            || (critical is int criticalValue && criticalValue != state.RedFrom);
        if (changed)
        {
            notices.Add(UiText.SettingsBandsReordered(state.BlueBelow, state.YellowFrom, state.OrangeFrom, state.RedFrom));
        }
    }

    private static int? SubmittedInt(IReadOnlyDictionary<string, string?> submitted, string key)
    {
        if (!submitted.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var text) ? text : null;

    private static int ReadInt(IReadOnlyDictionary<string, string?> values, string key, int fallback)
    {
        var text = Read(values, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return MonitorOptions.ParseInt(text.Trim(), fallback);
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string?> values, string key, bool fallback)
    {
        var text = Read(values, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback,
        };
    }
}
