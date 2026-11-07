// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace HWMonitor;

[Guid("37dd9813-4cb9-458b-bc43-41e89443f396")]
public sealed partial class HWMonitorExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly HWMonitorCommandsProvider _provider = new();

    public HWMonitorExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        _provider.Dispose();
        _extensionDisposedEvent.Set();
    }
}
