// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Sensors;
using HWMonitor.SensorService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = SensorServiceContract.ServiceName;
});
builder.Services.AddHostedService<SensorWorker>();
await builder.Build().RunAsync().ConfigureAwait(false);
