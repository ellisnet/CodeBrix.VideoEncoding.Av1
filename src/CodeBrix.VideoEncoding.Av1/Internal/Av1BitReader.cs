using System;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// Reads the AV1 bitstream's fixed-width (<c>f(n)</c>), <c>uvlc()</c> and <c>leb128()</c> fields, most significant
/// bit first, from a byte span.
/// </summary>
internal ref struct Av1BitReader
{
    private readonly ReadOnlySpan<byte> data;
    private long position;

    /// <summary>Starts reading at the first bit of <paramref name="data" />.</summary>
    /// <param name="data">The bytes.</param>
    public Av1BitReader(ReadOnlySpan<byte> data)
    {
        this.data = data;
        position = 0;
    }

    /// <summary>The number of bits read so far.</summary>
    public long BitPosition => position;

    /// <summary>Reads an unsigned <c>f(n)</c> field.</summary>
    /// <param name="bits">0 to 32.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The data ends first.</exception>
    public uint ReadBits(int bits)
    {
        if (bits < 0 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits));

        uint value = 0;
        for (int i = 0; i < bits; i++)
        {
            long byteIndex = position >> 3;
            if (byteIndex >= data.Length) throw new FormatException("The AV1 sequence header ends early.");
            int bit = (data[(int)byteIndex] >> (7 - (int)(position & 7))) & 1;
            value = (value << 1) | (uint)bit;
            position++;
        }

        return value;
    }

    /// <summary>Reads a one-bit flag.</summary>
    /// <returns>True for 1.</returns>
    public bool ReadFlag() => ReadBits(1) == 1;

    /// <summary>Reads a <c>uvlc()</c> field.</summary>
    /// <returns>The value (2^32 - 1 for an over-long code, as the specification says).</returns>
    public uint ReadUvlc()
    {
        int leadingZeros = 0;
        while (!ReadFlag())
        {
            leadingZeros++;
            if (leadingZeros >= 32) return uint.MaxValue;
        }

        if (leadingZeros == 0) return 0;
        uint value = ReadBits(leadingZeros);
        return value + ((1u << leadingZeros) - 1);
    }

    /// <summary>Reads a <c>leb128()</c> field. The reader must be byte-aligned.</summary>
    /// <returns>The value.</returns>
    public ulong ReadLeb128()
    {
        ulong value = 0;
        for (int i = 0; i < 8; i++)
        {
            uint b = ReadBits(8);
            value |= (ulong)(b & 0x7F) << (i * 7);
            if ((b & 0x80) == 0) return value;
        }

        return value;
    }
}
