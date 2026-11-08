// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using HWMonitor.Hardware;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed class MonitorSession : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly HardwareProbe _hardware = new(ExtensionLog.Write);
    private readonly WindowsUsageReader _usage = new();
    private readonly SensorPipeClient _pipe = new();
    private readonly CoolModeService _cool;
    private readonly AutoCoolController _autoCool = new();
    private readonly SensorBoard _board;
    private readonly Timer _timer;
    private int _busy;
    private bool _disposed;

    public MonitorSession(SettingsManager settings)
    {
        _settings = settings;
        _cool = new CoolModeService(settings.SettingsDirectory);
        Page = new SensorsPage();
        _board = new SensorBoard(Page.Id, ToggleCool);
        Band = new WrappedDockItem([], CommandIds.Dock, UiText.AppName);
        _settings.Changed += (_, _) =>
        {
            lock (Gate)
            {
                RestartTimer();
            }
        };
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private object Gate { get; } = new();

    public SensorsPage Page { get; }

    public WrappedDockItem Band { get; }

    public void Start()
    {
        _cool.RecoverOnStartup();
        Tick();
        RestartTimer();
    }

    public ICommandItem? Find(string id) => _board.Find(id);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        try
        {
            if (_cool.IsOn)
            {
                _cool.Disable();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }

        _usage.Dispose();
        _hardware.Dispose();
    }

    private void ToggleCool()
    {
        var options = _settings.Current;
        if (_cool.IsOn)
        {
            _cool.Disable();
        }
        else
        {
            _cool.Enable(options.Cool, activatedByAuto: false);
        }

        Tick();
    }

    private void RestartTimer()
    {
        var period = _settings.Current.RefreshIntervalMs;
        _timer.Change(period, period);
    }

    private void Tick()
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            var serviceUp = _pipe.TryQuery(out var snapshot);
            IReadOnlyList<RawSensor> sensors;
            string? serviceError = null;
            if (serviceUp && snapshot is { Ok: true })
            {
                sensors = snapshot.Sensors;
                serviceError = snapshot.Error;
                _hardware.Release();
            }
            else
            {
                sensors = _hardware.Read();
                if (serviceUp)
                {
                    serviceError = snapshot?.Error;
                }
            }

            var (cpu, adapters) = _usage.Sample();
            var readings = SensorCatalog.Compose(sensors, adapters, cpu);
            _settings.EnsureDevices(readings);
            var options = _settings.Current;
            var presented = readings.Select(options.Devices.Apply).ToArray();
            var package = presented.FirstOrDefault(reading => reading.Kind == DeviceKind.Cpu && reading.IsPrimary);
            var action = _autoCool.Evaluate(options.AutoCool, _cool.IsOn, _cool.ActivatedByAuto, package?.TemperatureCelsius, DateTimeOffset.UtcNow);
            if (action == AutoCoolAction.Activate)
            {
                _cool.Enable(options.Cool, activatedByAuto: true);
            }
            else if (action == AutoCoolAction.Deactivate)
            {
                _cool.Disable();
            }

            var specs = BuildSpecs(presented, options, serviceUp, serviceError);
            var change = _board.Apply(specs);
            Page.Publish(_board.ListItems(), change.ListChanged);
            if (change.DockChanged)
            {
                Band.Items = _board.DockItems();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.ToString());
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private List<RowSpec> BuildSpecs(IReadOnlyList<SensorReading> readings, MonitorOptions options, bool serviceUp, string? serviceError)
    {
        var specs = new List<RowSpec> { CoolSpec() };
        var installHint = !serviceUp && readings.Any(TemperatureGaps.IsSharedDieGap);
        var driverHint = serviceUp && readings.Any(TemperatureGaps.IsSharedDieGap);
        if (installHint)
        {
            specs.Add(Status(CommandIds.Status, UiText.StatusServiceTitle, UiText.StatusServiceSubtitle));
        }
        else if (driverHint || (readings.Count > 0 && readings.Where(reading => reading.IsPrimary).All(reading => reading.TemperatureCelsius is null)))
        {
            var subtitle = string.IsNullOrWhiteSpace(serviceError)
                ? UiText.StatusTempsSubtitle
                : serviceError;
            specs.Add(Status(CommandIds.Status, UiText.StatusTempsTitle, subtitle));
        }

        if (readings.Count == 0)
        {
            specs.Add(Status(CommandIds.Empty, UiText.EmptyTitle, UiText.EmptySubtitle));
        }

        foreach (var reading in readings)
        {
            var defaultList = reading.IsPrimary || !options.Compact;
            var defaultDock = reading.IsPrimary;
            if (!options.Devices.AppearsInList(reading.Id, defaultList)
                && !options.Devices.AppearsInDock(reading.Id, defaultDock))
            {
                continue;
            }

            var hideMissing = !serviceUp && TemperatureGaps.IsSharedDieGap(reading);
            var band = options.Colors.BandFor(reading.TemperatureCelsius);
            var color = band == TemperatureBand.Unknown ? (RgbColor?)null : options.Colors.ColorFor(band);
            var role = reading.Role switch
            {
                SensorRole.HottestCore => UiText.RoleHottestCore,
                SensorRole.Core => UiText.RoleCore(reading.CoreNumber ?? 0),
                SensorRole.CpuGraphics => UiText.RoleCpuGraphics,
                SensorRole.SharedPackage => UiText.RoleSharedPackage,
                SensorRole.Adapter => UiText.RoleAdapter,
                _ => UiText.RolePackage,
            };
            var culture = CultureInfo.CurrentCulture;
            var listTitle = ReadingText.ListLine(reading.ShortLabel, reading.UsagePercent, reading.TemperatureCelsius, UiText.Unavailable, hideMissing, culture);
            var listSubtitle = hideMissing ? UiText.StatusServiceTitle : $"{reading.HardwareName} · {role}";
            var dockTitle = ReadingText.Value(reading.UsagePercent, reading.TemperatureCelsius, UiText.Unavailable, hideMissing, culture);
            specs.Add(new RowSpec(
                SensorIds.ToCommandId(reading.Id),
                dockTitle,
                reading.ShortLabel,
                listTitle,
                listSubtitle,
                reading.Kind == DeviceKind.Cpu ? UiText.SectionProcessors : UiText.SectionGraphics,
                reading.Kind == DeviceKind.Cpu ? Glyphs.Cpu : Glyphs.Gpu,
                options.Devices.AppearsInList(reading.Id, defaultList),
                options.Devices.AppearsInDock(reading.Id, defaultDock),
                band,
                color,
                true,
                ReadingText.SensorTitleSample,
                reading.ShortLabel));
        }

        return specs;
    }

    private static RowSpec Status(string id, string title, string subtitle) =>
        new(
            id,
            title,
            string.Empty,
            title,
            subtitle,
            UiText.SectionStatus,
            Glyphs.Status,
            true,
            false,
            TemperatureBand.Unknown,
            null,
            true,
            title,
            " ");

    private RowSpec CoolSpec()
    {
        var on = _cool.IsOn;
        var subtitle = _cool.LastError
            ?? (on ? (_cool.ActivatedByAuto ? UiText.CoolAutoSubtitle : UiText.CoolOnSubtitle) : UiText.CoolOffSubtitle);
        return new RowSpec(
            CommandIds.Cool,
            on ? UiText.CoolDockOn : UiText.CoolDockOff,
            UiText.CoolDockLabel,
            on ? UiText.CoolOn : UiText.CoolOff,
            subtitle,
            UiText.SectionCooling,
            Glyphs.Cool,
            true,
            true,
            TemperatureBand.Unknown,
            null,
            false,
            UiText.CoolDockOff,
            UiText.CoolDockLabel);
    }
}

