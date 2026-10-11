namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>The constants of <c>codebrix_av1.h</c>, by their C names.</summary>
internal static class Av1Constants
{
    /// <summary><c>CBA_LAYOUT_I420</c>.</summary>
    public const int LayoutI420 = 0;

    /// <summary><c>CBA_LAYOUT_I444</c>.</summary>
    public const int LayoutI444 = 1;

    /// <summary><c>CBA_LAYOUT_MONOCHROME</c>.</summary>
    public const int LayoutMonochrome = 2;

    /// <summary><c>CBA_RATE_CONSTANT_QUALITY</c>.</summary>
    public const int RateConstantQuality = 0;

    /// <summary><c>CBA_RATE_CONSTRAINED_QUALITY</c>.</summary>
    public const int RateConstrainedQuality = 1;

    /// <summary><c>CBA_RATE_VARIABLE_BITRATE</c>.</summary>
    public const int RateVariableBitrate = 2;

    /// <summary><c>CBA_RATE_CONSTANT_BITRATE</c>.</summary>
    public const int RateConstantBitrate = 3;

    /// <summary><c>CBA_OPT_CPU_USED</c>.</summary>
    public const int OptCpuUsed = 1;

    /// <summary><c>CBA_OPT_RATE_MODE</c>.</summary>
    public const int OptRateMode = 2;

    /// <summary><c>CBA_OPT_QUALITY</c> (cq-level 0..63).</summary>
    public const int OptQuality = 3;

    /// <summary><c>CBA_OPT_TARGET_BITRATE_KBPS</c>.</summary>
    public const int OptTargetBitrateKbps = 4;

    /// <summary><c>CBA_OPT_LOSSLESS</c>.</summary>
    public const int OptLossless = 5;

    /// <summary><c>CBA_OPT_THREADS</c>.</summary>
    public const int OptThreads = 6;

    /// <summary><c>CBA_OPT_ROW_MULTITHREADING</c>.</summary>
    public const int OptRowMultithreading = 7;

    /// <summary><c>CBA_OPT_TILE_COLUMNS_LOG2</c>.</summary>
    public const int OptTileColumnsLog2 = 8;

    /// <summary><c>CBA_OPT_TILE_ROWS_LOG2</c>.</summary>
    public const int OptTileRowsLog2 = 9;

    /// <summary><c>CBA_OPT_LAG_IN_FRAMES</c>.</summary>
    public const int OptLagInFrames = 10;

    /// <summary><c>CBA_OPT_KEY_FRAME_MAX_DISTANCE</c>.</summary>
    public const int OptKeyFrameMaxDistance = 11;

    /// <summary><c>CBA_OPT_COLOR_PRIMARIES</c>.</summary>
    public const int OptColorPrimaries = 12;

    /// <summary><c>CBA_OPT_TRANSFER_CHARACTERISTICS</c>.</summary>
    public const int OptTransferCharacteristics = 13;

    /// <summary><c>CBA_OPT_MATRIX_COEFFICIENTS</c>.</summary>
    public const int OptMatrixCoefficients = 14;

    /// <summary><c>CBA_OPT_COLOR_RANGE</c>.</summary>
    public const int OptColorRange = 15;

    /// <summary><c>CBA_OPT_CHROMA_SAMPLE_POSITION</c>.</summary>
    public const int OptChromaSamplePosition = 16;

    /// <summary><c>CBA_OPT_AUTOMATIC_KEY_FRAMES</c>.</summary>
    public const int OptAutomaticKeyFrames = 17;

    /// <summary>The largest width and height AV1 allows.</summary>
    public const int MaxDimension = 65536;

    /// <summary>The largest <c>CBA_OPT_THREADS</c>.</summary>
    public const int MaxThreads = 64;

    /// <summary>The thread count used when the request leaves it to the encoder: the processor count, capped here.</summary>
    public const int DefaultThreadCap = 16;

    /// <summary>The largest <c>CBA_OPT_KEY_FRAME_MAX_DISTANCE</c>.</summary>
    public const int MaxKeyFrameDistance = 65535;

    /// <summary>The largest cq-level.</summary>
    public const int MaxCqLevel = 63;
}
