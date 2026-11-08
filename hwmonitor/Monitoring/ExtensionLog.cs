// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor;

internal static class ExtensionLog
{
    public static void Write(string message)
    {
        try
        {
            Microsoft.CommandPalette.Extensions.Toolkit.ExtensionHost.LogMessage(message);
        }
        catch
        {
            // The host logger is unavailable when the process is not activated.
        }
    }
}
