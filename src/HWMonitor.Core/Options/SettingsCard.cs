// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json.Nodes;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;

namespace HWMonitor.Core.Options;

public static class SettingsCard
{
    public static string Build(SettingsFormState state) => Build(state, null, false);

    public static string Build(SettingsFormState state, string? notice, bool warning)
    {
        var body = new JsonArray();
        if (!string.IsNullOrWhiteSpace(notice))
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = notice,
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["color"] = warning ? "Warning" : "Good",
            });
        }

        AddFold(body, "fold-general", UiText.SettingGroupGeneral, UiText.SettingScopeListDock, General(state));
        AddFold(body, "fold-colors", UiText.SettingGroupColors, UiText.SettingScopeListDock, Colors(state));
        AddFold(body, "fold-cool", UiText.SettingGroupCool, UiText.SettingScopeCool, Cool(state));
        AddFold(body, "fold-devices", UiText.SettingGroupDevices, UiText.SettingGroupDevicesHelp, Devices(state));
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

    private static List<JsonNode> General(SettingsFormState state) =>
    [
        Columns(
            NumberColumn(SettingsKeys.Refresh, UiText.SettingRefreshShort, UiText.SettingScopeListDock, state.RefreshIntervalMs, 500, 3000),
            ToggleColumn(SettingsKeys.Compact, UiText.SettingCompactShort, UiText.SettingScopeList, state.Compact),
            ToggleColumn(SettingsKeys.DockColorIcons, UiText.SettingDockColorShort, UiText.SettingScopeDock, state.ColorDockIcons)),
    ];

    private static List<JsonNode> Colors(SettingsFormState state)
    {
        var items = new List<JsonNode>
        {
            Columns(
                NumberColumn(SettingsKeys.BlueBelow, UiText.TagBlue, UiText.SettingScopeListDock, state.BlueBelow, 0, 120),
                NumberColumn(SettingsKeys.YellowFrom, UiText.TagYellow, UiText.SettingScopeListDock, state.YellowFrom, 0, 120),
                NumberColumn(SettingsKeys.OrangeFrom, UiText.TagOrange, UiText.SettingScopeListDock, state.OrangeFrom, 1, 124),
                NumberColumn(SettingsKeys.RedFrom, UiText.TagRed, UiText.SettingScopeListDock, state.RedFrom, 2, 125)),
        };
        AddBand(items, UiText.TagBlue, SettingsKeys.Blue, state.Blue);
        AddBand(items, UiText.TagYellow, SettingsKeys.Yellow, state.Yellow);
        AddBand(items, UiText.TagOrange, SettingsKeys.Orange, state.Orange);
        AddBand(items, UiText.TagRed, SettingsKeys.Red, state.Red);
        items.Add(Header("fold-advanced", UiText.SettingAdvanced, UiText.SettingAdvancedHelp));
        items.Add(Body("fold-advanced",
        [
            Columns(
                TextColumn(SettingsKeys.Custom(SettingsKeys.Blue), UiText.CustomHexLabel(UiText.TagBlue), UiText.SettingScopeListDock, state.Blue.CustomHex, UiText.SettingCustomHexPlaceholder),
                TextColumn(SettingsKeys.Custom(SettingsKeys.Yellow), UiText.CustomHexLabel(UiText.TagYellow), UiText.SettingScopeListDock, state.Yellow.CustomHex, UiText.SettingCustomHexPlaceholder),
                TextColumn(SettingsKeys.Custom(SettingsKeys.Orange), UiText.CustomHexLabel(UiText.TagOrange), UiText.SettingScopeListDock, state.Orange.CustomHex, UiText.SettingCustomHexPlaceholder),
                TextColumn(SettingsKeys.Custom(SettingsKeys.Red), UiText.CustomHexLabel(UiText.TagRed), UiText.SettingScopeListDock, state.Red.CustomHex, UiText.SettingCustomHexPlaceholder)),
        ]));
        return items;
    }

    private static List<JsonNode> Cool(SettingsFormState state) =>
    [
        Columns(
            NumberColumn(SettingsKeys.CoolMax, UiText.SettingCoolMaxShort, UiText.SettingScopeCool, state.CoolMaximum, 1, 100),
            ToggleColumn(SettingsKeys.CoolBoost, UiText.SettingCoolBoostShort, UiText.SettingScopeCool, state.LimitBoost),
            ChoiceColumn(
                SettingsKeys.BoostMode,
                UiText.SettingBoostShort,
                UiText.SettingScopeCool,
                state.BoostValue == PowerGuids.BoostEfficient ? "3" : "0",
                [
                    (UiText.SettingBoostDisabled, "0"),
                    (UiText.SettingBoostEfficient, "3"),
                ])),
        Columns(
            ChoiceColumn(
                SettingsKeys.CoolSelection,
                UiText.SettingCoolModeShort,
                UiText.SettingScopeListDock,
                CoolSelectionCycle.Token(state.Selection),
                [
                    (UiText.CoolBadgeAuto, "auto"),
                    (UiText.CoolBadgeOn, "on"),
                    (UiText.CoolBadgeOff, "off"),
                ]),
            NumberColumn(SettingsKeys.AutoOn, UiText.SettingAutoOnShort, UiText.SettingScopeAuto, state.AutoOnCelsius, 1, 125),
            NumberColumn(SettingsKeys.AutoOnSeconds, UiText.SettingAutoOnSecondsShort, UiText.SettingScopeAuto, state.AutoOnSeconds, 1, 600),
            NumberColumn(SettingsKeys.AutoOff, UiText.SettingAutoOffShort, UiText.SettingScopeAuto, state.AutoOffCelsius, 0, 124),
            NumberColumn(SettingsKeys.AutoOffSeconds, UiText.SettingAutoOffSecondsShort, UiText.SettingScopeAuto, state.AutoOffSeconds, 1, 600)),
    ];

    private static List<JsonNode> Devices(SettingsFormState state)
    {
        var items = new List<JsonNode>();
        if (state.Devices.Count == 0)
        {
            items.Add(Hint(UiText.SettingGroupDevicesEmpty));
            return items;
        }

        var packages = state.Devices.Where(device => device.Id.StartsWith("cpu:", StringComparison.Ordinal) && !DeviceIds.IsCore(device.Id)).ToArray();
        var cores = state.Devices.Where(device => DeviceIds.IsCore(device.Id)).ToArray();
        var adapters = state.Devices.Where(device => !device.Id.StartsWith("cpu:", StringComparison.Ordinal)).ToArray();
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            items.Add(Caption(package.DefaultShort, UiText.DeviceHardwareId(package.Id)));
            items.Add(DeviceRow(package));
            var children = cores.Where(core => string.Equals(DeviceIds.PackageId(core.Id), package.Id, StringComparison.Ordinal)).ToArray();
            foreach (var child in children)
            {
                owned.Add(child.Id);
            }

            if (children.Length == 0)
            {
                continue;
            }

            var fold = SettingsKeys.CoresFold(package.Id);
            items.Add(Header(fold, string.Format(CultureInfo.CurrentCulture, UiText.SettingCoresCount, children.Length), UiText.SettingScopeListDock));
            items.Add(Body(fold, children.SelectMany(core => new JsonNode[] { Caption(core.DefaultShort, UiText.DeviceHardwareId(core.Id)), DeviceRow(core) }).ToList()));
        }

        var loose = cores.Where(core => !owned.Contains(core.Id)).ToArray();
        if (loose.Length > 0)
        {
            items.Add(Header("fold-loose-cores", UiText.CoresCommand, UiText.SettingScopeListDock));
            items.Add(Body("fold-loose-cores", loose.SelectMany(core => new JsonNode[] { Caption(core.DefaultShort, UiText.DeviceHardwareId(core.Id)), DeviceRow(core) }).ToList()));
        }

        foreach (var adapter in adapters)
        {
            items.Add(Caption(adapter.DefaultShort, UiText.DeviceHardwareId(adapter.Id)));
            items.Add(DeviceRow(adapter));
        }

        return items;
    }

    private static void AddBand(List<JsonNode> items, string name, string key, ColorChoice choice)
    {
        items.Add(new JsonObject
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "Small",
            ["columns"] = new JsonArray
            {
                Column("auto",
                [
                    new JsonObject
                    {
                        ["type"] = "Image",
                        ["url"] = ColorSwatches.DataUri(choice.Color, true),
                        ["size"] = "Small",
                        ["altText"] = choice.Color.ToHex(),
                    },
                ]),
                Column("stretch",
                [
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = name,
                        ["weight"] = "Bolder",
                        ["size"] = "Small",
                        ["wrap"] = true,
                    },
                    Hint(choice.Color.ToHex() + " · " + UiText.SettingScopeListDock),
                ]),
            },
        });
        items.Add(SwatchRow(ColorPresets.Palette.Take(7), choice, key));
        items.Add(SwatchRow(ColorPresets.Palette.Skip(7), choice, key));
    }

    private static JsonObject SwatchRow(IEnumerable<NamedColor> colors, ColorChoice choice, string key)
    {
        var columns = new JsonArray();
        foreach (var preset in colors)
        {
            var selected = string.Equals(choice.PresetId, preset.Id, StringComparison.Ordinal);
            columns.Add(Column("auto",
            [
                new JsonObject
                {
                    ["type"] = "Image",
                    ["url"] = ColorSwatches.DataUri(preset.Color, selected),
                    ["size"] = "Medium",
                    ["altText"] = UiText.ColorPreset(preset.Id),
                    ["selectAction"] = new JsonObject
                    {
                        ["type"] = "Action.Submit",
                        ["title"] = UiText.ColorPreset(preset.Id),
                        ["data"] = new JsonObject
                        {
                            [SettingsKeys.Pick] = "color",
                            [SettingsKeys.Preset(key)] = preset.Id,
                        },
                    },
                },
            ]));
        }

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "None",
            ["columns"] = columns,
        };
    }

    private static JsonObject DeviceRow(DeviceFormState device) =>
        Columns(
            TextColumn(SettingsKeys.Device(device.Id, "short"), UiText.SettingDeviceShortShort, UiText.SettingScopeListDock, device.ShortLabel, device.DefaultShort),
            TextColumn(SettingsKeys.Device(device.Id, "full"), UiText.SettingDeviceFullShort, UiText.SettingScopeList, device.FullName, device.DefaultFull),
            ChoiceColumn(
                SettingsKeys.Device(device.Id, "list"),
                UiText.SettingDeviceListShort,
                UiText.SettingScopeList,
                PresentationMap.VisibilityToken(device.List),
                Visibility()),
            ChoiceColumn(
                SettingsKeys.Device(device.Id, "dock"),
                UiText.SettingDeviceDockShort,
                UiText.SettingScopeDock,
                PresentationMap.VisibilityToken(device.Dock),
                Visibility()));

    private static void AddFold(JsonArray body, string id, string title, string scope, List<JsonNode> children)
    {
        body.Add(Header(id, title, scope));
        body.Add(Body(id, children));
    }

    private static JsonObject Header(string id, string title, string scope) => new()
    {
        ["type"] = "Container",
        ["spacing"] = "Small",
        ["selectAction"] = new JsonObject
        {
            ["type"] = "Action.ToggleVisibility",
            ["title"] = title,
            ["targetElements"] = new JsonArray { id },
        },
        ["items"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "ColumnSet",
                ["columns"] = new JsonArray
                {
                    Column("stretch",
                    [
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = title,
                            ["weight"] = "Bolder",
                            ["wrap"] = true,
                        },
                        Hint(scope),
                    ]),
                    Column("auto",
                    [
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = UiText.SettingOpen,
                            ["size"] = "Small",
                            ["isSubtle"] = true,
                        },
                    ]),
                },
            },
        },
    };

    private static JsonObject Body(string id, List<JsonNode> children)
    {
        var items = new JsonArray();
        foreach (var child in children)
        {
            items.Add(child);
        }

        return new JsonObject
        {
            ["type"] = "Container",
            ["id"] = id,
            ["isVisible"] = false,
            ["spacing"] = "None",
            ["items"] = items,
        };
    }

    private static JsonObject Columns(params JsonObject[] columns)
    {
        var items = new JsonArray();
        foreach (var column in columns)
        {
            items.Add(column);
        }

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "Small",
            ["columns"] = items,
        };
    }

    private static JsonObject Column(string width, IReadOnlyList<JsonNode> items)
    {
        var body = new JsonArray();
        foreach (var item in items)
        {
            body.Add(item);
        }

        return new JsonObject
        {
            ["type"] = "Column",
            ["width"] = width,
            ["spacing"] = "Small",
            ["items"] = body,
        };
    }

    private static JsonObject NumberColumn(string id, string label, string scope, int value, int min, int max) =>
        Column("stretch",
        [
            Label(label),
            Hint(scope),
            new JsonObject
            {
                ["type"] = "Input.Number",
                ["id"] = id,
                ["value"] = value,
                ["min"] = min,
                ["max"] = max,
                ["spacing"] = "None",
            },
        ]);

    private static JsonObject ToggleColumn(string id, string label, string scope, bool value) =>
        Column("stretch",
        [
            Label(label),
            Hint(scope),
            new JsonObject
            {
                ["type"] = "Input.Toggle",
                ["id"] = id,
                ["title"] = " ",
                ["value"] = value ? "true" : "false",
                ["valueOn"] = "true",
                ["valueOff"] = "false",
                ["spacing"] = "None",
            },
        ]);

    private static JsonObject TextColumn(string id, string label, string scope, string value, string? placeholder = null) =>
        Column("stretch",
        [
            Label(label),
            Hint(scope),
            new JsonObject
            {
                ["type"] = "Input.Text",
                ["id"] = id,
                ["value"] = value,
                ["placeholder"] = placeholder ?? string.Empty,
                ["spacing"] = "None",
            },
        ]);

    private static JsonObject ChoiceColumn(string id, string label, string scope, string value, IReadOnlyList<(string Title, string Value)> choices)
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

        return Column("stretch",
        [
            Label(label),
            Hint(scope),
            new JsonObject
            {
                ["type"] = "Input.ChoiceSet",
                ["id"] = id,
                ["style"] = "compact",
                ["value"] = value,
                ["spacing"] = "None",
                ["choices"] = nodes,
            },
        ]);
    }

    private static List<(string Title, string Value)> Visibility() =>
    [
        (UiText.VisibilityDefault, "default"),
        (UiText.VisibilityShow, "show"),
        (UiText.VisibilityHide, "hide"),
    ];

    private static JsonObject Caption(string title, string scope) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = title + " · " + scope,
        ["weight"] = "Bolder",
        ["size"] = "Small",
        ["wrap"] = true,
        ["spacing"] = "Small",
    };

    private static JsonObject Label(string text) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["size"] = "Small",
        ["weight"] = "Bolder",
        ["wrap"] = true,
        ["spacing"] = "None",
    };

    private static JsonObject Hint(string text) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["size"] = "Small",
        ["isSubtle"] = true,
        ["wrap"] = true,
        ["spacing"] = "None",
    };
}
