// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Sensors;
using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed class MonitorSession : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly HardwareSession _hardware = new();
    private readonly WindowsUsageReader _usage = new();
    private readonly CoolModeService _cool;
    private readonly FanControlService _fan;
    private readonly AutoCoolController _autoCool = new();
    private readonly SensorBoard _board;
    private readonly Timer _timer;
    private int _busy;
    private bool _disposed;

    public MonitorSession(SettingsManager settings)
    {
        _settings = settings;
        _cool = new CoolModeService(settings.SettingsDirectory);
        _fan = new FanControlService(_hardware, settings.SettingsDirectory);
        Page = new SensorsPage();
        _board = new SensorBoard(Page.Id, ToggleCool, CycleFan);
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
        _fan.RecoverOnStartup();
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

            _fan.RestoreToAuto();
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

    private void CycleFan()
    {
        var options = _settings.Current;
        _fan.Cycle(options.Fans, options.ExperimentalMsiEc);
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
            var options = _settings.Current;
            var sensors = _hardware.Read();
            var (cpu, adapters) = _usage.Sample();
            var readings = SensorCatalog.Compose(sensors, adapters, cpu);
            var package = readings.FirstOrDefault(reading => reading.Kind == DeviceKind.Cpu && reading.IsPrimary);
            var action = _autoCool.Evaluate(options.AutoCool, _cool.IsOn, _cool.ActivatedByAuto, package?.TemperatureCelsius, DateTimeOffset.UtcNow);
            if (action == AutoCoolAction.Activate)
            {
                _cool.Enable(options.Cool, activatedByAuto: true);
            }
            else if (action == AutoCoolAction.Deactivate)
            {
                _cool.Disable();
            }

            if (_fan.ModeIndex >= 0 && !options.Fans.EnabledIndexes().Contains(_fan.ModeIndex))
            {
                _fan.RestoreToAuto();
            }

            _fan.Observe(options.ExperimentalMsiEc);

            var specs = BuildSpecs(readings, options);
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

    private List<RowSpec> BuildSpecs(IReadOnlyList<SensorReading> readings, MonitorOptions options)
    {
        var specs = new List<RowSpec> { CoolSpec(), FanSpec(options) };
        var primaries = readings.Where(reading => reading.IsPrimary).ToArray();
        if (primaries.Length > 0 && primaries.All(reading => reading.TemperatureCelsius is null))
        {
            specs.Add(new RowSpec(
                CommandIds.Status,
                UiText.StatusTempsTitle,
                UiText.StatusTempsTitle,
                UiText.StatusTempsSubtitle,
                UiText.SectionStatus,
                Glyphs.Status,
                true,
                false,
                TemperatureBand.Unknown,
                null,
                true));
        }

        if (readings.Count == 0)
        {
            specs.Add(new RowSpec(
                CommandIds.Empty,
                UiText.EmptyTitle,
                UiText.EmptyTitle,
                UiText.EmptySubtitle,
                UiText.SectionStatus,
                Glyphs.Status,
                true,
                false,
                TemperatureBand.Unknown,
                null,
                true));
        }

        foreach (var reading in readings)
        {
            if (options.Compact && !reading.IsPrimary)
            {
                continue;
            }

            var band = options.Colors.BandFor(reading.TemperatureCelsius);
            var color = band == TemperatureBand.Unknown ? (RgbColor?)null : options.Colors.ColorFor(band);
            var role = reading.Role switch
            {
                SensorRole.HottestCore => UiText.RoleHottestCore,
                SensorRole.Core => UiText.RoleCore(reading.CoreNumber ?? 0),
                SensorRole.CpuGraphics => UiText.RoleCpuGraphics,
                SensorRole.Adapter => UiText.RoleAdapter,
                _ => UiText.RolePackage,
            };
            var title = ReadingText.Format(UiText.ReadingFormat, reading.ShortLabel, reading.UsagePercent, reading.TemperatureCelsius, UiText.Unavailable, CultureInfo.CurrentCulture);
            var subtitle = options.Compact ? reading.HardwareName : $"{reading.HardwareName} · {role}";
            specs.Add(new RowSpec(
                SensorIds.ToCommandId(reading.Id),
                title,
                title,
                subtitle,
                reading.Kind == DeviceKind.Cpu ? UiText.SectionProcessors : UiText.SectionGraphics,
                reading.Kind == DeviceKind.Cpu ? Glyphs.Cpu : Glyphs.Gpu,
                true,
                reading.IsPrimary,
                band,
                color,
                true));
        }

        return specs;
    }

    private RowSpec CoolSpec()
    {
        var on = _cool.IsOn;
        var subtitle = _cool.LastError
            ?? (on ? (_cool.ActivatedByAuto ? UiText.CoolAutoSubtitle : UiText.CoolOnSubtitle) : UiText.CoolOffSubtitle);
        return new RowSpec(
            CommandIds.Cool,
            on ? UiText.CoolOn : UiText.CoolOff,
            UiText.CoolTitle,
            subtitle,
            UiText.SectionCooling,
            Glyphs.Cool,
            true,
            true,
            TemperatureBand.Unknown,
            null,
            false);
    }

    private RowSpec FanSpec(MonitorOptions options)
    {
        string dock;
        string subtitle;
        if (!_fan.Supported)
        {
            dock = "Fan unsupported";
            subtitle = _fan.Detail ?? UiText.FanUnsupported;
        }
        else if (_settings.FanError is not null)
        {
            dock = UiText.FanAuto;
            subtitle = _settings.FanError;
        }
        else if (_fan.ModeIndex < 0 || _fan.ModeIndex >= options.Fans.Modes.Count)
        {
            dock = UiText.FanAuto;
            subtitle = UiText.FanFirmwareSubtitle;
        }
        else
        {
            var mode = options.Fans.Modes[_fan.ModeIndex];
            dock = UiText.FanNamed(mode.Name);
            subtitle = UiText.FanDuty(mode.DutyPercent, _fan.AppliedFans);
        }

        return new RowSpec(CommandIds.Fan, dock, UiText.FanTitle, subtitle, UiText.SectionCooling, Glyphs.Fan, true, true, TemperatureBand.Unknown, null, false);
    }
}

internal sealed record RowSpec(
    string CommandId,
    string DockTitle,
    string ListTitle,
    string Subtitle,
    string Section,
    string Glyph,
    bool InList,
    bool InDock,
    TemperatureBand Band,
    RgbColor? Color,
    bool OpensPage);

internal sealed class SensorBoard
{
    private readonly Dictionary<string, BoundRow> _rows = new(StringComparer.Ordinal);
    private readonly string _pageId;
    private readonly Action _toggleCool;
    private readonly Action _cycleFan;
    private List<string> _listOrder = [];
    private List<string> _dockOrder = [];

    public SensorBoard(string pageId, Action toggleCool, Action cycleFan)
    {
        _pageId = pageId;
        _toggleCool = toggleCool;
        _cycleFan = cycleFan;
    }

    public (bool ListChanged, bool DockChanged) Apply(IReadOnlyList<RowSpec> specs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            seen.Add(spec.CommandId);
            if (!_rows.TryGetValue(spec.CommandId, out var row))
            {
                row = BoundRow.Create(spec, _pageId, _toggleCool, _cycleFan);
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
    private BoundRow(ListItem list, ListItem dock)
    {
        List = list;
        Dock = dock;
    }

    public ListItem List { get; }

    public ListItem Dock { get; }

    public static BoundRow Create(RowSpec spec, string pageId, Action toggleCool, Action cycleFan)
    {
        var command = CreateCommand(spec, pageId, toggleCool, cycleFan);
        var list = new ListItem(command) { Icon = new IconInfo(spec.Glyph) };
        var dock = new ListItem(command) { Icon = new IconInfo(spec.Glyph) };
        RememberWidth(dock);
        return new BoundRow(list, dock);
    }

    public void Update(RowSpec spec)
    {
        Set(List, spec.ListTitle, spec.Subtitle, spec.Section, spec);
        var dockSubtitle = spec.Subtitle.StartsWith(UiText.FanUnsupported, StringComparison.Ordinal)
            || spec.Subtitle == UiText.CoolFailed
            ? spec.Subtitle
            : string.Empty;
        Set(Dock, spec.DockTitle, dockSubtitle, string.Empty, spec);
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

    private static void RememberWidth(ListItem item)
    {
        var properties = item.GetProperties();
        properties["Microsoft.CommandPalette.Dock.TabularDigits"] = true;
        properties["Microsoft.CommandPalette.Dock.TitleWidth"] = "18ch";
    }

    private static ICommand CreateCommand(RowSpec spec, string pageId, Action toggleCool, Action cycleFan)
    {
        if (spec.CommandId == CommandIds.Cool)
        {
            return new CallbackCommand(spec.CommandId, UiText.CoolTitle, toggleCool);
        }

        if (spec.CommandId == CommandIds.Fan)
        {
            return new CallbackCommand(spec.CommandId, UiText.FanTitle, cycleFan);
        }

        return new OpenPageCommand(spec.CommandId, pageId, spec.DockTitle);
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
