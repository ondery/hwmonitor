// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace HWMonitor;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();
            var extensionDisposedEvent = new ManualResetEvent(false);

            // One extension instance is reused for every host activation.
            // The process exits only after that instance is disposed. A crashed
            // host cannot dispose it; the next activation keeps the same objects
            // and must still receive live updates.
            HWMonitorExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<HWMonitorExtension, IExtension>(() => extensionInstance);
            server.Start();

            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("HW Monitor is a Command Palette extension. Launch it from Command Palette.");
        }
    }
}
