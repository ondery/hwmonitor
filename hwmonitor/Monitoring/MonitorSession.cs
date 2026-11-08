// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using HWMonitor.Core.Colors;
using HWMonitor.Core.Notifications;
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
    private readonly UsageSampler _usage = new();
    private readonly SensorPipeClient _pipe = new();
    private readonly CoolModeService _cool;
    private readonly AutoCoolController _autoCool = new();
    private readonly SensorBoard _board;
    private readonly Dictionary<string, CoresPage> _corePages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _coreSignatures = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private int _busy;
    private int _settingsGeneration;
    private bool _disposed;

    public MonitorSession(SettingsManager settings)
    {
        _settings = settings;
        _cool = new CoolModeService(settings.SettingsDirectory);
        Page = new SensorsPage();
        _board = new SensorBoard(Page.Id, ToggleCool);
        Band = new WrappedDockItem([], CommandIds.Dock, UiText.AppName);
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Increment(ref _settingsGeneration);
        _usage.SetInterval(Period());
        ArmTimer();
        Tick();
    }

    private object Gate { get; } = new();

    public SensorsPage Page { get; }

    public WrappedDockItem Band { get; }

    public void Start()
    {
        _cool.RecoverOnStartup();
        _usage.Start(Period());
        Tick();
        ArmTimer();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
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

    private void ToggleCool() => _settings.CycleCoolMode();

    private int Period() => MonitorOptions.ClampRefresh(_settings.Current.RefreshIntervalMs);

    private void ArmTimer()
    {
        var period = Period();
        try
        {
            _timer.Change(period, period);
        }
        catch (ObjectDisposedException)
        {
        }
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

            // Usage is sampled on its own timer. This publish keeps the last values.
            var usage = _usage.Latest;
            var readings = SensorCatalog.Compose(sensors, usage.Adapters, usage.CpuPercent);
            _settings.EnsureDevices(readings);
            var options = _settings.Current;
            var presented = readings.Select(options.Devices.Apply).ToArray();
            var package = presented.FirstOrDefault(reading => reading.Kind == DeviceKind.Cpu && reading.IsPrimary);
            switch (options.CoolMode)
            {
                case CoolSelection.On:
                    if (!_cool.IsOn || _cool.ActivatedByAuto)
                    {
                        _cool.Enable(options.Cool, activatedByAuto: false);
                    }

                    break;
                case CoolSelection.Off:
                    if (_cool.IsOn)
                    {
                        _cool.Disable();
                    }

                    break;
                default:
                    if (_cool.IsOn && !_cool.ActivatedByAuto)
                    {
                        _cool.Disable();
                    }

                    var action = _autoCool.Evaluate(options.AutoCool with { Enabled = true }, _cool.IsOn, _cool.ActivatedByAuto, package?.TemperatureCelsius, DateTimeOffset.UtcNow);
                    if (action == AutoCoolAction.Activate)
                    {
                        _cool.Enable(options.Cool, activatedByAuto: true);
                    }
                    else if (action == AutoCoolAction.Deactivate)
                    {
                        _cool.Disable();
                    }

                    break;
            }

            var seenGeneration = Volatile.Read(ref _settingsGeneration);
            var specs = BuildSpecs(presented, options, serviceUp, serviceError);
            if (Volatile.Read(ref _settingsGeneration) != seenGeneration)
            {
                options = _settings.Current;
                presented = readings.Select(options.Devices.Apply).ToArray();
                specs = BuildSpecs(presented, options, serviceUp, serviceError);
            }

            if (_disposed)
            {
                return;
            }

            EnsureCorePages(specs);
            var change = _board.Apply(specs, _corePages);
            Guard(() => Page.Publish(_board.ListItems(), change.ListChanged));
            PublishCorePages(specs);
            if (change.DockChanged)
            {
                Guard(() => PublishDock(_board.DockItems()));
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

    private void EnsureCorePages(IReadOnlyList<RowSpec> specs)
    {
        foreach (var spec in specs)
        {
            if (!spec.OpensCores || spec.PackageReadingId is not string packageId)
            {
                continue;
            }

            var title = UiText.CoresPageTitle(spec.DockSubtitle);
            if (!_corePages.TryGetValue(packageId, out var page))
            {
                _corePages.Add(packageId, new CoresPage(SensorIds.ToCoresPageId(packageId), title));
                continue;
            }

            page.SetTitle(title);
        }
    }

    private void PublishCorePages(IReadOnlyList<RowSpec> specs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in specs.Where(spec => spec.IsCore && spec.InList && spec.PackageReadingId is not null).GroupBy(spec => spec.PackageReadingId!))
        {
            seen.Add(group.Key);
            if (!_corePages.TryGetValue(group.Key, out var page))
            {
                continue;
            }

            var signature = string.Join('\n', group.Select(spec => spec.CommandId));
            var changed = !_coreSignatures.TryGetValue(group.Key, out var previous) || !string.Equals(previous, signature, StringComparison.Ordinal);
            _coreSignatures[group.Key] = signature;
            var items = group.Select(spec => (IListItem)_board.ListItem(spec.CommandId)).ToArray();
            Guard(() => page.Publish(items, changed));
        }

        foreach (var pair in _corePages)
        {
            if (seen.Contains(pair.Key))
            {
                continue;
            }

            if (_coreSignatures.TryGetValue(pair.Key, out var previous) && previous.Length == 0)
            {
                continue;
            }

            _coreSignatures[pair.Key] = string.Empty;
            var page = pair.Value;
            Guard(() => page.Publish([], true));
        }
    }

    private void PublishDock(IListItem[] items)
    {
        // Command is the inner list page. WrappedDockItem itself is not an ICommand,
        // and the host subscribes to ItemsChanged on that list.
        HostNotify.Items(Band.Command, () => Band.Items = items, items.Length);
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }
    }

    public ICommandItem? Find(string id)
    {
        foreach (var page in _corePages.Values)
        {
            if (string.Equals(page.Id, id, StringComparison.Ordinal))
            {
                return page.Link;
            }
        }

        return _board.Find(id);
    }

    private List<RowSpec> BuildSpecs(IReadOnlyList<SensorReading> readings, MonitorOptions options, bool serviceUp, string? serviceError)
    {
        var packagesWithCores = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reading in readings)
        {
            if (reading.Role == SensorRole.Core)
            {
                packagesWithCores.Add(DeviceIds.PackageId(reading.Id));
            }
        }

        var specs = new List<RowSpec> { CoolSpec(options) };
        var processors = new List<RowSpec>();
        var graphics = new List<RowSpec>();
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
            var glyph = reading.Kind == DeviceKind.Cpu ? Glyphs.Cpu : Glyphs.Gpu;
            var isCore = reading.Role == SensorRole.Core;
            var opensCores = !isCore && reading.Kind == DeviceKind.Cpu && packagesWithCores.Contains(reading.Id);
            var row = new RowSpec(
                SensorIds.ToCommandId(reading.Id),
                dockTitle,
                reading.ShortLabel,
                listTitle,
                listSubtitle,
                isCore ? string.Empty : reading.Kind == DeviceKind.Cpu ? UiText.SectionProcessors : UiText.SectionGraphics,
                glyph,
                SensorIcons.Temperature(reading.Kind, color, options.ColorDockIcons) ?? glyph,
                options.Devices.AppearsInList(reading.Id, defaultList),
                options.Devices.AppearsInDock(reading.Id, defaultDock),
                color is RgbColor swatch ? StatusBadges.ForTemperature(band, swatch) : null,
                isCore,
                opensCores,
                isCore ? DeviceIds.PackageId(reading.Id) : opensCores ? reading.Id : null);
            if (reading.Kind == DeviceKind.Cpu)
            {
                processors.Add(row);
            }
            else
            {
                graphics.Add(row);
            }
        }

        specs.AddRange(processors);
        specs.AddRange(graphics);

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
            Glyphs.Status,
            true,
            false,
            null,
            false,
            false,
            null);

    private RowSpec CoolSpec(MonitorOptions options)
    {
        var selection = options.CoolMode;
        var engaged = selection == CoolSelection.Auto && _cool.IsOn;
        var subtitle = _cool.LastError ?? selection switch
        {
            CoolSelection.On => UiText.CoolOnSubtitle,
            CoolSelection.Off => UiText.CoolOffSubtitle,
            _ => engaged ? UiText.CoolAutoSubtitle : UiText.CoolAutoIdleSubtitle,
        };
        var icon = SensorIcons.Cool(selection, engaged || selection == CoolSelection.On, options.Colors.Blue);
        var dockTitle = selection switch
        {
            CoolSelection.On => UiText.CoolDockOn,
            CoolSelection.Off => UiText.CoolDockOff,
            _ => UiText.CoolDockAuto,
        };
        var dockSubtitle = engaged ? UiText.CoolDockEngaged : UiText.CoolDockLabel;
        return new RowSpec(
            CommandIds.Cool,
            dockTitle,
            dockSubtitle,
            UiText.CoolTitle,
            subtitle,
            UiText.SectionCooling,
            icon,
            icon,
            true,
            true,
            StatusBadges.ForCool(selection, options.Colors.Blue),
            false,
            false,
            null);
    }
}

