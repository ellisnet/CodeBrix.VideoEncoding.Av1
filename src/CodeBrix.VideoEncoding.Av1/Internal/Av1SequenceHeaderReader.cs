using System;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// Parses an AV1 sequence header OBU (AV1 specification sections 5.3 and 5.5) far enough to describe the
/// stream: profile, still picture, operating point 0's level and tier, and the whole colour config.
/// </summary>
internal static class Av1SequenceHeaderReader
{
    /// <summary>The <c>obu_type</c> of a sequence header.</summary>
    public const int ObuSequenceHeader = 1;

    /// <summary>Parses a complete sequence header OBU: OBU header, optional extension and size, then the payload.</summary>
    /// <param name="obu">The OBU bytes as they appear in the bitstream.</param>
    /// <returns>What the header says.</returns>
    /// <exception cref="FormatException">The bytes are not a well-formed sequence header OBU.</exception>
    public static Av1SequenceHeaderInfo ReadObu(ReadOnlySpan<byte> obu)
    {
        if (obu.Length < 2) throw new FormatException("An AV1 sequence header OBU is at least two bytes long.");

        Av1BitReader header = new Av1BitReader(obu);
        if (header.ReadFlag()) throw new FormatException("The OBU's forbidden bit is set.");
        int type = (int)header.ReadBits(4);
        bool extension = header.ReadFlag();
        bool hasSize = header.ReadFlag();
        header.ReadBits(1);

        if (type != ObuSequenceHeader) throw new FormatException($"The OBU is of type {type}, not a sequence header ({ObuSequenceHeader}).");
        if (extension) header.ReadBits(8);

        int payloadStart;
        int payloadLength;
        if (hasSize)
        {
            ulong size = header.ReadLeb128();
            payloadStart = (int)(header.BitPosition >> 3);
            if (size > (ulong)(obu.Length - payloadStart)) throw new FormatException("The OBU's size field is larger than the data.");
            payloadLength = (int)size;
        }
        else
        {
            payloadStart = (int)(header.BitPosition >> 3);
            payloadLength = obu.Length - payloadStart;
        }

        return ReadPayload(obu.Slice(payloadStart, payloadLength));
    }

