using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>Reads back what <see cref="IvfWriter" /> wrote: the header fields and every frame's bytes and pts.</summary>
internal sealed class IvfFile
{
    public string FourCC { get; private init; }

    public int Width { get; private init; }

    public int Height { get; private init; }

    public int TimebaseDenominator { get; private init; }

    public int TimebaseNumerator { get; private init; }

    public int HeaderFrameCount { get; private init; }

    public List<byte[]> Frames { get; } = new List<byte[]>();

    public List<long> Pts { get; } = new List<long>();

    public static IvfFile Read(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        if (all.Length < 32 || all[0] != (byte)'D' || all[1] != (byte)'K' || all[2] != (byte)'I' || all[3] != (byte)'F')
        {
            throw new InvalidDataException(path + " is not an IVF file");
        }

        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(6));
        IvfFile file = new IvfFile
        {
            FourCC = System.Text.Encoding.ASCII.GetString(all, 8, 4),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(12)),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(14)),
            TimebaseDenominator = (int)BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(16)),
            TimebaseNumerator = (int)BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(20)),
            HeaderFrameCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(24)),
        };

        int offset = headerLength;
        while (offset + 12 <= all.Length)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(offset));
            long pts = BinaryPrimitives.ReadInt64LittleEndian(all.AsSpan(offset + 4));
            offset += 12;
            if (offset + size > all.Length) throw new InvalidDataException(path + ": truncated frame");
            file.Frames.Add(all.AsSpan(offset, size).ToArray());
            file.Pts.Add(pts);
            offset += size;
        }

        return file;
    }
}
