// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Runtime.ExceptionServices;

namespace HWMonitor.Core.Notifications;

public readonly record struct ShownText(
    string Title,
    string Subtitle,
    string Section,
    string Icon,
    string BadgeKey,
    string Suggest)
{
    public static ShownText Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

public readonly record struct DisplayChanges(
    bool Title,
    bool Subtitle,
    bool Section,
    bool Command,
    bool Icon,
    bool Tags,
    bool Suggest)
{
    public bool Any => Title || Subtitle || Section || Command || Icon || Tags || Suggest;

    public string[] Names()
    {
        var names = new List<string>(7);
        if (Title)
        {
            names.Add("Title");
        }

        if (Subtitle)
        {
            names.Add("Subtitle");
        }

        if (Section)
        {
            names.Add("Section");
        }

        if (Command)
        {
            names.Add("Command");
        }

        if (Icon)
        {
            names.Add("Icon");
        }

        if (Tags)
        {
            names.Add("Tags");
        }

        if (Suggest)
        {
            names.Add("TextToSuggest");
        }

        return names.ToArray();
    }
}

public static class DisplayNotice
{
    public static DisplayChanges Compare(ShownText current, ShownText next, bool commandChanged) => new(
        Changed(current.Title, next.Title),
        Changed(current.Subtitle, next.Subtitle),
        Changed(current.Section, next.Section),
        commandChanged,
        Changed(current.Icon, next.Icon),
        Changed(current.BadgeKey, next.BadgeKey),
        Changed(current.Suggest, next.Suggest));

    private static bool Changed(string? current, string? next) =>
        !string.Equals(current ?? string.Empty, next ?? string.Empty, StringComparison.Ordinal);
}

public static class HostDisconnect
{
    // RPC_E_DISCONNECTED, RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED,
    // RPC_S_CALL_FAILED_DNE, CO_E_OBJNOTCONNECTED, RPC_S_UNKNOWN_IF, RPC_E_SERVERFAULT.
    public static readonly int[] Codes =
    [
        unchecked((int)0x80010108),
        unchecked((int)0x800706BA),
        unchecked((int)0x800706BE),
        unchecked((int)0x800706BF),
        unchecked((int)0x800401FD),
        unchecked((int)0x800706B5),
        unchecked((int)0x80010105),
    ];

    public static bool Is(Exception exception) => Code(exception) != 0;

    public static int Code(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (Codes.Contains(current.HResult))
            {
                return current.HResult;
            }
        }

        return 0;
    }
}

public readonly record struct FanoutResult(Delegate? Survivors, int Delivered, int Dropped, int HResult);

public static class SubscriberFanout
{
    public static FanoutResult Deliver(Delegate? handlers, object? sender, object? args)
    {
        if (handlers is null)
        {
            return new FanoutResult(null, 0, 0, 0);
        }

        Delegate? survivors = null;
        var delivered = 0;
        var dropped = 0;
        var code = 0;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                handler.DynamicInvoke(sender, args);
                survivors = Delegate.Combine(survivors, handler);
                delivered++;
            }
            catch (Exception ex) when (HostDisconnect.Is(ex))
            {
                dropped++;
                if (code == 0)
                {
                    code = HostDisconnect.Code(ex);
                }
            }
            catch
            {
                survivors = Delegate.Combine(survivors, handler);
            }
        }

        return new FanoutResult(survivors, delivered, dropped, code);
    }
}

public static class EventDelivery
{
    public static void Assign(
        Func<Delegate?> read,
        Action<Delegate?> write,
        Action mutate,
        object? sender,
        IReadOnlyList<object?> args,
        Action<int, int>? onDropped = null)
    {
        var taken = Detach(read, write);
        Exception? mutateError = null;
        try
        {
            mutate();
        }
        catch (Exception ex)
        {
            mutateError = ex;
        }

        var during = read();
        var dropped = 0;
        var code = 0;
        Delegate? keptTaken = taken;
        Delegate? keptDuring = Except(during, taken);
        if (mutateError is null)
        {
            keptTaken = DeliverAll(taken, sender, args, ref dropped, ref code);
            keptDuring = DeliverAll(keptDuring, sender, args, ref dropped, ref code);
        }

        Restore(read, write, during, Union(keptTaken, keptDuring));
        onDropped?.Invoke(dropped, code);
        if (mutateError is not null)
        {
            ExceptionDispatchInfo.Capture(mutateError).Throw();
        }
    }

    private static Delegate? DeliverAll(
        Delegate? handlers,
        object? sender,
        IReadOnlyList<object?> args,
        ref int dropped,
        ref int code)
    {
        var current = handlers;
        foreach (var arg in args)
        {
            var result = SubscriberFanout.Deliver(current, sender, arg);
            current = result.Survivors;
            dropped += result.Dropped;
            if (code == 0 && result.HResult != 0)
            {
                code = result.HResult;
            }
        }

        return current;
    }

    private static Delegate? Detach(Func<Delegate?> read, Action<Delegate?> write)
    {
        Delegate? taken = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var current = read();
            taken = Union(taken, current);
            write(null);
            if (read() is null)
            {
                return taken;
            }
        }

        return Union(taken, read());
    }

    private static void Restore(Func<Delegate?> read, Action<Delegate?> write, Delegate? during, Delegate? kept)
    {
        Delegate? baseline = during;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var current = read();
            var next = Union(kept, Except(current, baseline));
            write(next);
            if (Same(read(), next))
            {
                return;
            }

            baseline = current;
        }
    }

    private static Delegate? Union(Delegate? left, Delegate? right) =>
        Combine(left, Except(right, left));

    private static Delegate? Combine(Delegate? left, Delegate? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Delegate.Combine(left, right);
    }

    private static Delegate? Except(Delegate? source, Delegate? remove)
    {
        if (source is null || remove is null)
        {
            return source;
        }

        var drop = new HashSet<Delegate>(remove.GetInvocationList());
        Delegate? result = null;
        foreach (var handler in source.GetInvocationList())
        {
            if (!drop.Contains(handler))
            {
                result = Delegate.Combine(result, handler);
            }
        }

        return result;
    }

    private static bool Same(Delegate? left, Delegate? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        var a = left.GetInvocationList();
        var b = right.GetInvocationList();
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
