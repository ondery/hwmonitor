// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Options;
using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace HWMonitor;

internal sealed partial class MonitorSettingsPage : ContentPage
{
    private readonly MonitorSettingsForm _form;

    public MonitorSettingsPage(SettingsManager settings)
    {
        Name = UiText.SettingsPageName;
        Title = UiText.SettingsPageTitle;
        Icon = new IconInfo("\uE713");
        _form = new MonitorSettingsForm(settings);
        settings.DevicesChanged += (_, _) => _form.Refresh();
    }

    public override IContent[] GetContent()
    {
        _form.Refresh();
        return [_form];
    }
}

internal sealed partial class MonitorSettingsForm : FormContent
{
    private readonly SettingsManager _settings;

    public MonitorSettingsForm(SettingsManager settings) => _settings = settings;

    private string? _notice;
    private bool _noticeWarning;

    public void Refresh()
    {
        var json = SettingsCard.Build(_settings.FormState, _notice, _noticeWarning);
        if (!string.Equals(TemplateJson, json, StringComparison.Ordinal))
        {
            HostNotify.Properties(this, () => TemplateJson = json, ["TemplateJson"]);
        }
    }

    public override ICommandResult SubmitForm(string inputs) => Apply(inputs, null);

    public override ICommandResult SubmitForm(string inputs, string data) => Apply(inputs, data);

    private ICommandResult Apply(string inputs, string? data)
    {
        try
        {
            var values = SettingsPayload.Read(inputs);
            foreach (var pair in SettingsPayload.Read(data))
            {
                values[pair.Key] = pair.Value;
            }

            if (!values.Keys.Any(key => key.StartsWith("hwmonitor.", StringComparison.Ordinal) && !string.Equals(key, SettingsKeys.Pick, StringComparison.Ordinal)))
            {
                _notice = UiText.SettingsEmpty;
                _noticeWarning = true;
                Refresh();
                ShowNotice(_notice, warning: true);
                return CommandResult.KeepOpen();
            }

            var result = _settings.ApplySubmission(values);
            var message = result.Notices.Count == 0
                ? UiText.SettingsSaved
                : UiText.SettingsSaved + " " + string.Join(' ', result.Notices);
            _notice = message;
            _noticeWarning = result.Notices.Count > 0;
            Refresh();
            ShowNotice(message, _noticeWarning);
            return CommandResult.KeepOpen();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.ToString());
            _notice = UiText.SettingsEmpty;
            _noticeWarning = true;
            Refresh();
            ShowNotice(_notice, warning: true);
            return CommandResult.KeepOpen();
        }
    }

    private static void ShowNotice(string message, bool warning)
    {
        ExtensionHost.ShowStatus(
            new StatusMessage
            {
                Message = message,
                State = warning ? MessageState.Warning : MessageState.Success,
            },
            StatusContext.Page);
    }
}

public sealed partial class MonitorCommandSettings : ICommandSettings
{
    internal MonitorCommandSettings(MonitorSettingsPage page, SettingsManager settings)
    {
        SettingsPage = page;
        settings.Changed += (_, _) => HostNotify.Signal(this, nameof(SettingsChanged), this);
    }

    public IContentPage SettingsPage { get; }

    public event TypedEventHandler<object, ICommandSettings>? SettingsChanged;
}
