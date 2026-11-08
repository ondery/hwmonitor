// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using HWMonitor.Core.Sensors;
using HWMonitor.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HWMonitor.SensorService;

internal sealed class SensorWorker : BackgroundService
{
    private readonly ILogger<SensorWorker> _logger;
    private readonly HardwareProbe _probe;

    public SensorWorker(ILogger<SensorWorker> logger)
    {
        _logger = logger;
        _probe = new HardwareProbe(message => _logger.LogWarning("{Message}", message));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Name} is listening on \\\\.\\pipe\\{Pipe}.", SensorServiceContract.DisplayName, SensorServiceContract.PipeName);
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreatePipe();
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleAsync(server, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sensor pipe connection failed.");
                try
                {
                    await Task.Delay(200, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                if (server is not null)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public override void Dispose()
    {
        _probe.Dispose();
        base.Dispose();
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        byte[] request;
        try
        {
            request = await ReadFrameAsync(server, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            await WriteFrameAsync(server, SensorPipeProtocol.Handle([], () => new SensorReadResult([], ex.Message)), cancellationToken).ConfigureAwait(false);
            return;
        }

        var response = SensorPipeProtocol.Handle(request, () => new SensorReadResult(_probe.Read(), _probe.OpenError));
        await WriteFrameAsync(server, response, cancellationToken).ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        var clientRights = PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize;
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            clientRights,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            SensorServiceContract.PipeName,
            PipeDirection.InOut,
            4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > SensorServiceContract.MaxPayloadBytes)
        {
            throw new InvalidDataException("The frame length is not valid.");
        }

        var body = new byte[length];
        await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException("The pipe closed before the frame was complete.");
            }

            offset += read;
        }
    }
}
