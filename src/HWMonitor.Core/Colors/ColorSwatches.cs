// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.IO.Compression;

namespace HWMonitor.Core.Colors;

public static class ColorSwatches
{
    public static string DataUri(RgbColor color, bool selected)
    {
        var png = Encode(color, selected);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public static byte[] Encode(RgbColor fill, bool selected)
    {
        const int size = 22;
        var thickness = selected ? 3 : 1;
        var border = selected ? fill.ContrastText() : new RgbColor(92, 101, 112);
        var raw = new byte[(size * 3 + 1) * size];
        for (var y = 0; y < size; y++)
        {
            var row = y * (size * 3 + 1);
            raw[row] = 0;
            for (var x = 0; x < size; x++)
            {
                var edge = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness;
                var color = edge ? border : fill;
                var index = row + 1 + (x * 3);
                raw[index] = color.R;
                raw[index + 1] = color.G;
                raw[index + 2] = color.B;
            }
        }

        using var payload = new MemoryStream();
        payload.WriteByte(0x78);
        payload.WriteByte(0x9C);
        using (var deflate = new DeflateStream(payload, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        var adler = Adler32(raw);
        payload.WriteByte((byte)(adler >> 24));
        payload.WriteByte((byte)(adler >> 16));
        payload.WriteByte((byte)(adler >> 8));
        payload.WriteByte((byte)adler);

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(png, "IHDR", Header(size, size));
        WriteChunk(png, "IDAT", payload.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        return header;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcSource = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcSource, 0);
        data.CopyTo(crcSource, typeBytes.Length);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(crcSource));
        stream.Write(crc);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (crc & 1) == 1 ? 0xEDB88320u : 0;
                crc = (crc >> 1) ^ mask;
            }
        }

        return ~crc;
    }
}