internal sealed record RowSpec(
    string CommandId,
    string DockTitle,
    string DockSubtitle,
    string ListTitle,
    string ListSubtitle,
    string Section,
    string Glyph,
    bool InList,
    bool InDock,
    TemperatureBand Band,
    RgbColor? Color,
    bool OpensPage,
    string TitleSample,
    string SubtitleSample);

internal sealed class SensorBoard
{
    private readonly Dictionary<string, BoundRow> _rows = new(StringComparer.Ordinal);
    private readonly string _pageId;
    private readonly Action _toggleCool;
    private List<string> _listOrder = [];
    private List<string> _dockOrder = [];

    public SensorBoard(string pageId, Action toggleCool)
    {
        _pageId = pageId;
        _toggleCool = toggleCool;
    }

    public (bool ListChanged, bool DockChanged) Apply(IReadOnlyList<RowSpec> specs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            seen.Add(spec.CommandId);
            if (!_rows.TryGetValue(spec.CommandId, out var row))
            {
                row = BoundRow.Create(spec, _pageId, _toggleCool);
                _rows.Add(spec.CommandId, row);
            }

            row.Update(spec);
        }

        foreach (var stale in _rows.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _rows.Remove(stale);
        }

        var listOrder = specs.Where(spec => spec.InList).Select(spec => spec.CommandId).ToList();
        var dockOrder = specs.Where(spec => spec.InDock).Select(spec => spec.CommandId).ToList();
        var listChanged = !listOrder.SequenceEqual(_listOrder);
        var dockChanged = !dockOrder.SequenceEqual(_dockOrder);
        _listOrder = listOrder;
        _dockOrder = dockOrder;
        return (listChanged, dockChanged);
    }

    public IListItem[] ListItems() => _listOrder.Select(id => (IListItem)_rows[id].List).ToArray();

    public IListItem[] DockItems() => _dockOrder.Select(id => (IListItem)_rows[id].Dock).ToArray();

    public ICommandItem? Find(string id) => _rows.TryGetValue(id, out var row) ? row.Dock : null;
}

