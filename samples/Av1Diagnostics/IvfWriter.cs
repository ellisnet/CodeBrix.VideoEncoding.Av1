using System;
using System.Buffers.Binary;
using System.IO;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>
/// A tiny IVF writer (a copy of the one in the test project): the 32-byte "DKIF" file header, then a 12-byte
/// header (size, pts) before every temporal unit. Sample-only: the library knows nothing about containers.
/// </summary>
internal sealed class IvfWriter : IDisposable
{
    private readonly Stream output;
    private int frames;

    public IvfWriter(Stream output, int width, int height, int timebaseDenominator, int timebaseNumerator)
    {
        this.output = output;
        byte[] header = new byte[32];
        header[0] = (byte)'D';
        header[1] = (byte)'K';
        header[2] = (byte)'I';
        header[3] = (byte)'F';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 32);
        header[8] = (byte)'A';
        header[9] = (byte)'V';
        header[10] = (byte)'0';
        header[11] = (byte)'1';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)timebaseDenominator);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)timebaseNumerator);
        output.Write(header, 0, header.Length);
    }

    public int FrameCount => frames;

    public void WriteFrame(ReadOnlySpan<byte> data, long pts)
    {
        byte[] frameHeader = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(frameHeader.AsSpan(0), (uint)data.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frameHeader.AsSpan(4), pts);
        output.Write(frameHeader, 0, frameHeader.Length);
        output.Write(data);
        frames++;
    }

    public void Dispose()
    {
        // Patch the frame count (bytes 24..27) when the stream allows it.
        if (output.CanSeek)
        {
            long end = output.Position;
            output.Position = 24;
            byte[] count = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)frames);
            output.Write(count, 0, 4);
            output.Position = end;
        }

        output.Flush();
    }
}