internal sealed record RowSpec(
    string CommandId,
    string DockTitle,
    string DockSubtitle,
    string ListTitle,
    string ListSubtitle,
    string Section,
    string ListIcon,
    string DockIcon,
    bool InList,
    bool InDock,
    StatusBadge? Badge,
    bool IsCore,
    bool OpensCores,
    string? PackageReadingId);

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

    public (bool ListChanged, bool DockChanged) Apply(IReadOnlyList<RowSpec> specs, IReadOnlyDictionary<string, CoresPage> corePages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            seen.Add(spec.CommandId);
            if (!_rows.TryGetValue(spec.CommandId, out var row))
            {
                row = BoundRow.Create(spec, _pageId, _toggleCool, corePages);
                _rows.Add(spec.CommandId, row);
            }

            try
            {
                row.Update(spec, corePages);
            }
            catch (Exception ex)
            {
                ExtensionLog.Write(ex.Message);
            }
        }

        foreach (var stale in _rows.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _rows.Remove(stale);
        }

        var listOrder = specs.Where(spec => spec.InList && !spec.IsCore).Select(spec => spec.CommandId).ToList();
        var dockOrder = specs.Where(spec => spec.InDock).Select(spec => spec.CommandId).ToList();
        var listChanged = !listOrder.SequenceEqual(_listOrder);
        var dockChanged = !dockOrder.SequenceEqual(_dockOrder);
        _listOrder = listOrder;
        _dockOrder = dockOrder;
        return (listChanged, dockChanged);
    }

    public IListItem[] ListItems() => _listOrder.Select(id => (IListItem)_rows[id].List).ToArray();

    public ListItem ListItem(string id) => _rows[id].List;

    public IListItem[] DockItems() => _dockOrder.Select(id => (IListItem)_rows[id].Dock).ToArray();

    public ICommandItem? Find(string id) => _rows.TryGetValue(id, out var row) ? row.Dock : null;
}

