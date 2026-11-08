// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class CoresPage : ListPage
{
    private readonly ListItem _empty;
    private IListItem[] _items;

    public CoresPage(string id, string title)
    {
        Id = id;
        Name = UiText.CoresCommand;
        Title = title;
        Icon = new IconInfo(Glyphs.Cpu);
        _empty = new ListItem(new OpenPageCommand(id + ".back", CommandIds.Page, UiText.CoresEmptyTitle))
        {
            Title = UiText.CoresEmptyTitle,
            Subtitle = UiText.CoresEmptySubtitle,
            Icon = new IconInfo(Glyphs.Status),
        };
        _items = [_empty];
        Link = new CommandItem(this)
        {
            Title = title,
            Subtitle = UiText.CoresCommand,
            Icon = Icon,
        };
    }

    public CommandItem Link { get; }

    public void SetTitle(string title)
    {
        if (string.Equals(Title, title, StringComparison.Ordinal))
        {
            return;
        }

        Title = title;
        Link.Title = title;
    }

    public override IListItem[] GetItems() => _items;

    public void Publish(IListItem[] items, bool changed)
    {
        _items = items.Length == 0 ? [_empty] : items;
        if (changed)
        {
            RaiseItemsChanged(_items.Length);
        }
    }
}
