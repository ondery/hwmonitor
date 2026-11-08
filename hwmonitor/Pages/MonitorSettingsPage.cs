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

    public void Refresh()
    {
        var json = SettingsCard.Build(_settings.FormState);
        if (!string.Equals(TemplateJson, json, StringComparison.Ordinal))
        {
            TemplateJson = json;
        }
    }

    public override ICommandResult SubmitForm(string inputs) => Apply(inputs);

    public override ICommandResult SubmitForm(string inputs, string data) => Apply(inputs);

    private ICommandResult Apply(string inputs)
    {
        try
        {
            var values = SettingsPayload.Read(inputs);
            if (values.Count == 0)
            {
                return CommandResult.KeepOpen();
            }

            _settings.ApplySubmission(values);
            return CommandResult.GoHome();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.ToString());
            return CommandResult.KeepOpen();
        }
    }
}

public sealed partial class MonitorCommandSettings : ICommandSettings
{
    internal MonitorCommandSettings(MonitorSettingsPage page, SettingsManager settings)
    {
        SettingsPage = page;
        settings.Changed += (_, _) => SettingsChanged?.Invoke(this, this);
    }

    public IContentPage SettingsPage { get; }

    public event TypedEventHandler<object, ICommandSettings>? SettingsChanged;
}
