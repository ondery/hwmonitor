// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using Xunit;

namespace HWMonitor.Tests;

public class SettingsAndStatusTests
{
    [Fact]
    public void Saved_defaults_use_named_colors_and_dock_coloring()
    {
        var state = SettingsFormStateFactory.FromStored(new Dictionary<string, string?>(), []);
        Assert.Equal(1000, state.RefreshIntervalMs);
        Assert.False(state.Compact);
        Assert.True(state.ColorDockIcons);
        Assert.Equal("blue", state.Blue.PresetId);
        Assert.Equal("yellow", state.Yellow.PresetId);
        Assert.Equal("orange", state.Orange.PresetId);
        Assert.Equal("red", state.Red.PresetId);
        Assert.Equal(99, state.CoolMaximum);
        Assert.True(state.LimitBoost);
        Assert.Equal(PowerGuids.BoostDisabled, state.BoostValue);
        Assert.False(state.AutoEnabled);
    }

    [Fact]
    public void Stored_hex_migrates_to_the_nearest_preset_or_stays_custom()
    {
        var stored = new Dictionary<string, string?>
        {
            [SettingsKeys.Blue] = "#3b82f6",
            [SettingsKeys.Yellow] = "#EAB309",
            [SettingsKeys.Orange] = "#112233",
            [SettingsKeys.Red] = "EF4444",
        };
        var state = SettingsFormStateFactory.FromStored(stored, []);
        Assert.Equal("blue", state.Blue.PresetId);
        Assert.Equal("#3B82F6", state.Blue.Color.ToHex());
        Assert.Equal("yellow", state.Yellow.PresetId);
        Assert.Equal("#EAB308", state.Yellow.Color.ToHex());
        Assert.Equal(ColorPresets.CustomId, state.Orange.PresetId);
        Assert.Equal("#112233", state.Orange.Color.ToHex());
        Assert.Equal("red", state.Red.PresetId);
        Assert.Equal(RgbColor.Red, state.Red.Color);
        Assert.Equal("blue", ColorPresets.FromStored("not-a-color", RgbColor.Blue).PresetId);
    }

    [Fact]
    public void Submission_keeps_a_distant_custom_hex_and_clamps_numbers()
    {
        var current = SettingsFormStateFactory.FromStored(new Dictionary<string, string?>(), []);
        var submitted = new Dictionary<string, string?>
        {
            [SettingsKeys.Refresh] = "90",
            [SettingsKeys.Compact] = "true",
            [SettingsKeys.DockColorIcons] = "false",
            [SettingsKeys.BlueBelow] = "40",
            [SettingsKeys.YellowFrom] = "80",
            [SettingsKeys.OrangeFrom] = "70",
            [SettingsKeys.RedFrom] = "90",
            [SettingsKeys.Preset(SettingsKeys.Blue)] = ColorPresets.CustomId,
            [SettingsKeys.Custom(SettingsKeys.Blue)] = "#112233",
            [SettingsKeys.Preset(SettingsKeys.Orange)] = ColorPresets.CustomId,
            [SettingsKeys.Custom(SettingsKeys.Orange)] = "#F97317",
            [SettingsKeys.Preset(SettingsKeys.Yellow)] = "amber",
            [SettingsKeys.CoolMax] = "150",
            [SettingsKeys.BoostMode] = "3",
            [SettingsKeys.CoolSelection] = "auto",
            [SettingsKeys.AutoOn] = "70",
            [SettingsKeys.AutoOff] = "80",
        };
        var next = SettingsFormStateFactory.FromSubmission(submitted, current);
        Assert.Equal(500, next.RefreshIntervalMs);
        Assert.True(next.Compact);
        Assert.False(next.ColorDockIcons);
        Assert.Equal(80, next.YellowFrom);
        Assert.Equal(80, next.BlueBelow);
        Assert.Equal(81, next.OrangeFrom);
        Assert.Equal(90, next.RedFrom);
        Assert.Equal(ColorPresets.CustomId, next.Blue.PresetId);
        Assert.Equal("#112233", next.Blue.Color.ToHex());
        Assert.Equal("orange", next.Orange.PresetId);
        Assert.Equal(RgbColor.Orange, next.Orange.Color);
        Assert.Equal("amber", next.Yellow.PresetId);
        Assert.Equal(100, next.CoolMaximum);
        Assert.Equal(PowerGuids.BoostEfficient, next.BoostValue);
        Assert.True(next.AutoEnabled);
        Assert.Equal(70, next.AutoOnCelsius);
        Assert.Equal(69, next.AutoOffCelsius);

        var options = next.ToOptions();
        Assert.False(options.ColorDockIcons);
        Assert.Equal("#112233", options.Colors.Blue.ToHex());
        Assert.Equal("false", next.ToStoredValues()[SettingsKeys.DockColorIcons]);
    }

