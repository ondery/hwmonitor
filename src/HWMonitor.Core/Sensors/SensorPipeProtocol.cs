// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HWMonitor.Core.Sensors;

public static class SensorServiceContract
{
    public const int Version = 1;
    public const string PipeName = "Ondery.HWMonitor.Sensors";
    public const string ServiceName = "HWMonitorSensorService";
    public const string DisplayName = "HW Monitor Sensor Service";
    public const string InstallDirectory = @"C:\Program Files\Ondery\HWMonitor\SensorService";
    public const int MaxPayloadBytes = 256 * 1024;
    public const string GetOperation = "get";
    public const string UnsupportedOperation = "unsupported operation";
    public const string UnsupportedVersion = "unsupported version";
    public const string InvalidRequest = "invalid request";
}

public sealed record SensorSnapshot(
    int Version,
    bool Ok,
    DateTimeOffset CapturedUtc,
    string? Error,
    IReadOnlyList<RawSensor> Sensors);

public static class SensorPipeProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] EncodeGetRequest() =>
        JsonSerializer.SerializeToUtf8Bytes(new RequestDto { V = SensorServiceContract.Version, Op = SensorServiceContract.GetOperation }, JsonOptions);

    public static byte[] Handle(ReadOnlySpan<byte> request, Func<SensorReadResult> read)
    {
        if (!TryParseRequest(request, out var operation, out var error))
        {
            return Encode(Failed(error ?? SensorServiceContract.InvalidRequest));
        }

        if (!string.Equals(operation, SensorServiceContract.GetOperation, StringComparison.Ordinal))
        {
            return Encode(Failed(SensorServiceContract.UnsupportedOperation));
        }

        SensorReadResult result;
        try
        {
            result = read();
        }
        catch (Exception ex)
        {
            result = new SensorReadResult([], ex.Message);
        }

        return Encode(new SensorSnapshot(
            SensorServiceContract.Version,
            true,
            DateTimeOffset.UtcNow,
            result.Error,
            result.Sensors));
    }

    public static bool TryParseSnapshot(ReadOnlySpan<byte> payload, out SensorSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        try
        {
            var dto = JsonSerializer.Deserialize<SnapshotDto>(payload, JsonOptions);
            if (dto is null || dto.V != SensorServiceContract.Version)
            {
                error = SensorServiceContract.UnsupportedVersion;
                return false;
            }

            snapshot = new SensorSnapshot(
                dto.V,
                dto.Ok,
                dto.CapturedUtc,
                dto.Error,
                dto.Sensors ?? []);
            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = SensorServiceContract.InvalidRequest;
            return false;
        }
    }

    private static bool TryParseRequest(ReadOnlySpan<byte> payload, out string? operation, out string? error)
    {
        operation = null;
        try
        {
            var dto = JsonSerializer.Deserialize<RequestDto>(payload, JsonOptions);
            if (dto is null)
            {
                error = SensorServiceContract.InvalidRequest;
                return false;
            }

            if (dto.V != SensorServiceContract.Version)
            {
                error = SensorServiceContract.UnsupportedVersion;
                return false;
            }

            operation = dto.Op;
            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = SensorServiceContract.InvalidRequest;
            return false;
        }
    }

    private static SensorSnapshot Failed(string error) =>
        new(SensorServiceContract.Version, false, DateTimeOffset.UtcNow, error, []);

    private static byte[] Encode(SensorSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new SnapshotDto
            {
                V = snapshot.Version,
                Ok = snapshot.Ok,
                CapturedUtc = snapshot.CapturedUtc,
                Error = snapshot.Error,
                Sensors = snapshot.Sensors.ToList(),
            },
            JsonOptions);

    private sealed class RequestDto
    {
        public int V { get; set; }

        public string? Op { get; set; }
    }

    private sealed class SnapshotDto
    {
        public int V { get; set; }

        public bool Ok { get; set; }

        public DateTimeOffset CapturedUtc { get; set; }

        public string? Error { get; set; }

        public List<RawSensor>? Sensors { get; set; }
    }
}

public readonly record struct SensorReadResult(IReadOnlyList<RawSensor> Sensors, string? Error);

public static class SensorPipeFrames
{
    public static void Write(Stream stream, ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 2 or > SensorServiceContract.MaxPayloadBytes)
        {
            throw new InvalidOperationException("The frame is outside the supported size.");
        }

        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    public static bool TryRead(Stream stream, out byte[] payload, out string? error)
    {
        payload = [];
        var header = new byte[4];
        if (!TryReadExact(stream, header))
        {
            error = "The pipe closed before a frame header arrived.";
            return false;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > SensorServiceContract.MaxPayloadBytes)
        {
            error = "The frame length is not valid.";
            return false;
        }

        payload = new byte[length];
        if (!TryReadExact(stream, payload))
        {
            error = "The pipe closed before the frame body arrived.";
            payload = [];
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
