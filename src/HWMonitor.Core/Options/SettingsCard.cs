// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;

namespace HWMonitor.Core.Options;

public static class SettingsCard
{
    public static string Build(SettingsFormState state)
    {
        var body = new JsonArray
        {
            Group(UiText.SettingGroupGeneral, UiText.SettingGroupGeneralHelp, false, General(state)),
            Group(UiText.SettingGroupColors, UiText.SettingGroupColorsHelp, true, Colors(state)),
            Group(UiText.SettingGroupCool, UiText.SettingGroupCoolHelp, true, Cool(state)),
            Group(UiText.SettingGroupDevices, UiText.SettingGroupDevicesHelp, true, Devices(state)),
        };
        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Action.Submit",
                    ["title"] = UiText.SettingSave,
                },
            },
        };
        return card.ToJsonString();
    }

    private static List<JsonNode> General(SettingsFormState state)
    {
        var items = new List<JsonNode>();
        Add(items, Number(SettingsKeys.Refresh, UiText.SettingRefreshLabel, state.RefreshIntervalMs, 500, 3000), UiText.SettingRefreshDescription);
        Add(items, Toggle(SettingsKeys.Compact, UiText.SettingCompactLabel, state.Compact), UiText.SettingCompactDescription);
        Add(items, Toggle(SettingsKeys.DockColorIcons, UiText.SettingDockColorLabel, state.ColorDockIcons), UiText.SettingDockColorDescription);
        return items;
    }

    private static List<JsonNode> Colors(SettingsFormState state)
    {
        var items = new List<JsonNode>();
        Add(items, Number(SettingsKeys.BlueBelow, UiText.SettingBlueBelowLabel, state.BlueBelow, 0, 120), UiText.SettingThresholdDescription);
        Add(items, Number(SettingsKeys.YellowFrom, UiText.SettingYellowLabel, state.YellowFrom, 0, 120), UiText.SettingThresholdDescription);
        Add(items, Number(SettingsKeys.OrangeFrom, UiText.SettingOrangeLabel, state.OrangeFrom, 1, 124), UiText.SettingThresholdDescription);
        Add(items, Number(SettingsKeys.RedFrom, UiText.SettingRedLabel, state.RedFrom, 2, 125), UiText.SettingThresholdDescription);
        AddColor(items, UiText.SettingBlueColorLabel, UiText.TagBlue, SettingsKeys.Blue, state.Blue);
        AddColor(items, UiText.SettingYellowColorLabel, UiText.TagYellow, SettingsKeys.Yellow, state.Yellow);
        AddColor(items, UiText.SettingOrangeColorLabel, UiText.TagOrange, SettingsKeys.Orange, state.Orange);
        AddColor(items, UiText.SettingRedColorLabel, UiText.TagRed, SettingsKeys.Red, state.Red);
        return items;
    }

    private static List<JsonNode> Cool(SettingsFormState state)
    {
        var items = new List<JsonNode>();
        Add(items, Number(SettingsKeys.CoolMax, UiText.SettingCoolMaxLabel, state.CoolMaximum, 1, 100), UiText.SettingCoolMaxDescription);
        Add(items, Toggle(SettingsKeys.CoolBoost, UiText.SettingCoolBoostLabel, state.LimitBoost), UiText.SettingCoolBoostDescription);
        Add(items, Choice(
            SettingsKeys.BoostMode,
            UiText.SettingBoostChoiceLabel,
            state.BoostValue == PowerGuids.BoostEfficient ? "3" : "0",
            [
                new(UiText.SettingBoostDisabled, "0"),
                new(UiText.SettingBoostEfficient, "3"),
            ]), UiText.SettingBoostChoiceDescription);
        Add(items, Toggle(SettingsKeys.Auto, UiText.SettingAutoLabel, state.AutoEnabled), UiText.SettingAutoDescription);
        Add(items, Number(SettingsKeys.AutoOn, UiText.SettingAutoOnLabel, state.AutoOnCelsius, 1, 125), UiText.SettingAutoOnDescription);
        Add(items, Number(SettingsKeys.AutoOnSeconds, UiText.SettingAutoOnSecondsLabel, state.AutoOnSeconds, 1, 600), UiText.SettingAutoOnSecondsDescription);
        Add(items, Number(SettingsKeys.AutoOff, UiText.SettingAutoOffLabel, state.AutoOffCelsius, 0, 124), UiText.SettingAutoOffDescription);
        Add(items, Number(SettingsKeys.AutoOffSeconds, UiText.SettingAutoOffSecondsLabel, state.AutoOffSeconds, 1, 600), UiText.SettingAutoOffSecondsDescription);
        return items;
    }

    private static List<JsonNode> Devices(SettingsFormState state)
    {
        var items = new List<JsonNode>();
        if (state.Devices.Count == 0)
        {
            items.Add(Hint(UiText.SettingGroupDevicesEmpty));
            return items;
        }

        foreach (var device in state.Devices)
        {
            var fields = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = device.DefaultShort,
                    ["weight"] = "Bolder",
                    ["wrap"] = true,
                    ["spacing"] = "Medium",
                },
                Hint(UiText.DeviceHardwareId(device.Id)),
            };
            foreach (var node in DeviceFields(device))
            {
                fields.Add(node);
            }

            items.Add(new JsonObject
            {
                ["type"] = "Container",
                ["spacing"] = "Medium",
                ["items"] = fields,
            });
        }

        return items;
    }

    private static List<JsonNode> DeviceFields(DeviceFormState device)
    {
        var items = new List<JsonNode>();
        Add(items, Text(SettingsKeys.Device(device.Id, "short"), UiText.DeviceShortLabel(device.DefaultShort), device.ShortLabel, device.DefaultShort), UiText.DeviceShortDescription(device.Id));
        Add(items, Text(SettingsKeys.Device(device.Id, "full"), UiText.DeviceFullLabel(device.DefaultShort), device.FullName, device.DefaultFull), UiText.DeviceFullDescription);
        Add(items, Choice(SettingsKeys.Device(device.Id, "list"), UiText.DeviceListLabel(device.DefaultShort), PresentationMap.VisibilityToken(device.List), VisibilityChoices()), UiText.SettingDeviceListDescription);
        Add(items, Choice(SettingsKeys.Device(device.Id, "dock"), UiText.DeviceDockLabel(device.DefaultShort), PresentationMap.VisibilityToken(device.Dock), VisibilityChoices()), UiText.SettingDeviceDockDescription);
        return items;
    }

    private static void AddColor(List<JsonNode> items, string label, string band, string key, ColorChoice choice)
    {
        var choices = new List<(string Title, string Value)>();
        foreach (var preset in ColorPresets.Palette)
        {
            choices.Add((UiText.ColorPreset(preset.Id), preset.Id));
        }

        choices.Add((UiText.ColorPreset(ColorPresets.CustomId), ColorPresets.CustomId));
        Add(items, Choice(SettingsKeys.Preset(key), label, choice.PresetId, choices), UiText.SettingColorDescription);
        Add(items, Text(SettingsKeys.Custom(key), UiText.CustomHexLabel(band), choice.CustomHex, UiText.SettingCustomHexPlaceholder), UiText.SettingCustomHexDescription);
    }

    private static List<(string Title, string Value)> VisibilityChoices() =>
    [
        (UiText.VisibilityDefault, "default"),
        (UiText.VisibilityShow, "show"),
        (UiText.VisibilityHide, "hide"),
    ];

    private static void Add(List<JsonNode> items, JsonObject input, string help)
    {
        items.Add(input);
        items.Add(Hint(help));
    }

    private static JsonObject Group(string title, string help, bool separate, List<JsonNode> children)
    {
        var items = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = title,
                ["weight"] = "Bolder",
                ["size"] = "Medium",
                ["wrap"] = true,
            },
            Hint(help),
        };
        foreach (var child in children)
        {
            items.Add(child);
        }

        var container = new JsonObject
        {
            ["type"] = "Container",
            ["spacing"] = "Large",
            ["items"] = items,
        };
        if (separate)
        {
            container["separator"] = true;
        }

        return container;
    }

    private static JsonObject Hint(string text) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["wrap"] = true,
        ["size"] = "Small",
        ["isSubtle"] = true,
        ["spacing"] = "None",
    };

    private static JsonObject Number(string id, string label, int value, int min, int max) => new()
    {
        ["type"] = "Input.Number",
        ["id"] = id,
        ["label"] = label,
        ["value"] = value,
        ["min"] = min,
        ["max"] = max,
        ["spacing"] = "Medium",
    };

    private static JsonObject Toggle(string id, string label, bool value) => new()
    {
        ["type"] = "Input.Toggle",
        ["id"] = id,
        ["label"] = label,
        ["title"] = " ",
        ["value"] = value ? "true" : "false",
        ["valueOn"] = "true",
        ["valueOff"] = "false",
        ["spacing"] = "Small",
    };

    private static JsonObject Text(string id, string label, string value, string placeholder) => new()
    {
        ["type"] = "Input.Text",
        ["id"] = id,
        ["label"] = label,
        ["value"] = value,
        ["placeholder"] = placeholder,
        ["spacing"] = "Medium",
    };

    private static JsonObject Choice(string id, string label, string value, IReadOnlyList<(string Title, string Value)> choices)
    {
        var nodes = new JsonArray();
        foreach (var choice in choices)
        {
            nodes.Add(new JsonObject
            {
                ["title"] = choice.Title,
                ["value"] = choice.Value,
            });
        }

        return new JsonObject
        {
            ["type"] = "Input.ChoiceSet",
            ["id"] = id,
            ["label"] = label,
            ["style"] = "compact",
            ["value"] = value,
            ["choices"] = nodes,
            ["spacing"] = "Medium",
        };
    }
}