    [Fact]
    public void Device_labels_round_trip_on_the_stable_hardware_key()
    {
        const string id = "cpu:/cpu/0";
        var seed = new DeviceSettingSeed(id, "CPU", "Processor", "  Pkg  ", null, VisibilityChoice.Hide, VisibilityChoice.Show);
        var state = SettingsFormStateFactory.FromStored(new Dictionary<string, string?>(), [seed]);
        Assert.Equal("Pkg", state.Devices[0].ShortLabel);
        Assert.Equal(string.Empty, state.Devices[0].FullName);
        Assert.Equal(VisibilityChoice.Hide, state.Devices[0].List);
        Assert.Equal(VisibilityChoice.Show, state.Devices[0].Dock);

        var key = SettingsKeys.Device(id, "short");
        Assert.Equal("hwmonitor.device.f57c9201081aab8bca82.short", key);
        var stored = state.ToStoredValues();
        Assert.Equal("Pkg", stored[key]);
        Assert.Equal("hide", stored[SettingsKeys.Device(id, "list")]);
        Assert.Equal("show", stored[SettingsKeys.Device(id, "dock")]);

        var renamed = SettingsFormStateFactory.FromSubmission(
            new Dictionary<string, string?>
            {
                [SettingsKeys.Device(id, "short")] = new string('A', 80),
                [SettingsKeys.Device(id, "dock")] = "hide",
            },
            state);
        Assert.Equal(PresentationMap.MaxShortLabelLength, renamed.Devices[0].ShortLabel.Length);
        Assert.Equal(VisibilityChoice.Hide, renamed.Devices[0].Dock);
        Assert.Equal(VisibilityChoice.Hide, renamed.Devices[0].List);
    }

    [Fact]
    public void Payload_reads_numbers_and_toggles()
    {
        var values = SettingsPayload.Read("""
            {"hwmonitor.refreshIntervalMs":1500,"hwmonitor.compactMode":"true","hwmonitor.dock.colorIcons":false}
            """);
        Assert.Equal("1500", values[SettingsKeys.Refresh]);
        Assert.Equal("true", values[SettingsKeys.Compact]);
        Assert.Equal("false", values[SettingsKeys.DockColorIcons]);
        Assert.Empty(SettingsPayload.Read("not json"));

        var state = SettingsFormStateFactory.FromStored(values, []);
        Assert.Equal(1500, state.RefreshIntervalMs);
        Assert.True(state.Compact);
        Assert.False(state.ColorDockIcons);
    }

