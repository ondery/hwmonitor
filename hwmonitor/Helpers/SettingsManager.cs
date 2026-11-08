// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private const string Namespace = "hwmonitor";

    private readonly object _gate = new();
    private readonly TextSetting _refresh = Text("refreshIntervalMs", UiText.SettingRefreshLabel, UiText.SettingRefreshDescription, "1000");
    private readonly ToggleSetting _compact = Toggle("compactMode", UiText.SettingCompactLabel, UiText.SettingCompactDescription, false);
    private readonly TextSetting _blueBelow = Text("color.blueBelow", UiText.SettingBlueBelowLabel, UiText.SettingThresholdDescription, "60");
    private readonly TextSetting _yellowFrom = Text("color.yellowFrom", UiText.SettingYellowLabel, UiText.SettingThresholdDescription, "60");
    private readonly TextSetting _orangeFrom = Text("color.orangeFrom", UiText.SettingOrangeLabel, UiText.SettingThresholdDescription, "75");
    private readonly TextSetting _redFrom = Text("color.redFrom", UiText.SettingRedLabel, UiText.SettingThresholdDescription, "90");
    private readonly TextSetting _blue = Text("color.blue", UiText.SettingBlueColorLabel, UiText.SettingColorDescription, "#3B82F6");
    private readonly TextSetting _yellow = Text("color.yellow", UiText.SettingYellowColorLabel, UiText.SettingColorDescription, "#EAB308");
    private readonly TextSetting _orange = Text("color.orange", UiText.SettingOrangeColorLabel, UiText.SettingColorDescription, "#F97316");
    private readonly TextSetting _red = Text("color.red", UiText.SettingRedColorLabel, UiText.SettingColorDescription, "#EF4444");
    private readonly TextSetting _coolMax = Text("cool.maxProcessorState", UiText.SettingCoolMaxLabel, UiText.SettingCoolMaxDescription, "99");
    private readonly ToggleSetting _limitBoost = Toggle("cool.limitBoost", UiText.SettingCoolBoostLabel, UiText.SettingCoolBoostDescription, true);
    private readonly ChoiceSetSetting _boostMode;
    private readonly ToggleSetting _auto = Toggle("autoCool.enabled", UiText.SettingAutoLabel, UiText.SettingAutoDescription, false);
    private readonly TextSetting _autoOn = Text("autoCool.activateCelsius", UiText.SettingAutoOnLabel, UiText.SettingAutoDescription, "85");
    private readonly TextSetting _autoOnSeconds = Text("autoCool.activateSeconds", UiText.SettingAutoOnSecondsLabel, UiText.SettingAutoDescription, "10");
    private readonly TextSetting _autoOff = Text("autoCool.deactivateCelsius", UiText.SettingAutoOffLabel, UiText.SettingAutoDescription, "70");
    private readonly TextSetting _autoOffSeconds = Text("autoCool.deactivateSeconds", UiText.SettingAutoOffSecondsLabel, UiText.SettingAutoDescription, "10");
    private readonly Dictionary<string, DeviceRowSettings> _devices = new(StringComparer.Ordinal);
    private readonly string _sidecarPath;
    private bool _suppress;
    private MonitorOptions _current = MonitorOptions.CreateDefault();

    public SettingsManager()
    {
        _boostMode = new ChoiceSetSetting(
            Key("cool.boostMode"),
            UiText.SettingBoostChoiceLabel,
            UiText.SettingBoostChoiceDescription,
            [
                new ChoiceSetSetting.Choice(UiText.SettingBoostDisabled, "0"),
                new ChoiceSetSetting.Choice(UiText.SettingBoostEfficient, "3"),
            ]);

        FilePath = SettingsJsonPath();
        _sidecarPath = Path.Combine(SettingsDirectory, "device-presentation.json");
        foreach (var setting in TextSettings())
        {
            Settings.Add(setting);
        }

        Settings.Add(_compact);
        Settings.Add(_limitBoost);
        Settings.Add(_boostMode);
        Settings.Add(_auto);
        foreach (var entry in PresentationMap.Deserialize(ReadSidecar()).Entries)
        {
            AddDevice(entry.Id, entry.DefaultShort, entry.DefaultFull, entry);
        }

        LoadSettings();
        ApplyParsed();
        Settings.SettingsChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            lock (_gate)
            {
                SaveSettings();
                ApplyParsed();
                WriteSidecar();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? Changed;

    public MonitorOptions Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public string SettingsDirectory => Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();

    public void EnsureDevices(IReadOnlyList<SensorReading> readings)
    {
        var added = false;
        lock (_gate)
        {
            foreach (var reading in readings)
            {
                if (!_devices.TryGetValue(reading.Id, out var row))
                {
                    AddDevice(reading.Id, reading.ShortLabel, reading.HardwareName, null);
                    added = true;
                    continue;
                }

                if (!string.Equals(row.DefaultShort, reading.ShortLabel, StringComparison.Ordinal)
                    || !string.Equals(row.DefaultFull, reading.HardwareName, StringComparison.Ordinal))
                {
                    row.DefaultShort = reading.ShortLabel;
                    row.DefaultFull = reading.HardwareName;
                    ApplyDeviceLabels(row);
                    added = true;
                }
            }

            if (!added)
            {
                return;
            }

            ApplyParsed();
            WriteSidecar();
        }
    }

    private void ApplyParsed()
    {
        var entries = new List<DevicePresentationEntry>();
        foreach (var row in _devices.Values)
        {
            entries.Add(new DevicePresentationEntry(
                row.Id,
                row.DefaultShort,
                row.DefaultFull,
                Blank(row.Short.Value),
                Blank(row.Full.Value),
                PresentationMap.ParseVisibility(row.List.Value),
                PresentationMap.ParseVisibility(row.Dock.Value)));
        }

        var colors = ColorScale.Normalize(
            MonitorOptions.ParseInt(_blueBelow.Value, 60),
            MonitorOptions.ParseInt(_yellowFrom.Value, 60),
            MonitorOptions.ParseInt(_orangeFrom.Value, 75),
            MonitorOptions.ParseInt(_redFrom.Value, 90),
            ParseColor(_blue.Value),
            ParseColor(_yellow.Value),
            ParseColor(_orange.Value),
            ParseColor(_red.Value));
        var boost = _boostMode.Value == "3" ? PowerGuids.BoostEfficient : PowerGuids.BoostDisabled;
        var cool = CoolOptions.Normalize(MonitorOptions.ParseInt(_coolMax.Value, 99), _limitBoost.Value == true, boost);
        var auto = AutoCoolOptions.Normalize(
            _auto.Value == true,
            MonitorOptions.ParseInt(_autoOn.Value, 85),
            MonitorOptions.ParseInt(_autoOnSeconds.Value, 10),
            MonitorOptions.ParseInt(_autoOff.Value, 70),
            MonitorOptions.ParseInt(_autoOffSeconds.Value, 10));
        var refresh = MonitorOptions.ClampRefresh(MonitorOptions.ParseInt(_refresh.Value, MonitorOptions.DefaultRefreshMs));
        _current = new MonitorOptions(refresh, _compact.Value == true, colors, cool, auto, PresentationMap.FromEntries(entries));

        _suppress = true;
        _refresh.Value = refresh.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _blueBelow.Value = colors.BlueBelow.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _yellowFrom.Value = colors.YellowFrom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _orangeFrom.Value = colors.OrangeFrom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _redFrom.Value = colors.RedFrom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _blue.Value = colors.Blue.ToHex();
        _yellow.Value = colors.Yellow.ToHex();
        _orange.Value = colors.Orange.ToHex();
        _red.Value = colors.Red.ToHex();
        _coolMax.Value = cool.MaximumProcessorState.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _autoOn.Value = auto.ActivateCelsius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _autoOnSeconds.Value = auto.ActivateSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _autoOff.Value = auto.DeactivateCelsius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _autoOffSeconds.Value = auto.DeactivateSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _suppress = false;
    }

    private void AddDevice(string id, string defaultShort, string defaultFull, DevicePresentationEntry? saved)
    {
        var row = new DeviceRowSettings(
            id,
            defaultShort,
            defaultFull,
            Text(DeviceKey(id, "short"), UiText.DeviceShortLabel(defaultShort), UiText.DeviceShortDescription(id.Replace("{", "(").Replace("}", ")")), saved?.ShortLabel ?? string.Empty),
            Text(DeviceKey(id, "full"), UiText.DeviceFullLabel(defaultShort), UiText.DeviceFullDescription, saved?.FullName ?? string.Empty),
            Choice(DeviceKey(id, "list"), UiText.DeviceListLabel(defaultShort), saved?.List),
            Choice(DeviceKey(id, "dock"), UiText.DeviceDockLabel(defaultShort), saved?.Dock));
        _devices.Add(id, row);
        Settings.Add(row.Short);
        Settings.Add(row.Full);
        Settings.Add(row.List);
        Settings.Add(row.Dock);
    }

    private static void ApplyDeviceLabels(DeviceRowSettings row)
    {
        row.Short.Label = UiText.DeviceShortLabel(row.DefaultShort);
        row.Short.Description = UiText.DeviceShortDescription(row.Id);
        row.Full.Label = UiText.DeviceFullLabel(row.DefaultShort);
        row.List.Label = UiText.DeviceListLabel(row.DefaultShort);
        row.Dock.Label = UiText.DeviceDockLabel(row.DefaultShort);
    }

    private void WriteSidecar()
    {
        try
        {
            var json = _current.Devices.Serialize();
            var temporary = _sidecarPath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _sidecarPath, overwrite: true);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }
    }

    private string? ReadSidecar()
    {
        try
        {
            return File.Exists(_sidecarPath) ? File.ReadAllText(_sidecarPath) : null;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return null;
        }
    }

    private IEnumerable<Setting<string>> TextSettings()
    {
        yield return _refresh;
        yield return _blueBelow;
        yield return _yellowFrom;
        yield return _orangeFrom;
        yield return _redFrom;
        yield return _blue;
        yield return _yellow;
        yield return _orange;
        yield return _red;
        yield return _coolMax;
        yield return _autoOn;
        yield return _autoOnSeconds;
        yield return _autoOff;
        yield return _autoOffSeconds;
    }

    private static ChoiceSetSetting Choice(string name, string label, VisibilityChoice? selected)
    {
        var setting = new ChoiceSetSetting(
            Key(name),
            label,
            UiText.SettingDeviceVisibilityDescription,
            [
                new ChoiceSetSetting.Choice(UiText.VisibilityDefault, "default"),
                new ChoiceSetSetting.Choice(UiText.VisibilityShow, "show"),
                new ChoiceSetSetting.Choice(UiText.VisibilityHide, "hide"),
            ]);
        setting.Value = PresentationMap.VisibilityToken(selected ?? VisibilityChoice.Default);
        return setting;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string DeviceKey(string id, string part)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return $"device.{hash[..20]}.{part}";
    }

    private static RgbColor? ParseColor(string? text) =>
        RgbColor.TryParse(text, out var color) ? color : null;

    private static string Key(string name) => $"{Namespace}.{name}";

    private static TextSetting Text(string name, string label, string description, string value) =>
        new(Key(name), label, description, value);

    private static ToggleSetting Toggle(string name, string label, string description, bool value) =>
        new(Key(name), label, description, value);

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("HWMonitor");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    private sealed class DeviceRowSettings
    {
        public DeviceRowSettings(string id, string defaultShort, string defaultFull, TextSetting shortLabel, TextSetting fullName, ChoiceSetSetting list, ChoiceSetSetting dock)
        {
            Id = id;
            DefaultShort = defaultShort;
            DefaultFull = defaultFull;
            Short = shortLabel;
            Full = fullName;
            List = list;
            Dock = dock;
        }

        public string Id { get; }

        public string DefaultShort { get; set; }

        public string DefaultFull { get; set; }

        public TextSetting Short { get; }

        public TextSetting Full { get; }

        public ChoiceSetSetting List { get; }

        public ChoiceSetSetting Dock { get; }
    }
}