internal sealed class BoundRow
{
    private string _titleSample = string.Empty;
    private string _subtitleSample = string.Empty;

    private BoundRow(ListItem list, ListItem dock)
    {
        List = list;
        Dock = dock;
    }

    public ListItem List { get; }

    public ListItem Dock { get; }

    public static BoundRow Create(RowSpec spec, string pageId, Action toggleCool)
    {
        var command = spec.CommandId == CommandIds.Cool
            ? new CallbackCommand(spec.CommandId, UiText.CoolTitle, toggleCool)
            : (ICommand)new OpenPageCommand(spec.CommandId, pageId, spec.DockSubtitle.Length == 0 ? spec.ListTitle : spec.DockSubtitle);
        var list = new ListItem(command) { Icon = new IconInfo(spec.Glyph) };
        var dock = new ListItem(command) { Icon = new IconInfo(spec.Glyph) };
        return new BoundRow(list, dock);
    }

    public void Update(RowSpec spec)
    {
        Set(List, spec.ListTitle, spec.ListSubtitle, spec.Section, spec);
        Set(Dock, spec.DockTitle, spec.DockSubtitle, string.Empty, spec);
        if (!string.Equals(_titleSample, spec.TitleSample, StringComparison.Ordinal)
            || !string.Equals(_subtitleSample, spec.SubtitleSample, StringComparison.Ordinal))
        {
            ApplyDockLayout(Dock, spec.TitleSample, spec.SubtitleSample);
            _titleSample = spec.TitleSample;
            _subtitleSample = spec.SubtitleSample;
        }
    }

    private static void Set(ListItem item, string title, string subtitle, string section, RowSpec spec)
    {
        if (!string.Equals(item.Title, title, StringComparison.Ordinal))
        {
            item.Title = title;
        }

        if (!string.Equals(item.Subtitle, subtitle, StringComparison.Ordinal))
        {
            item.Subtitle = subtitle;
        }

        if (!string.Equals(item.Section, section, StringComparison.Ordinal))
        {
            item.Section = section;
        }

        if (spec.Color is not RgbColor color || spec.Band == TemperatureBand.Unknown)
        {
            if (item.Tags.Length != 0)
            {
                item.Tags = [];
            }

            return;
        }

        var text = spec.Band switch
        {
            TemperatureBand.Yellow => UiText.TagYellow,
            TemperatureBand.Orange => UiText.TagOrange,
            TemperatureBand.Red => UiText.TagRed,
            _ => UiText.TagBlue,
        };
        var contrast = color.ContrastText();
        item.Tags =
        [
            new Tag(text)
            {
                Background = ColorHelpers.FromRgb(color.R, color.G, color.B),
                Foreground = ColorHelpers.FromRgb(contrast.R, contrast.G, contrast.B),
            },
        ];
    }

    private static void ApplyDockLayout(ListItem item, string titleSample, string subtitleSample)
    {
        var properties = item.GetProperties();
        properties[DockProperties.TabularDigits] = true;
        properties[DockProperties.TitleWidth] = DockProperties.Sample(titleSample);
        properties[DockProperties.SubtitleWidth] = DockProperties.Sample(subtitleSample);
    }
}

internal sealed partial class CallbackCommand : InvokableCommand
{
    private readonly Action _action;

    public CallbackCommand(string id, string name, Action action)
    {
        Id = id;
        Name = name;
        _action = action;
    }

    public override ICommandResult Invoke()
    {
        _action();
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class OpenPageCommand : InvokableCommand
{
    private readonly string _pageId;

    public OpenPageCommand(string id, string pageId, string name)
    {
        Id = id;
        Name = name;
        _pageId = pageId;
    }

    public override ICommandResult Invoke() => CommandResult.GoToPage(new GoToPageArgs { PageId = _pageId });
}
