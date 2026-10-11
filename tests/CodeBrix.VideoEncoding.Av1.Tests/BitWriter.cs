using System;
using System.Collections.Generic;

namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>Writes AV1-style big-endian bit fields, for building hand-made OBUs in tests.</summary>
internal sealed class BitWriter
{
    private readonly List<byte> bytes = new List<byte>();
    private int bitCount;

    public BitWriter Bits(long value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if ((bitCount & 7) == 0) bytes.Add(0);
            int bit = (int)((value >> i) & 1);
            bytes[bytes.Count - 1] |= (byte)(bit << (7 - (bitCount & 7)));
            bitCount++;
        }

        return this;
    }

    public BitWriter Flag(bool value) => Bits(value ? 1 : 0, 1);

    public BitWriter Uvlc(uint value)
    {
        ulong v = (ulong)value + 1;
        int length = 0;
        while ((v >> (length + 1)) != 0) length++;
        Bits(0, length);
        return Bits((long)v, length + 1);
    }

    /// <summary>Pads with zero bits to a byte boundary (the trailing_bits of a real OBU start with a 1 bit).</summary>
    public BitWriter TrailingBits()
    {
        Bits(1, 1);
        while ((bitCount & 7) != 0) Bits(0, 1);
        return this;
    }

    public byte[] ToArray() => bytes.ToArray();

    public static byte[] Leb128(int value)
    {
        List<byte> result = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            result.Add(b);
        }
        while (value != 0);
        return result.ToArray();
    }

    /// <summary>Wraps a payload in an OBU header (+ optional extension) and, when asked, a LEB128 size field.</summary>
    public static byte[] Obu(int type, byte[] payload, bool withSize = true, bool withExtension = false)
    {
        List<byte> obu = new List<byte>();
        obu.Add((byte)((type << 3) | (withExtension ? 0x04 : 0) | (withSize ? 0x02 : 0)));
        if (withExtension) obu.Add(0x00);
        if (withSize) obu.AddRange(Leb128(payload.Length));
        obu.AddRange(payload);
        return obu.ToArray();
    }
}