internal sealed class BoundRow
{
    private readonly ICommand _pageCommand;
    private ShownText _listShown;
    private ShownText _dockShown;

    private BoundRow(ListItem list, ListItem dock, ICommand pageCommand, ShownText listShown, ShownText dockShown)
    {
        List = list;
        Dock = dock;
        _pageCommand = pageCommand;
        _listShown = listShown;
        _dockShown = dockShown;
    }

    public ListItem List { get; }

    public ListItem Dock { get; }

    public static BoundRow Create(RowSpec spec, string pageId, Action toggleCool, IReadOnlyDictionary<string, CoresPage> corePages)
    {
        var pageCommand = spec.CommandId == CommandIds.Cool
            ? (ICommand)new CallbackCommand(spec.CommandId, UiText.CoolTitle, toggleCool)
            : new OpenPageCommand(spec.CommandId, pageId, spec.DockSubtitle.Length == 0 ? spec.ListTitle : spec.DockSubtitle);
        var listCommand = ListCommand(spec, pageCommand, corePages);
        var list = new ListItem(listCommand) { Icon = new IconInfo(spec.ListIcon) };
        var dock = new ListItem(pageCommand) { Icon = new IconInfo(spec.DockIcon) };
        return new BoundRow(
            list,
            dock,
            pageCommand,
            ShownText.Empty with { Icon = spec.ListIcon },
            ShownText.Empty with { Icon = spec.DockIcon });
    }

