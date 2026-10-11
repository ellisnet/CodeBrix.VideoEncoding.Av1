namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>
/// Builds an AV1 sequence_header_obu() payload field by field, following the specification's syntax, so the
/// reader can be tested on headers no encoder in the test run produces (timing info, several operating points,
/// reduced still-picture headers, 12-bit 4:2:2 ...).
/// </summary>
internal sealed class SequenceHeaderBuilder
{
    public int Profile { get; set; }
    public bool StillPicture { get; set; }
    public bool Reduced { get; set; }
    public bool TimingInfo { get; set; }
    public bool EqualPictureInterval { get; set; }
    public bool DecoderModelInfo { get; set; }
    public bool InitialDisplayDelay { get; set; }
    public int[] Levels { get; set; } = { 8 };
    public int[] Tiers { get; set; } = { 0 };
    public int MaxWidth { get; set; } = 320;
    public int MaxHeight { get; set; } = 180;
    public bool FrameIdNumbers { get; set; }
    public bool EnableOrderHint { get; set; } = true;
    public bool ChooseScreenContentTools { get; set; } = true;
    public bool ForceScreenContentTools { get; set; }
    public bool ChooseIntegerMv { get; set; } = true;
    public bool HighBitDepth { get; set; }
    public bool TwelveBit { get; set; }
    public bool Monochrome { get; set; }
    public bool ColorDescription { get; set; }
    public int Primaries { get; set; } = 2;
    public int Transfer { get; set; } = 2;
    public int Matrix { get; set; } = 2;
    public bool FullRange { get; set; }
    public int SubsamplingX { get; set; } = 1;
    public int SubsamplingY { get; set; } = 1;
    public int ChromaSamplePosition { get; set; }
    public bool FilmGrain { get; set; }

    public byte[] Payload()
    {
        BitWriter w = new BitWriter();
        w.Bits(Profile, 3).Flag(StillPicture).Flag(Reduced);

        if (Reduced)
        {
            w.Bits(Levels[0], 5);
        }
        else
        {
            w.Flag(TimingInfo);
            if (TimingInfo)
            {
                w.Bits(1001, 32).Bits(30000, 32).Flag(EqualPictureInterval);
                if (EqualPictureInterval) w.Uvlc(0);
                w.Flag(DecoderModelInfo);
                if (DecoderModelInfo) w.Bits(9, 5).Bits(1000, 32).Bits(4, 5).Bits(4, 5);
            }

            w.Flag(InitialDisplayDelay);
            w.Bits(Levels.Length - 1, 5);
            for (int i = 0; i < Levels.Length; i++)
            {
                w.Bits(i == 0 ? 0 : 0x101, 12).Bits(Levels[i], 5);
                if (Levels[i] > 7) w.Bits(Tiers[i], 1);
                if (TimingInfo && DecoderModelInfo)
                {
                    w.Flag(true).Bits(123, 10).Bits(456, 10).Flag(false);
                }

                if (InitialDisplayDelay) w.Flag(true).Bits(9, 4);
            }
        }

        w.Bits(15, 4).Bits(15, 4).Bits(MaxWidth - 1, 16).Bits(MaxHeight - 1, 16);
        if (!Reduced)
        {
            w.Flag(FrameIdNumbers);
            if (FrameIdNumbers) w.Bits(5, 4).Bits(2, 3);
        }

        w.Flag(false).Flag(true).Flag(true);
        if (!Reduced)
        {
            w.Flag(true).Flag(true).Flag(false).Flag(true).Flag(EnableOrderHint);
            if (EnableOrderHint) w.Flag(true).Flag(true);
            w.Flag(ChooseScreenContentTools);
            int force = ChooseScreenContentTools ? 2 : (ForceScreenContentTools ? 1 : 0);
            if (!ChooseScreenContentTools) w.Flag(ForceScreenContentTools);
            if (force > 0)
            {
                w.Flag(ChooseIntegerMv);
                if (!ChooseIntegerMv) w.Flag(false);
            }

            if (EnableOrderHint) w.Bits(6, 3);
        }

        w.Flag(false).Flag(true).Flag(true);

        w.Flag(HighBitDepth);
        if (Profile == 2 && HighBitDepth) w.Flag(TwelveBit);
        if (Profile != 1) w.Flag(Monochrome);
        w.Flag(ColorDescription);
        if (ColorDescription) w.Bits(Primaries, 8).Bits(Transfer, 8).Bits(Matrix, 8);

        if (Monochrome)
        {
            w.Flag(FullRange);
        }
        else if (Primaries == 1 && Transfer == 13 && Matrix == 0)
        {
            w.Flag(false);
        }
        else
        {
            w.Flag(FullRange);
            int sx;
            int sy;
            if (Profile == 0)
            {
                sx = 1;
                sy = 1;
            }
            else if (Profile == 1)
            {
                sx = 0;
                sy = 0;
            }
            else if (HighBitDepth && TwelveBit)
            {
                sx = SubsamplingX;
                sy = sx == 1 ? SubsamplingY : 0;
                w.Bits(sx, 1);
                if (sx == 1) w.Bits(sy, 1);
            }
            else
            {
                sx = 1;
                sy = 0;
            }

            if (sx == 1 && sy == 1) w.Bits(ChromaSamplePosition, 2);
            w.Flag(false);
        }

        w.Flag(FilmGrain);
        w.TrailingBits();
        return w.ToArray();
    }

    public byte[] Obu(bool withSize = true, bool withExtension = false) => BitWriter.Obu(1, Payload(), withSize, withExtension);
}