    /// <summary>Parses the payload of a sequence header OBU (<c>sequence_header_obu()</c>).</summary>
    /// <param name="payload">The payload bytes.</param>
    /// <returns>What the header says.</returns>
    /// <exception cref="FormatException">The payload ends early or is malformed.</exception>
    public static Av1SequenceHeaderInfo ReadPayload(ReadOnlySpan<byte> payload)
    {
        Av1BitReader r = new Av1BitReader(payload);

        int profile = (int)r.ReadBits(3);
        if (profile > 2) throw new FormatException($"seq_profile {profile} is reserved.");
        bool stillPicture = r.ReadFlag();
        bool reduced = r.ReadFlag();

        int operatingPoints = 1;
        int level0 = 0;
        int tier0 = 0;

        if (reduced)
        {
            level0 = (int)r.ReadBits(5);
        }
        else
        {
            bool decoderModelInfoPresent = false;
            int bufferDelayLength = 0;

            if (r.ReadFlag())
            {
                // timing_info()
                r.ReadBits(32);
                r.ReadBits(32);
                if (r.ReadFlag()) r.ReadUvlc();

                decoderModelInfoPresent = r.ReadFlag();
                if (decoderModelInfoPresent)
                {
                    bufferDelayLength = (int)r.ReadBits(5) + 1;
                    r.ReadBits(32);
                    r.ReadBits(5);
                    r.ReadBits(5);
                }
            }

            bool initialDisplayDelayPresent = r.ReadFlag();
            operatingPoints = (int)r.ReadBits(5) + 1;
            for (int i = 0; i < operatingPoints; i++)
            {
                r.ReadBits(12);
                int level = (int)r.ReadBits(5);
                int tier = level > 7 ? (int)r.ReadBits(1) : 0;
                if (decoderModelInfoPresent && r.ReadFlag())
                {
                    r.ReadBits(bufferDelayLength);
                    r.ReadBits(bufferDelayLength);
                    r.ReadBits(1);
                }

                if (initialDisplayDelayPresent && r.ReadFlag()) r.ReadBits(4);

                if (i == 0)
                {
                    level0 = level;
                    tier0 = tier;
                }
            }
        }

        int widthBits = (int)r.ReadBits(4) + 1;
        int heightBits = (int)r.ReadBits(4) + 1;
        int maxWidth = (int)r.ReadBits(widthBits) + 1;
        int maxHeight = (int)r.ReadBits(heightBits) + 1;

        bool frameIdNumbersPresent = !reduced && r.ReadFlag();
        if (frameIdNumbersPresent)
        {
            r.ReadBits(4);
            r.ReadBits(3);
        }

        r.ReadBits(3); // use_128x128_superblock, enable_filter_intra, enable_intra_edge_filter

        if (!reduced)
        {
            r.ReadBits(4); // enable_interintra_compound, enable_masked_compound, enable_warped_motion, enable_dual_filter
            bool enableOrderHint = r.ReadFlag();
            if (enableOrderHint) r.ReadBits(2); // enable_jnt_comp, enable_ref_frame_mvs

            int forceScreenContentTools = r.ReadFlag() ? 2 : (int)r.ReadBits(1);
            if (forceScreenContentTools > 0)
            {
                if (!r.ReadFlag()) r.ReadBits(1); // seq_choose_integer_mv / seq_force_integer_mv
            }

            if (enableOrderHint) r.ReadBits(3);
        }

        r.ReadBits(3); // enable_superres, enable_cdef, enable_restoration

        // color_config()
        bool highBitDepth = r.ReadFlag();
        int bitDepth;
        if (profile == 2 && highBitDepth) bitDepth = r.ReadFlag() ? 12 : 10;
        else bitDepth = highBitDepth ? 10 : 8;

        bool monochrome = profile != 1 && r.ReadFlag();
        bool colorDescription = r.ReadFlag();
        int primaries = 2;
        int transfer = 2;
        int matrix = 2;
        if (colorDescription)
        {
            primaries = (int)r.ReadBits(8);
            transfer = (int)r.ReadBits(8);
            matrix = (int)r.ReadBits(8);
        }

        bool fullRange;
        int subsamplingX;
        int subsamplingY;
        int chromaPosition = 0;

        if (monochrome)
        {
            fullRange = r.ReadFlag();
            subsamplingX = 1;
            subsamplingY = 1;
        }
        else if (primaries == 1 && transfer == 13 && matrix == 0)
        {
            fullRange = true;
            subsamplingX = 0;
            subsamplingY = 0;
            r.ReadBits(1); // separate_uv_delta_q
        }
        else
        {
            fullRange = r.ReadFlag();
            if (profile == 0)
            {
                subsamplingX = 1;
                subsamplingY = 1;
            }
            else if (profile == 1)
            {
                subsamplingX = 0;
                subsamplingY = 0;
            }
            else if (bitDepth == 12)
            {
                subsamplingX = (int)r.ReadBits(1);
                subsamplingY = subsamplingX == 1 ? (int)r.ReadBits(1) : 0;
            }
            else
            {
                subsamplingX = 1;
                subsamplingY = 0;
            }

            if (subsamplingX == 1 && subsamplingY == 1) chromaPosition = (int)r.ReadBits(2);
            r.ReadBits(1); // separate_uv_delta_q
        }

        bool filmGrain = r.ReadFlag();

        return new Av1SequenceHeaderInfo
        {
            Profile = profile,
            StillPicture = stillPicture,
            ReducedStillPictureHeader = reduced,
            OperatingPointCount = operatingPoints,
            LevelIndex = level0,
            Tier = tier0,
            BitDepth = bitDepth,
            Monochrome = monochrome,
            SubsamplingX = subsamplingX,
            SubsamplingY = subsamplingY,
            ChromaSamplePosition = chromaPosition,
            ColorDescriptionPresent = colorDescription,
            ColorPrimaries = primaries,
            TransferCharacteristics = transfer,
            MatrixCoefficients = matrix,
            FullRange = fullRange,
            MaxFrameWidth = maxWidth,
            MaxFrameHeight = maxHeight,
            FilmGrainParamsPresent = filmGrain,
        };
    }
}
