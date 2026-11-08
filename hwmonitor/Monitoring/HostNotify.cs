// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Reflection;
using HWMonitor.Core.Notifications;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HWMonitor;

// SDK 0.12 raises PropChanged and ItemsChanged with one multicast Invoke, then swallows the exception.
// A disconnected host handler is left in the list and aborts every later subscriber, including the handler
// added when Command Palette starts again. The process stays up until Dispose, so this path has to deliver
// each subscriber itself and drop only RPC disconnects.
internal static class HostNotify
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(Type Type, string Name), FieldInfo?> Fields = [];
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);
    private static int _dropLogged;

    public static bool Properties(object source, Action mutate, IReadOnlyList<string> propertyNames)
    {
        if (propertyNames.Count == 0)
        {
            return true;
        }

        var args = new object?[propertyNames.Count];
        for (var i = 0; i < propertyNames.Count; i++)
        {
            args[i] = new PropChangedEventArgs(propertyNames[i]);
        }

        return Deliver(source, "PropChanged", mutate, args);
    }

    public static bool Items(object source, Action mutate, int totalItems)
    {
        return Deliver(source, "ItemsChanged", mutate, [new ItemsChangedEventArgs(totalItems)]);
    }

    public static void Signal(object source, string eventName, object args)
    {
        Deliver(source, eventName, static () => { }, [args]);
    }

    private static bool Deliver(object source, string eventName, Action mutate, object?[] args)
    {
        if (!TryBind(source, eventName, out var read, out var write))
        {
            return Run(mutate);
        }

        try
        {
            var droppedCode = 0;
            EventDelivery.Assign(read, write, mutate, source, args, (dropped, code) =>
            {
                if (dropped > 0)
                {
                    droppedCode = code;
                }
            });
            if (droppedCode != 0)
            {
                NoteDrop(droppedCode);
            }

            return true;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return false;
        }
    }

    private static bool Run(Action mutate)
    {
        try
        {
            mutate();
            return true;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return false;
        }
    }

    private static bool TryBind(object source, string eventName, out Func<Delegate?> read, out Action<Delegate?> write)
    {
        var field = Field(source.GetType(), eventName);
        if (field is null)
        {
            NoteMissing(eventName);
            read = static () => null;
            write = static _ => { };
            return false;
        }

        read = () =>
        {
            var value = field.GetValue(source);
            if (value is null || value is Delegate)
            {
                return value as Delegate;
            }

            NoteMissing(eventName);
            return null;
        };
        write = value =>
        {
            var current = field.GetValue(source);
            if (current is not null && current is not Delegate)
            {
                return;
            }

            field.SetValue(source, value);
        };
        return true;
    }

    private static FieldInfo? Field(Type type, string eventName)
    {
        lock (Gate)
        {
            var key = (type, eventName);
            if (Fields.TryGetValue(key, out var cached))
            {
                return cached;
            }

            FieldInfo? found = null;
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                var field = current.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field is not null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    found = field;
                    break;
                }
            }

            Fields[key] = found;
            return found;
        }
    }

    private static void NoteMissing(string eventName)
    {
        lock (Gate)
        {
            if (!Missing.Add(eventName))
            {
                return;
            }
        }

        ExtensionLog.Write("No subscriber list for " + eventName + ". Property changes use the toolkit raise.");
    }

    private static void NoteDrop(int hresult)
    {
        if (Interlocked.Exchange(ref _dropLogged, 1) == 1)
        {
            return;
        }

        ExtensionLog.Write("Dropped a disconnected Command Palette subscriber (0x" + hresult.ToString("X8") + "). Updates continue for the current host.");
    }
}
