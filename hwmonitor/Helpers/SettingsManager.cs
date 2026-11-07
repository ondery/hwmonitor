// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.IO;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Fans;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private const string Namespace = "hwmonitor";

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
    private readonly ToggleSetting _experimental = Toggle("fan.experimentalMsi", UiText.SettingExperimentalLabel, UiText.SettingExperimentalDescription, false);
    private readonly TextSetting[] _fanNames = new TextSetting[5];
    private readonly TextSetting[] _fanDuties = new TextSetting[5];
    private readonly ToggleSetting[] _fanEnabled = new ToggleSetting[5];
    private readonly FanModeSet _defaultFans = FanModeSet.Default;
    private FanModeSet _fans = FanModeSet.Default;
    private bool _suppress;

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

        for (var i = 0; i < 5; i++)
        {
            var template = _defaultFans.Modes[i];
            var number = i + 1;
            _fanNames[i] = Text($"fan.{number}.name", UiText.FanNameLabel(number), UiText.SettingFanDutyDescription, template.Name);
            _fanDuties[i] = Text($"fan.{number}.duty", UiText.FanDutyLabel(number), UiText.SettingFanDutyDescription, template.DutyPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _fanEnabled[i] = Toggle($"fan.{number}.enabled", UiText.FanEnabledLabel(number), UiText.FanTooFew, template.Enabled);
        }

        FilePath = SettingsJsonPath();
        foreach (var setting in TextSettings())
        {
            Settings.Add(setting);
        }

        Settings.Add(_compact);
        Settings.Add(_limitBoost);
        Settings.Add(_boostMode);
        Settings.Add(_auto);
        Settings.Add(_experimental);
        foreach (var setting in _fanEnabled)
        {
            Settings.Add(setting);
        }

        LoadSettings();
        ApplyParsed(writeBack: false);
        Settings.SettingsChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            SaveSettings();
            ApplyParsed(writeBack: true);
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? Changed;

    public MonitorOptions Current { get; private set; } = MonitorOptions.CreateDefault();

    public string? FanError { get; private set; }

    public string SettingsDirectory => Path.GetDirectoryName(FilePath) ?? Path.GetTempPath();

    private void ApplyParsed(bool writeBack)
    {
        var inputs = new List<(string? Name, string? Duty, bool Enabled)>();
        for (var i = 0; i < 5; i++)
        {
            inputs.Add((_fanNames[i].Value, _fanDuties[i].Value, _fanEnabled[i].Value == true));
        }

        if (!FanModeSet.TryCreate(inputs, out var fans, out var error))
        {
            FanError = error;
            if (writeBack)
            {
                WriteFans(_fans);
            }
        }
        else
        {
            _fans = fans;
            FanError = null;
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

        Current = new MonitorOptions(refresh, _compact.Value == true, colors, cool, auto, _fans, _experimental.Value == true);

        if (!writeBack)
        {
            return;
        }

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

    private void WriteFans(FanModeSet fans)
    {
        _suppress = true;
        for (var i = 0; i < 5; i++)
        {
            var mode = fans.Modes[i];
            _fanNames[i].Value = mode.Name;
            _fanDuties[i].Value = mode.DutyPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _fanEnabled[i].Value = mode.Enabled;
        }

        _suppress = false;
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
        foreach (var setting in _fanNames)
        {
            yield return setting;
        }

        foreach (var setting in _fanDuties)
        {
            yield return setting;
        }
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
}
