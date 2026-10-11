using System;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// Builds the AV1CodecConfigurationRecord ("av1C", AV1 Codec ISO Media File Format Binding section 2.3) a
/// container stores as the track's codec-private data.
/// </summary>
/// <remarks>
/// The record is four bytes followed by the configuration OBUs, which here are exactly the sequence header OBU:
/// <code>
/// byte 0  marker (1) = 1, version (7) = 1                                   -> 0x81
/// byte 1  seq_profile (3), seq_level_idx_0 (5)
/// byte 2  seq_tier_0 (1), high_bitdepth (1), twelve_bit (1), monochrome (1),
///         chroma_subsampling_x (1), chroma_subsampling_y (1), chroma_sample_position (2)
/// byte 3  reserved (3) = 0, initial_presentation_delay_present (1) = 0, reserved (4) = 0   -> 0x00
/// then    configOBUs = the sequence header OBU, with its size field, as the encoder produced it
/// </code>
/// high_bitdepth is 1 for 10 and 12 bits, twelve_bit is 1 for 12 bits. Every field comes from the parsed
/// sequence header, so the record cannot disagree with the OBU it carries.
/// </remarks>
internal static class Av1ConfigurationRecord
{
    /// <summary>The first byte: marker 1, version 1.</summary>
    public const byte MarkerAndVersion = 0x81;

    /// <summary>The length of the fixed part, before the configuration OBUs.</summary>
    public const int HeaderLength = 4;

    /// <summary>Builds the record.</summary>
    /// <param name="info">The parsed sequence header.</param>
    /// <param name="sequenceHeaderObu">The sequence header OBU bytes the record carries.</param>
    /// <returns>A new array holding the record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="info" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="sequenceHeaderObu" /> is empty.</exception>
    public static byte[] Build(Av1SequenceHeaderInfo info, ReadOnlySpan<byte> sequenceHeaderObu)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));
        if (sequenceHeaderObu.IsEmpty) throw new ArgumentException("The sequence header OBU is empty.", nameof(sequenceHeaderObu));

        byte[] record = new byte[HeaderLength + sequenceHeaderObu.Length];
        record[0] = MarkerAndVersion;
        record[1] = (byte)(((info.Profile & 0x7) << 5) | (info.LevelIndex & 0x1F));
        record[2] = (byte)(
            ((info.Tier & 1) << 7)
            | ((info.BitDepth > 8 ? 1 : 0) << 6)
            | ((info.BitDepth == 12 ? 1 : 0) << 5)
            | ((info.Monochrome ? 1 : 0) << 4)
            | ((info.SubsamplingX & 1) << 3)
            | ((info.SubsamplingY & 1) << 2)
            | (info.ChromaSamplePosition & 0x3));
        record[3] = 0;
        sequenceHeaderObu.CopyTo(record.AsSpan(HeaderLength));
        return record;
    }
}
