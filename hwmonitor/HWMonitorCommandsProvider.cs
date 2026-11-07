// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class HWMonitorCommandsProvider : CommandProvider, IDisposable
{
    private readonly SettingsManager _settings = new();
    private readonly MonitorSession _session;
    private readonly ICommandItem[] _commands;

    public HWMonitorCommandsProvider()
    {
        DisplayName = UiText.AppName;
        Id = CommandIds.Provider;
        Icon = new IconInfo(Glyphs.App);
        _session = new MonitorSession(_settings);
        _commands =
        [
            new CommandItem(_session.Page)
            {
                Title = DisplayName,
                Subtitle = UiText.AppSubtitle,
                MoreCommands = [new CommandContextItem(_settings.Settings.SettingsPage)],
            },
        ];
        Settings = _settings.Settings;
        _session.Start();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_session.Band];

    public override ICommandItem? GetCommandItem(string id)
    {
        if (string.Equals(id, CommandIds.Page, StringComparison.Ordinal))
        {
            return _commands[0];
        }

        return _session.Find(id);
    }

    public override void Dispose() => _session.Dispose();
}
