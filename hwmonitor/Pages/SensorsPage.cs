// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

internal sealed partial class SensorsPage : ListPage
{
    private IListItem[] _items = [];

    public SensorsPage()
    {
        Id = CommandIds.Page;
        Name = UiText.PageName;
        Title = UiText.PageTitle;
        Icon = new IconInfo(Glyphs.App);
        PlaceholderText = UiText.SearchPlaceholder;
    }

    public override IListItem[] GetItems() => _items;

    public void Publish(IListItem[] items, bool structureChanged)
    {
        _items = items;
        if (IsLoading)
        {
            HostNotify.Properties(this, () => IsLoading = false, ["IsLoading"]);
        }

        if (structureChanged)
        {
            HostNotify.Items(this, () => RaiseItemsChanged(_items.Length), _items.Length);
        }
    }
}