    public void Update(RowSpec spec, IReadOnlyDictionary<string, CoresPage> corePages)
    {
        var listCommand = ListCommand(spec, _pageCommand, corePages);
        var badgeKey = BadgeKey(spec.Badge);
        Push(
            List,
            ref _listShown,
            new ShownText(spec.ListTitle, spec.ListSubtitle, spec.Section, spec.ListIcon, badgeKey, Suggest(spec.ListTitle, spec.ListSubtitle, spec.Badge)),
            listCommand,
            spec.Badge);
        Push(
            Dock,
            ref _dockShown,
            new ShownText(spec.DockTitle, spec.DockSubtitle, string.Empty, spec.DockIcon, string.Empty, string.Empty),
            command: null,
            badge: null);
    }

    private static void Push(ListItem item, ref ShownText shown, ShownText next, ICommand? command, StatusBadge? badge)
    {
        var changes = DisplayNotice.Compare(shown, next, command is not null && !ReferenceEquals(item.Command, command));
        if (!changes.Any)
        {
            return;
        }

        var wrote = HostNotify.Properties(item, () =>
        {
            if (changes.Title)
            {
                item.Title = next.Title;
            }

            if (changes.Subtitle)
            {
                item.Subtitle = next.Subtitle;
            }

            if (changes.Section)
            {
                item.Section = next.Section;
            }

            if (changes.Command && command is not null)
            {
                item.Command = command;
            }

            if (changes.Icon)
            {
                item.Icon = new IconInfo(next.Icon);
            }

            if (changes.Tags)
            {
                item.Tags = TagsFor(badge);
            }

            if (changes.Suggest)
            {
                item.TextToSuggest = next.Suggest;
            }
        }, changes.Names());
        if (wrote)
        {
            shown = next;
        }
    }

    private static string BadgeKey(StatusBadge? badge) =>
        badge is StatusBadge shown
            ? shown.Text + "|" + shown.Background.ToHex() + "|" + shown.Foreground.ToHex()
            : string.Empty;

    private static string Suggest(string title, string subtitle, StatusBadge? badge) =>
        badge is StatusBadge extra
            ? string.Join(' ', title, subtitle, extra.Text)
            : string.Join(' ', title, subtitle);

    private static ITag[] TagsFor(StatusBadge? badge) =>
        badge is StatusBadge tag
            ?
            [
                new Tag(tag.Text)
                {
                    Background = ColorHelpers.FromRgb(tag.Background.R, tag.Background.G, tag.Background.B),
                    Foreground = ColorHelpers.FromRgb(tag.Foreground.R, tag.Foreground.G, tag.Foreground.B),
                },
            ]
            : [];

    private static ICommand ListCommand(RowSpec spec, ICommand pageCommand, IReadOnlyDictionary<string, CoresPage> corePages)
    {
        if (spec.OpensCores
            && spec.PackageReadingId is string packageId
            && corePages.TryGetValue(packageId, out var cores))
        {
            return cores;
        }

        return pageCommand;
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