    [Fact]
    public void Settings_card_is_compact_and_picks_colors_from_swatches()
    {
        const string packageId = "cpu:/cpu/0";
        const string coreId = "cpu:/cpu/0:core:2";
        var state = SettingsFormStateFactory.FromStored(
            new Dictionary<string, string?>(),
            [
                new DeviceSettingSeed(packageId, "CPU", "Processor", null, null, VisibilityChoice.Default, VisibilityChoice.Default),
                new DeviceSettingSeed(coreId, "Core 2", "Processor", null, null, VisibilityChoice.Default, VisibilityChoice.Default),
            ]);
        var json = SettingsCard.Build(state, UiText.SettingsSaved, false);
        var nodes = Walk(JsonNode.Parse(json)).ToArray();
        var refresh = nodes.Single(node => node["id"]?.GetValue<string>() == SettingsKeys.Refresh);
        Assert.Equal("Input.Number", refresh["type"]!.GetValue<string>());
        Assert.Equal(500, refresh["min"]!.GetValue<int>());
        Assert.Equal(3000, refresh["max"]!.GetValue<int>());
        var general = nodes.Single(node => node["id"]?.GetValue<string>() == "fold-general");
        Assert.False(general["isVisible"]!.GetValue<bool>());
        Assert.Contains(nodes, node => node["type"]?.GetValue<string>() == "Action.ToggleVisibility");
        Assert.Contains(nodes, node => node["type"]?.GetValue<string>() == "ColumnSet");
        Assert.Contains(nodes, node => node["type"]?.GetValue<string>() == "Input.Toggle" && node["id"]?.GetValue<string>() == SettingsKeys.DockColorIcons);
        Assert.DoesNotContain(nodes, node => node["id"]?.GetValue<string>() == SettingsKeys.Preset(SettingsKeys.Blue));
        var swatch = nodes.First(node => node["type"]?.GetValue<string>() == "Image" && node["selectAction"] is not null);
        Assert.StartsWith("data:image/png;base64,", swatch["url"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Action.Submit", swatch["selectAction"]!["type"]!.GetValue<string>());
        Assert.Equal("color", swatch["selectAction"]!["data"]![SettingsKeys.Pick]!.GetValue<string>());
        var cores = nodes.Single(node => node["id"]?.GetValue<string>() == SettingsKeys.CoresFold(packageId));
        Assert.False(cores["isVisible"]!.GetValue<bool>());
        Assert.Contains(SettingsKeys.Device(coreId, "short"), cores.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsKeys.Device(packageId, "short"), cores.ToJsonString(), StringComparison.Ordinal);
        var advanced = nodes.Single(node => node["id"]?.GetValue<string>() == "fold-advanced");
        Assert.False(advanced["isVisible"]!.GetValue<bool>());
        Assert.Contains(SettingsKeys.Custom(SettingsKeys.Blue), advanced.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(nodes, node => node["text"]?.GetValue<string>() == UiText.SettingScopeListDock);
        Assert.Contains(nodes, node => node["text"]?.GetValue<string>() == UiText.SettingScopeDock);
        Assert.Contains(nodes, node => node["text"]?.GetValue<string>() == UiText.SettingScopeCool);
        Assert.Contains(nodes, node => node["text"]?.GetValue<string>() == UiText.SettingScopeAuto);
        Assert.Contains(nodes, node => node["text"]?.GetValue<string>() == UiText.SettingsSaved);
        Assert.DoesNotContain(DockProperties.TitleWidth, json, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.TabularDigits, json, StringComparison.Ordinal);
        Assert.Equal("cpu:/cpu/0", DeviceIds.PackageId(coreId));
        Assert.StartsWith("com.ondery.hwmonitor.cores.", SensorIds.ToCoresPageId(packageId), StringComparison.Ordinal);
    }

    [Fact]
    public void Submit_round_trip_reloads_saved_values_and_reports_reordering()
    {
        const string id = "cpu:/cpu/0";
        var current = SettingsFormStateFactory.FromStored(
            new Dictionary<string, string?>(),
            [new DeviceSettingSeed(id, "CPU", "Processor", null, null, VisibilityChoice.Default, VisibilityChoice.Default)]);
        var submitted = new Dictionary<string, string?>
        {
            [SettingsKeys.Refresh] = "90",
            [SettingsKeys.DockColorIcons] = "false",
            [SettingsKeys.BlueBelow] = "80",
            [SettingsKeys.YellowFrom] = "60",
            [SettingsKeys.OrangeFrom] = "70",
            [SettingsKeys.RedFrom] = "90",
            [SettingsKeys.CoolMax] = "150",
            [SettingsKeys.Auto] = "true",
            [SettingsKeys.AutoOn] = "70",
            [SettingsKeys.AutoOff] = "80",
            [SettingsKeys.Custom(SettingsKeys.Blue)] = "#zzzz",
            [SettingsKeys.Device(id, "dock")] = "show",
            [SettingsKeys.Device(id, "short")] = "Pkg",
        };
        var applied = SettingsFormStateFactory.Apply(submitted, current);
        Assert.Equal(500, applied.State.RefreshIntervalMs);
        Assert.False(applied.State.ColorDockIcons);
        Assert.Equal(80, applied.State.BlueBelow);
        Assert.Equal(80, applied.State.YellowFrom);
        Assert.Equal(81, applied.State.OrangeFrom);
        Assert.Equal(100, applied.State.CoolMaximum);
        Assert.Equal(69, applied.State.AutoOffCelsius);
        Assert.Equal("blue", applied.State.Blue.PresetId);
        Assert.Equal("Pkg", applied.State.Devices[0].ShortLabel);
        Assert.Equal(VisibilityChoice.Show, applied.State.Devices[0].Dock);
        var notes = string.Join(' ', applied.Notices);
        Assert.Contains("limited to 500", notes, StringComparison.Ordinal);
        Assert.Contains("kept in order", notes, StringComparison.Ordinal);
        Assert.Contains("not a color", notes, StringComparison.Ordinal);
        Assert.Contains("lowered to 69", notes, StringComparison.Ordinal);

        var persisted = applied.State.ToStoredValues().ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);
        var seeds = applied.State.Devices.Select(device => new DeviceSettingSeed(
            device.Id,
            device.DefaultShort,
            device.DefaultFull,
            persisted[SettingsKeys.Device(device.Id, "short")],
            persisted[SettingsKeys.Device(device.Id, "full")],
            PresentationMap.ParseVisibility(persisted[SettingsKeys.Device(device.Id, "list")]),
            PresentationMap.ParseVisibility(persisted[SettingsKeys.Device(device.Id, "dock")]))).ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(applied.State.ToStoredValues());
        var reloaded = SettingsFormStateFactory.FromStored(SettingsPayload.Read(json), seeds);
        Assert.Equal(applied.State.RefreshIntervalMs, reloaded.RefreshIntervalMs);
        Assert.Equal(applied.State.ColorDockIcons, reloaded.ColorDockIcons);
        Assert.Equal(applied.State.BlueBelow, reloaded.BlueBelow);
        Assert.Equal(applied.State.YellowFrom, reloaded.YellowFrom);
        Assert.Equal(applied.State.OrangeFrom, reloaded.OrangeFrom);
        Assert.Equal(applied.State.RedFrom, reloaded.RedFrom);
        Assert.Equal(applied.State.CoolMaximum, reloaded.CoolMaximum);
        Assert.Equal(applied.State.AutoOffCelsius, reloaded.AutoOffCelsius);
        Assert.Equal(applied.State.Blue.Color, reloaded.Blue.Color);
        Assert.Equal("Pkg", reloaded.Devices[0].ShortLabel);
        Assert.Equal(VisibilityChoice.Show, reloaded.Devices[0].Dock);
        var png = ColorSwatches.Encode(RgbColor.Blue, selected: false);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());
        Assert.NotEqual(ColorSwatches.DataUri(RgbColor.Blue, false), ColorSwatches.DataUri(RgbColor.Blue, true));
    }

    [Fact]
    public void Cool_mode_cycles_and_the_choice_survives_a_reload()
    {
        Assert.Equal(CoolSelection.On, CoolSelectionCycle.Next(CoolSelection.Auto));
        Assert.Equal(CoolSelection.Off, CoolSelectionCycle.Next(CoolSelection.On));
        Assert.Equal(CoolSelection.Auto, CoolSelectionCycle.Next(CoolSelection.Off));
        Assert.Equal(CoolSelection.Auto, CoolSelectionCycle.Parse(null, true));
        Assert.Equal(CoolSelection.Off, CoolSelectionCycle.Parse(null, false));
        Assert.Equal(CoolSelection.Off, CoolSelectionCycle.Parse(" ", null));
        Assert.Equal(CoolSelection.On, CoolSelectionCycle.Parse("on", true));

        var legacy = SettingsFormStateFactory.FromStored(
            new Dictionary<string, string?> { [SettingsKeys.Auto] = "true" },
            []);
        Assert.Equal(CoolSelection.Auto, legacy.Selection);
        Assert.True(legacy.ToOptions().AutoCool.Enabled);
        Assert.Equal("auto", legacy.ToStoredValues()[SettingsKeys.CoolSelection]);

        var manual = SettingsFormStateFactory.FromStored(new Dictionary<string, string?>(), []);
        Assert.Equal(CoolSelection.Off, manual.Selection);
        Assert.False(manual.ToOptions().AutoCool.Enabled);

        var saved = SettingsFormStateFactory.Apply(
            new Dictionary<string, string?> { [SettingsKeys.CoolSelection] = "on" },
            manual);
        Assert.Equal(CoolSelection.On, saved.State.Selection);
        Assert.False(saved.State.ToOptions().AutoCool.Enabled);
        var json = System.Text.Json.JsonSerializer.Serialize(saved.State.ToStoredValues());
        var loaded = SettingsFormStateFactory.FromStored(SettingsPayload.Read(json), []);
        Assert.Equal(CoolSelection.On, loaded.Selection);
        Assert.Equal(CoolSelection.Off, CoolSelectionCycle.Next(loaded.Selection));
        Assert.Equal(CoolSelection.Auto, CoolSelectionCycle.Next(CoolSelectionCycle.Next(loaded.Selection)));
    }

    [Fact]
    public void Temperature_and_cool_badges_name_the_visible_state()
    {
        var scale = ColorScale.Default;
        var cool = StatusBadges.ForTemperature(scale.BandFor(58), scale.ColorFor(TemperatureBand.Blue));
        var warm = StatusBadges.ForTemperature(scale.BandFor(61), scale.ColorFor(TemperatureBand.Yellow));
        var hot = StatusBadges.ForTemperature(scale.BandFor(81), scale.ColorFor(TemperatureBand.Orange));
        var critical = StatusBadges.ForTemperature(scale.BandFor(90), scale.ColorFor(TemperatureBand.Red));
        Assert.Equal("Cool", cool!.Value.Text);
        Assert.Equal("#3B82F6", cool.Value.Background.ToHex());
        Assert.Equal("Warm", warm!.Value.Text);
        Assert.Equal("#EAB308", warm.Value.Background.ToHex());
        Assert.Equal("Hot", hot!.Value.Text);
        Assert.Equal("#F97316", hot.Value.Background.ToHex());
        Assert.Equal("Critical", critical!.Value.Text);
        Assert.Equal("#EF4444", critical.Value.Background.ToHex());
        Assert.Null(StatusBadges.ForTemperature(TemperatureBand.Unknown, RgbColor.Blue));

        var on = StatusBadges.ForCool(CoolSelection.On, RgbColor.Blue);
        var off = StatusBadges.ForCool(CoolSelection.Off, RgbColor.Blue);
        var auto = StatusBadges.ForCool(CoolSelection.Auto, RgbColor.Blue);
        Assert.Equal("Cool On", on.Text);
        Assert.Equal("Cool Off", off.Text);
        Assert.Equal("Auto", auto.Text);
        Assert.NotEqual(on.Background, off.Background);
        Assert.NotEqual(auto.Background, on.Background);
        Assert.Equal("Cooling", UiText.SectionCooling);
        Assert.Equal("Processors", UiText.SectionProcessors);
        Assert.Equal("Graphics", UiText.SectionGraphics);
        Assert.Equal("Cool Mode", UiText.CoolTitle);
    }

    [Fact]
    public void Dock_icons_use_the_band_color_and_skip_width_hints()
    {
        var colored = SensorIcons.Temperature(DeviceKind.Gpu, new RgbColor(0xF9, 0x73, 0x16), true);
        Assert.NotNull(colored);
        Assert.StartsWith("|ThemedSvg|#F97316|", colored, StringComparison.Ordinal);
        Assert.Contains("{{AccentColor}}", colored, StringComparison.Ordinal);
        Assert.Contains("<svg", colored, StringComparison.Ordinal);
        Assert.Null(SensorIcons.Temperature(DeviceKind.Cpu, null, true));
        Assert.Null(SensorIcons.Temperature(DeviceKind.Cpu, RgbColor.Blue, false));
        Assert.DoesNotContain(DockProperties.TitleWidth, colored, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.SubtitleWidth, colored, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.TabularDigits, colored, StringComparison.Ordinal);

        var on = SensorIcons.Cool(CoolSelection.On, false, RgbColor.Blue);
        var off = SensorIcons.Cool(CoolSelection.Off, false, RgbColor.Blue);
        var auto = SensorIcons.Cool(CoolSelection.Auto, false, RgbColor.Blue);
        var engaged = SensorIcons.Cool(CoolSelection.Auto, true, RgbColor.Blue);
        Assert.StartsWith("|ThemedSvg|#3B82F6|", on, StringComparison.Ordinal);
        Assert.StartsWith("|ThemedSvg|#64748B|", off, StringComparison.Ordinal);
        Assert.NotEqual(on, off);
        Assert.NotEqual(auto, engaged);
        Assert.NotEqual(auto, on);
        Assert.DoesNotContain(DockProperties.TitleWidth, on, StringComparison.Ordinal);
    }

    private static IEnumerable<JsonObject> Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                yield return obj;
                foreach (var pair in obj)
                {
                    foreach (var child in Walk(pair.Value))
                    {
                        yield return child;
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    foreach (var child in Walk(item))
                    {
                        yield return child;
                    }
                }

                break;
        }
    }
}
