// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private readonly object _gate = new();
    private readonly TextSetting _refresh = Text(SettingsKeys.Refresh, UiText.SettingRefreshLabel, UiText.SettingRefreshDescription, "1000");
    private readonly ToggleSetting _compact = Toggle(SettingsKeys.Compact, UiText.SettingCompactLabel, UiText.SettingCompactDescription, false);
    private readonly ToggleSetting _dockColors = Toggle(SettingsKeys.DockColorIcons, UiText.SettingDockColorLabel, UiText.SettingDockColorDescription, true);
    private readonly TextSetting _blueBelow = Text(SettingsKeys.BlueBelow, UiText.SettingBlueBelowLabel, UiText.SettingThresholdDescription, "60");
    private readonly TextSetting _yellowFrom = Text(SettingsKeys.YellowFrom, UiText.SettingYellowLabel, UiText.SettingThresholdDescription, "60");
    private readonly TextSetting _orangeFrom = Text(SettingsKeys.OrangeFrom, UiText.SettingOrangeLabel, UiText.SettingThresholdDescription, "75");
    private readonly TextSetting _redFrom = Text(SettingsKeys.RedFrom, UiText.SettingRedLabel, UiText.SettingThresholdDescription, "90");
    private readonly TextSetting _blue = Text(SettingsKeys.Blue, UiText.SettingBlueColorLabel, UiText.SettingColorDescription, RgbColor.Blue.ToHex());
    private readonly TextSetting _yellow = Text(SettingsKeys.Yellow, UiText.SettingYellowColorLabel, UiText.SettingColorDescription, RgbColor.Yellow.ToHex());
    private readonly TextSetting _orange = Text(SettingsKeys.Orange, UiText.SettingOrangeColorLabel, UiText.SettingColorDescription, RgbColor.Orange.ToHex());
    private readonly TextSetting _red = Text(SettingsKeys.Red, UiText.SettingRedColorLabel, UiText.SettingColorDescription, RgbColor.Red.ToHex());
    private readonly TextSetting _coolMax = Text(SettingsKeys.CoolMax, UiText.SettingCoolMaxLabel, UiText.SettingCoolMaxDescription, "99");
    private readonly ToggleSetting _limitBoost = Toggle(SettingsKeys.CoolBoost, UiText.SettingCoolBoostLabel, UiText.SettingCoolBoostDescription, true);
    private readonly ChoiceSetSetting _boostMode;
    private readonly ToggleSetting _auto = Toggle(SettingsKeys.Auto, UiText.SettingAutoLabel, UiText.SettingAutoDescription, false);
    private readonly TextSetting _autoOn = Text(SettingsKeys.AutoOn, UiText.SettingAutoOnLabel, UiText.SettingAutoOnDescription, "85");
    private readonly TextSetting _autoOnSeconds = Text(SettingsKeys.AutoOnSeconds, UiText.SettingAutoOnSecondsLabel, UiText.SettingAutoOnSecondsDescription, "10");
    private readonly TextSetting _autoOff = Text(SettingsKeys.AutoOff, UiText.SettingAutoOffLabel, UiText.SettingAutoOffDescription, "70");
    private readonly TextSetting _autoOffSeconds = Text(SettingsKeys.AutoOffSeconds, UiText.SettingAutoOffSecondsLabel, UiText.SettingAutoOffSecondsDescription, "10");
    private readonly Dictionary<string, DeviceRowSettings> _devices = new(StringComparer.Ordinal);
    private readonly string _sidecarPath;
    private bool _suppress;
    private MonitorOptions _current = MonitorOptions.CreateDefault();

    public SettingsManager()
    {
        _boostMode = new ChoiceSetSetting(
            SettingsKeys.BoostMode,
            UiText.SettingBoostChoiceLabel,
            UiText.SettingBoostChoiceDescription,
            [
                new ChoiceSetSetting.Choice(UiText.SettingBoostDisabled, "0"),
                new ChoiceSetSetting.Choice(UiText.SettingBoostEfficient, "3"),
            ]);

        FilePath = SettingsJsonPath();
        _sidecarPath = Path.Combine(SettingsDirectory, "device-presentation.json");
        foreach (var setting in ScalarSettings())
        {
            Settings.Add(setting);
        }

        Settings.Add(_compact);
        Settings.Add(_dockColors);
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

    public event EventHandler? DevicesChanged;

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

    public SettingsFormState FormState
    {
        get
        {
            lock (_gate)
            {
                return Capture();
            }
        }
    }

    public string SettingsDirectory => Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();

    public void ApplySubmission(IReadOnlyDictionary<string, string?> submitted)
    {
        lock (_gate)
        {
            var next = SettingsFormStateFactory.FromSubmission(submitted, Capture());
            Write(next);
            ApplyParsed();
            SaveSettings();
            WriteSidecar();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

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

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private SettingsFormState Capture() =>
        SettingsFormStateFactory.FromStored(StoredValues(), DeviceSeeds());

    private void Write(SettingsFormState state)
    {
        var stored = state.ToStoredValues();
        _suppress = true;
        _refresh.Value = stored[SettingsKeys.Refresh];
        _compact.Value = state.Compact;
        _dockColors.Value = state.ColorDockIcons;
        _blueBelow.Value = stored[SettingsKeys.BlueBelow];
        _yellowFrom.Value = stored[SettingsKeys.YellowFrom];
        _orangeFrom.Value = stored[SettingsKeys.OrangeFrom];
        _redFrom.Value = stored[SettingsKeys.RedFrom];
        _blue.Value = stored[SettingsKeys.Blue];
        _yellow.Value = stored[SettingsKeys.Yellow];
        _orange.Value = stored[SettingsKeys.Orange];
        _red.Value = stored[SettingsKeys.Red];
        _coolMax.Value = stored[SettingsKeys.CoolMax];
        _limitBoost.Value = state.LimitBoost;
        _boostMode.Value = stored[SettingsKeys.BoostMode];
        _auto.Value = state.AutoEnabled;
        _autoOn.Value = stored[SettingsKeys.AutoOn];
        _autoOnSeconds.Value = stored[SettingsKeys.AutoOnSeconds];
        _autoOff.Value = stored[SettingsKeys.AutoOff];
        _autoOffSeconds.Value = stored[SettingsKeys.AutoOffSeconds];
        foreach (var row in _devices.Values)
        {
            row.Short.Value = stored[SettingsKeys.Device(row.Id, "short")];
            row.Full.Value = stored[SettingsKeys.Device(row.Id, "full")];
            row.List.Value = stored[SettingsKeys.Device(row.Id, "list")];
            row.Dock.Value = stored[SettingsKeys.Device(row.Id, "dock")];
        }

        _suppress = false;
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

        var blue = ColorPresets.FromStored(_blue.Value, RgbColor.Blue);
        var yellow = ColorPresets.FromStored(_yellow.Value, RgbColor.Yellow);
        var orange = ColorPresets.FromStored(_orange.Value, RgbColor.Orange);
        var red = ColorPresets.FromStored(_red.Value, RgbColor.Red);
        var colors = ColorScale.Normalize(
            MonitorOptions.ParseInt(_blueBelow.Value, 60),
            MonitorOptions.ParseInt(_yellowFrom.Value, 60),
            MonitorOptions.ParseInt(_orangeFrom.Value, 75),
            MonitorOptions.ParseInt(_redFrom.Value, 90),
            blue.Color,
            yellow.Color,
            orange.Color,
            red.Color);
        var boost = _boostMode.Value == "3" ? PowerGuids.BoostEfficient : PowerGuids.BoostDisabled;
        var cool = CoolOptions.Normalize(MonitorOptions.ParseInt(_coolMax.Value, 99), _limitBoost.Value == true, boost);
        var auto = AutoCoolOptions.Normalize(
            _auto.Value == true,
            MonitorOptions.ParseInt(_autoOn.Value, 85),
            MonitorOptions.ParseInt(_autoOnSeconds.Value, 10),
            MonitorOptions.ParseInt(_autoOff.Value, 70),
            MonitorOptions.ParseInt(_autoOffSeconds.Value, 10));
        var refresh = MonitorOptions.ClampRefresh(MonitorOptions.ParseInt(_refresh.Value, MonitorOptions.DefaultRefreshMs));
        _current = new MonitorOptions(refresh, _compact.Value == true, _dockColors.Value == true, colors, cool, auto, PresentationMap.FromEntries(entries));

        _suppress = true;
        _refresh.Value = refresh.ToString(CultureInfo.InvariantCulture);
        _blueBelow.Value = colors.BlueBelow.ToString(CultureInfo.InvariantCulture);
        _yellowFrom.Value = colors.YellowFrom.ToString(CultureInfo.InvariantCulture);
        _orangeFrom.Value = colors.OrangeFrom.ToString(CultureInfo.InvariantCulture);
        _redFrom.Value = colors.RedFrom.ToString(CultureInfo.InvariantCulture);
        _blue.Value = colors.Blue.ToHex();
        _yellow.Value = colors.Yellow.ToHex();
        _orange.Value = colors.Orange.ToHex();
        _red.Value = colors.Red.ToHex();
        _coolMax.Value = cool.MaximumProcessorState.ToString(CultureInfo.InvariantCulture);
        _autoOn.Value = auto.ActivateCelsius.ToString(CultureInfo.InvariantCulture);
        _autoOnSeconds.Value = auto.ActivateSeconds.ToString(CultureInfo.InvariantCulture);
        _autoOff.Value = auto.DeactivateCelsius.ToString(CultureInfo.InvariantCulture);
        _autoOffSeconds.Value = auto.DeactivateSeconds.ToString(CultureInfo.InvariantCulture);
        _suppress = false;
    }

    private Dictionary<string, string?> StoredValues() => new(StringComparer.Ordinal)
    {
        [SettingsKeys.Refresh] = _refresh.Value,
        [SettingsKeys.Compact] = _compact.Value == true ? "true" : "false",
        [SettingsKeys.DockColorIcons] = _dockColors.Value == true ? "true" : "false",
        [SettingsKeys.BlueBelow] = _blueBelow.Value,
        [SettingsKeys.YellowFrom] = _yellowFrom.Value,
        [SettingsKeys.OrangeFrom] = _orangeFrom.Value,
        [SettingsKeys.RedFrom] = _redFrom.Value,
        [SettingsKeys.Blue] = _blue.Value,
        [SettingsKeys.Yellow] = _yellow.Value,
        [SettingsKeys.Orange] = _orange.Value,
        [SettingsKeys.Red] = _red.Value,
        [SettingsKeys.CoolMax] = _coolMax.Value,
        [SettingsKeys.CoolBoost] = _limitBoost.Value == true ? "true" : "false",
        [SettingsKeys.BoostMode] = _boostMode.Value,
        [SettingsKeys.Auto] = _auto.Value == true ? "true" : "false",
        [SettingsKeys.AutoOn] = _autoOn.Value,
        [SettingsKeys.AutoOnSeconds] = _autoOnSeconds.Value,
        [SettingsKeys.AutoOff] = _autoOff.Value,
        [SettingsKeys.AutoOffSeconds] = _autoOffSeconds.Value,
    };

    private List<DeviceSettingSeed> DeviceSeeds()
    {
        var seeds = new List<DeviceSettingSeed>();
        foreach (var row in _devices.Values)
        {
            seeds.Add(new DeviceSettingSeed(
                row.Id,
                row.DefaultShort,
                row.DefaultFull,
                Blank(row.Short.Value),
                Blank(row.Full.Value),
                PresentationMap.ParseVisibility(row.List.Value),
                PresentationMap.ParseVisibility(row.Dock.Value)));
        }

        return seeds;
    }

    private void AddDevice(string id, string defaultShort, string defaultFull, DevicePresentationEntry? saved)
    {
        var row = new DeviceRowSettings(
            id,
            defaultShort,
            defaultFull,
            Text(SettingsKeys.Device(id, "short"), UiText.DeviceShortLabel(defaultShort), UiText.DeviceShortDescription(id), saved?.ShortLabel ?? string.Empty),
            Text(SettingsKeys.Device(id, "full"), UiText.DeviceFullLabel(defaultShort), UiText.DeviceFullDescription, saved?.FullName ?? string.Empty),
            Choice(SettingsKeys.Device(id, "list"), UiText.DeviceListLabel(defaultShort), UiText.SettingDeviceListDescription, saved?.List),
            Choice(SettingsKeys.Device(id, "dock"), UiText.DeviceDockLabel(defaultShort), UiText.SettingDeviceDockDescription, saved?.Dock));
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

    private IEnumerable<Setting<string>> ScalarSettings()
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

    private static ChoiceSetSetting Choice(string key, string label, string description, VisibilityChoice? selected)
    {
        var setting = new ChoiceSetSetting(
            key,
            label,
            description,
            [
                new ChoiceSetSetting.Choice(UiText.VisibilityDefault, "default"),
                new ChoiceSetSetting.Choice(UiText.VisibilityShow, "show"),
                new ChoiceSetSetting.Choice(UiText.VisibilityHide, "hide"),
            ]);
        setting.Value = PresentationMap.VisibilityToken(selected ?? VisibilityChoice.Default);
        return setting;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static TextSetting Text(string key, string label, string description, string value) =>
        new(key, label, description, value);

    private static ToggleSetting Toggle(string key, string label, string description, bool value) =>
        new(key, label, description, value);

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
