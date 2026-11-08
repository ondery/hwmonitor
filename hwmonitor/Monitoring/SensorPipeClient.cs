// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.IO.Pipes;
using HWMonitor.Core.Sensors;

namespace HWMonitor;

internal sealed class SensorPipeClient
{
    private const int ConnectTimeoutMs = 250;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private int _failures;

    public bool TryQuery(out SensorSnapshot? snapshot)
    {
        snapshot = null;
        if (DateTimeOffset.UtcNow < _retryAfter)
        {
            return false;
        }

        try
        {
            snapshot = Query();
            _failures = 0;
            _retryAfter = DateTimeOffset.MinValue;
            return true;
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            if (_failures < 8)
            {
                _failures++;
            }

            var delayMs = FailureBackoff.DelayMs(_failures, 2_000, 15_000);
            _retryAfter = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
            snapshot = null;
            return false;
        }
    }

    private static SensorSnapshot Query()
    {
        using var pipe = new NamedPipeClientStream(".", SensorServiceContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(ConnectTimeoutMs);
        try
        {
            pipe.ReadTimeout = 1000;
            pipe.WriteTimeout = 1000;
        }
        catch (InvalidOperationException)
        {
            // Some pipe streams only honor the connect timeout.
        }

        var request = SensorPipeProtocol.EncodeGetRequest();
        Span<byte> header = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, request.Length);
        pipe.Write(header);
        pipe.Write(request);
        pipe.Flush();
        if (!SensorPipeFrames.TryRead(pipe, out var body, out var error))
        {
            throw new IOException(error ?? "The sensor service closed the pipe.");
        }

        if (!SensorPipeProtocol.TryParseSnapshot(body, out var snapshot, out error) || snapshot is null)
        {
            throw new IOException(error ?? SensorServiceContract.InvalidRequest);
        }

        return snapshot;
    }
}
