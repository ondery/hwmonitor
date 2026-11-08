// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.InteropServices;
using HWMonitor.Core.Notifications;
using HWMonitor.Core.Sensors;
using Xunit;

namespace HWMonitor.Tests;

public class HostNotificationTests
{
    [Fact]
    public void Disconnected_subscriber_does_not_stop_later_ticks()
    {
        Assert.Contains(unchecked((int)0x800706BA), HostDisconnect.Codes);
        Assert.Contains(unchecked((int)0x80010108), HostDisconnect.Codes);
        Assert.True(HostDisconnect.Is(new COMException("unavailable", unchecked((int)0x800706BA))));
        Assert.False(HostDisconnect.Is(new InvalidOperationException("other")));

        var deadCalls = 0;
        var live = new List<int>();
        var late = new List<int>();
        Action<object?, object?> dead = (_, _) =>
        {
            deadCalls++;
            throw new COMException("The RPC server is unavailable.", unchecked((int)0x800706BA));
        };
        Action<object?, object?> good = (_, arg) => live.Add((int)arg!);
        Action<object?, object?> joined = (_, arg) => late.Add((int)arg!);
        Delegate? handlers = Delegate.Combine(dead, good);
        var completed = 0;
        for (var tick = 1; tick <= 3; tick++)
        {
            var joinedThisTick = joined;
            EventDelivery.Assign(
                () => handlers,
                value => handlers = value,
                () =>
                {
                    if (tick == 1)
                    {
                        handlers = Delegate.Combine(handlers, joinedThisTick);
                    }
                },
                sender: "item",
                args: [tick]);
            completed++;
        }

        Assert.Equal(3, completed);
        Assert.Equal(1, deadCalls);
        Assert.Equal([1, 2, 3], live);
        Assert.Equal([1, 2, 3], late);
        Assert.Equal(2, handlers!.GetInvocationList().Length);
        Assert.DoesNotContain(dead, handlers.GetInvocationList());
    }

    [Fact]
    public void Unrelated_subscriber_failure_stays_subscribed_and_does_not_stop_the_other()
    {
        var badCalls = 0;
        var good = new List<int>();
        Action<object?, object?> bad = (_, _) =>
        {
            badCalls++;
            throw new COMException("unspecified", unchecked((int)0x80004005));
        };
        Action<object?, object?> live = (_, arg) => good.Add((int)arg!);
        Delegate? handlers = Delegate.Combine(bad, live);
        for (var tick = 1; tick <= 2; tick++)
        {
            EventDelivery.Assign(() => handlers, value => handlers = value, () => { }, "item", [tick]);
        }

        Assert.Equal(2, badCalls);
        Assert.Equal([1, 2], good);
        Assert.Equal(2, handlers!.GetInvocationList().Length);
    }

    [Fact]
    public void A_healthy_subscriber_is_called_once_per_update()
    {
        var calls = 0;
        object? seen = null;
        Action<object?, object?> live = (_, arg) =>
        {
            calls++;
            seen = arg;
        };
        Delegate? handlers = live;
        EventDelivery.Assign(() => handlers, value => handlers = value, () => { }, "item", ["Title"]);
        Assert.Equal(1, calls);
        Assert.Equal("Title", seen);
        Assert.Same(live, handlers);
    }

    [Fact]
    public void Mutate_failure_restores_subscribers_and_does_not_notify()
    {
        var calls = 0;
        Action<object?, object?> live = (_, _) => calls++;
        Delegate? handlers = live;
        var error = Assert.Throws<InvalidOperationException>(() =>
            EventDelivery.Assign(
                () => handlers,
                value => handlers = value,
                () => throw new InvalidOperationException("setter"),
                "item",
                ["Title"]));
        Assert.Equal("setter", error.Message);
        Assert.Equal(0, calls);
        Assert.Same(live, handlers);
    }

    [Fact]
    public void Formatted_text_notifies_only_when_it_changes()
    {
        var icon = "|ThemedSvg|#F97316|<svg></svg>";
        var title = ReadingText.Value(55.2, 72.2, "n/a", false, CultureInfo.InvariantCulture);
        var again = ReadingText.Value(55.1, 72.4, "n/a", false, CultureInfo.InvariantCulture);
        Assert.Equal("55% · 72°C", title);
        Assert.Equal(title, again);

        var dock = new ShownText(title, "CPU", string.Empty, icon, string.Empty, string.Empty);
        var same = dock with { Title = again };
        Assert.False(DisplayNotice.Compare(dock, same, false).Any);
        Assert.Empty(DisplayNotice.Compare(dock, dock, false).Names());

        var nextTitle = ReadingText.Value(56, 72, "n/a", false, CultureInfo.InvariantCulture);
        var changed = DisplayNotice.Compare(dock, dock with { Title = nextTitle }, false);
        Assert.Equal(["Title"], changed.Names());
        Assert.DoesNotContain(DockProperties.TitleWidth, changed.Names());
        Assert.DoesNotContain(DockProperties.SubtitleWidth, changed.Names());
        Assert.DoesNotContain(DockProperties.TabularDigits, changed.Names());

        var tinted = DisplayNotice.Compare(dock, dock with { Icon = icon + "-hot" }, false);
        Assert.Equal(["Icon"], tinted.Names());

        var list = new ShownText("CPU " + title, "Processor · Package", "Processors", "\uE950", "Cool|#3B82F6|#FFFFFF", "CPU " + title + " Processor · Package Cool");
        var listNext = list with
        {
            Title = "CPU " + nextTitle,
            Suggest = "CPU " + nextTitle + " Processor · Package Cool",
        };
        Assert.Equal(["Title", "TextToSuggest"], DisplayNotice.Compare(list, listNext, false).Names());
        Assert.Equal(["Tags"], DisplayNotice.Compare(list, list with { BadgeKey = "Warm|#EAB308|#111111" }, false).Names());
    }
}
