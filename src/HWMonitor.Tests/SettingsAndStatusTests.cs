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
            [SettingsKeys.Auto] = "true",
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
    public void Settings_card_groups_typed_inputs_and_states_where_each_one_applies()
    {
        var state = SettingsFormStateFactory.FromStored(
            new Dictionary<string, string?>(),
            [new DeviceSettingSeed("cpu:/cpu/0", "CPU", "Processor", null, null, VisibilityChoice.Default, VisibilityChoice.Default)]);
        var json = SettingsCard.Build(state);
        var card = JsonNode.Parse(json)!.AsObject();
        var body = card["body"]!.AsArray();
        Assert.Equal(
            ["General", "Temperature colors", "Cool Mode", "Devices"],
            body.Select(item => item!["items"]![0]!["text"]!.GetValue<string>()).ToArray());

        var nodes = Walk(card).ToArray();
        var refresh = nodes.Single(node => node["id"]?.GetValue<string>() == SettingsKeys.Refresh);
        Assert.Equal("Input.Number", refresh["type"]!.GetValue<string>());
        Assert.Equal(500, refresh["min"]!.GetValue<int>());
        Assert.Equal(3000, refresh["max"]!.GetValue<int>());
        Assert.Contains(nodes, node => node["type"]?.GetValue<string>() == "Input.Toggle" && node["id"]?.GetValue<string>() == SettingsKeys.DockColorIcons);
        Assert.Contains(nodes, node => node["type"]?.GetValue<string>() == "Input.Toggle" && node["id"]?.GetValue<string>() == SettingsKeys.Compact);
        var color = nodes.Single(node => node["id"]?.GetValue<string>() == SettingsKeys.Preset(SettingsKeys.Blue));
        Assert.Equal("Input.ChoiceSet", color["type"]!.GetValue<string>());
        Assert.Equal("compact", color["style"]!.GetValue<string>());
        Assert.Equal("blue", color["value"]!.GetValue<string>());
        var titles = color["choices"]!.AsArray().Select(choice => choice!["title"]!.GetValue<string>()).ToArray();
        Assert.Contains("Blue", titles);
        Assert.Contains("Custom", titles);
        Assert.Contains(nodes, node => node["id"]?.GetValue<string>() == SettingsKeys.Custom(SettingsKeys.Blue) && node["type"]?.GetValue<string>() == "Input.Text");
        Assert.Contains("Applies to the Dock only", json, StringComparison.Ordinal);
        Assert.Contains("Applies to the list page and Dock", json, StringComparison.Ordinal);
        Assert.Contains("Applies to the list page.", json, StringComparison.Ordinal);
        Assert.Contains("Used by Cool Mode auto-trigger", json, StringComparison.Ordinal);
        Assert.Contains("Used by Cool Mode.", json, StringComparison.Ordinal);
        Assert.Contains("hwmonitor.device.f57c9201081aab8bca82.short", json, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.TitleWidth, json, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.SubtitleWidth, json, StringComparison.Ordinal);
        Assert.DoesNotContain(DockProperties.TabularDigits, json, StringComparison.Ordinal);
        foreach (var preset in ColorPresets.Palette)
        {
            Assert.DoesNotContain("ColorPreset_", UiText.ColorPreset(preset.Id), StringComparison.Ordinal);
        }

        Assert.Equal("Custom", UiText.ColorPreset(ColorPresets.CustomId));
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

        var on = StatusBadges.ForCool(true, RgbColor.Blue);
        var off = StatusBadges.ForCool(false, RgbColor.Blue);
        Assert.Equal("Cool ON", on.Text);
        Assert.Equal("Cool OFF", off.Text);
        Assert.NotEqual(on.Background, off.Background);
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

        var on = SensorIcons.Cool(true, RgbColor.Blue);
        var off = SensorIcons.Cool(false, RgbColor.Blue);
        Assert.StartsWith("|ThemedSvg|#3B82F6|", on, StringComparison.Ordinal);
        Assert.StartsWith("|ThemedSvg|#64748B|", off, StringComparison.Ordinal);
        Assert.NotEqual(on, off);
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
