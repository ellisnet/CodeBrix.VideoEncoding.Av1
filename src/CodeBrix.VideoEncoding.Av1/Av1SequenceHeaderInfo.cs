namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// What an AV1 sequence header says about a stream - read back from the header the encoder produced (see
/// <see cref="Av1Encoder.SequenceHeader" />). The values are those of the AV1 specification's sequence header
/// and colour config syntax, and they are what the av1C configuration record is built from.
/// </summary>
public sealed class Av1SequenceHeaderInfo
{
    /// <summary>
    /// <c>seq_profile</c>: 0 (Main - 4:2:0 and monochrome at 8 or 10 bits), 1 (High - 4:4:4 at 8 or 10 bits) or 2
    /// (Professional - 12 bits, and 4:2:2).
    /// </summary>
    public int Profile { get; init; }

    /// <summary><c>still_picture</c>: true for a stream that holds a single picture.</summary>
    public bool StillPicture { get; init; }

    /// <summary><c>reduced_still_picture_header</c>.</summary>
    public bool ReducedStillPictureHeader { get; init; }

    /// <summary>The number of operating points (<c>operating_points_cnt_minus_1 + 1</c>).</summary>
    public int OperatingPointCount { get; init; }

    /// <summary>
    /// <c>seq_level_idx[0]</c>, the level of operating point 0: 0 = level 2.0, 1 = 2.1, ... 4 = 3.0, 8 = 4.0,
    /// 12 = 5.0 ... (major = 2 + index / 4, minor = index % 4); 31 = no level constraint.
    /// </summary>
    public int LevelIndex { get; init; }

    /// <summary><c>seq_tier[0]</c>: 0 = Main tier, 1 = High tier.</summary>
    public int Tier { get; init; }

    /// <summary>Bits per sample: 8, 10 or 12.</summary>
    public int BitDepth { get; init; }

    /// <summary><c>mono_chrome</c>: true for a luma-only stream.</summary>
    public bool Monochrome { get; init; }

    /// <summary><c>subsampling_x</c>: 1 when chroma is half width.</summary>
    public int SubsamplingX { get; init; }

    /// <summary><c>subsampling_y</c>: 1 when chroma is half height.</summary>
    public int SubsamplingY { get; init; }

    /// <summary><c>chroma_sample_position</c>: 0 unknown, 1 vertical, 2 co-located (4:2:0 only).</summary>
    public int ChromaSamplePosition { get; init; }

    /// <summary><c>color_description_present_flag</c>.</summary>
    public bool ColorDescriptionPresent { get; init; }

    /// <summary><c>color_primaries</c> (2 = unspecified when no colour description is present).</summary>
    public int ColorPrimaries { get; init; }

    /// <summary><c>transfer_characteristics</c> (2 = unspecified when no colour description is present).</summary>
    public int TransferCharacteristics { get; init; }

    /// <summary><c>matrix_coefficients</c> (2 = unspecified when no colour description is present).</summary>
    public int MatrixCoefficients { get; init; }

    /// <summary><c>color_range</c>: true for full range, false for limited (studio) range.</summary>
    public bool FullRange { get; init; }

    /// <summary>The largest frame width the stream may use (<c>max_frame_width_minus_1 + 1</c>).</summary>
    public int MaxFrameWidth { get; init; }

    /// <summary>The largest frame height the stream may use (<c>max_frame_height_minus_1 + 1</c>).</summary>
    public int MaxFrameHeight { get; init; }

    /// <summary><c>film_grain_params_present</c>.</summary>
    public bool FilmGrainParamsPresent { get; init; }

    /// <summary>The level as text, for example "4.0"; "unconstrained" for index 31.</summary>
    public string LevelName => LevelIndex == 31 ? "unconstrained" : $"{2 + (LevelIndex >> 2)}.{LevelIndex & 3}";

    /// <inheritdoc />
    public override string ToString() =>
        $"profile {Profile}, level {LevelName}{(Tier == 1 ? " high tier" : string.Empty)}, {BitDepth}-bit, "
        + (Monochrome ? "monochrome" : $"subsampling {SubsamplingX},{SubsamplingY}")
        + $", {MaxFrameWidth}x{MaxFrameHeight}, {(FullRange ? "full" : "limited")} range";
}
