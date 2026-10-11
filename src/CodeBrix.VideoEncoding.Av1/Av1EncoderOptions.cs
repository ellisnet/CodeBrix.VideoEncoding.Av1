using System;

namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// AV1-specific settings for an <see cref="Av1Encoder" />, on top of the codec-neutral
/// <c>VideoEncoderRequest</c>. Every value has a "let the encoder choose" default, so
/// <c>new Av1EncoderOptions()</c> is a good starting point.
/// </summary>
/// <remarks>
/// <para>
/// The encoder takes its own copy when it is created, so an options object may be changed and reused
/// afterwards. <see cref="Validate" /> checks the ranges; the encoder calls it.
/// </para>
/// <para>
/// Anything not named here can still be set through the request's <c>CodecOptions</c> dictionary, which passes
/// libaom option names and values (the names its command-line encoder accepts, without the leading dashes -
/// for example <c>"tune" = "psnr"</c>, <c>"sharpness" = "2"</c>, <c>"enable-tpl-model" = "0"</c>) straight to the
/// encoder.
/// </para>
/// </remarks>
public sealed class Av1EncoderOptions
{
    /// <summary>The value of the integer settings that means "the encoder's default".</summary>
    public const int Default = -1;

    /// <summary>The largest <see cref="CpuUsed" />.</summary>
    public const int MaxCpuUsed = 9;

    /// <summary>The largest <see cref="TileColumnsLog2" /> and <see cref="TileRowsLog2" />.</summary>
    public const int MaxTilesLog2 = 6;

    /// <summary>The largest <see cref="LagInFrames" />.</summary>
    public const int MaxLagInFrames = 48;

    /// <summary>The largest <see cref="TargetBitrateKbps" />.</summary>
    public const int MaxTargetBitrateKbps = 2000000;

    /// <summary>The usage profile. Default <see cref="Av1EncoderUsage.GoodQuality" />.</summary>
    public Av1EncoderUsage Usage { get; set; } = Av1EncoderUsage.GoodQuality;

    /// <summary>
    /// The speed / quality trade-off, libaom's "cpu-used": 0 (slowest, best) to 9 (fastest); <see cref="Default" />
    /// (-1, the default) lets the request's <c>Speed</c> decide, or the encoder when that is 0 too. Unlike
    /// <c>Speed</c>, this can ask for cpu-used 0. When both are set, this one wins.
    /// </summary>
    public int CpuUsed { get; set; } = Default;

    /// <summary>
    /// How bits are spent. Default <see cref="Av1RateMode.Automatic" />, which follows the request's
    /// <c>Quality</c>. The bitrate modes need a bitrate: <see cref="TargetBitrateKbps" />, or a request whose
    /// <c>Quality</c> is a target bitrate. Ignored for a lossless request.
    /// </summary>
    public Av1RateMode RateMode { get; set; } = Av1RateMode.Automatic;

    /// <summary>
    /// The bitrate, in kilobits per second, for the bitrate modes (the cap for
    /// <see cref="Av1RateMode.ConstrainedQuality" />); 0 (the default) takes it from a target-bitrate
    /// <c>Quality</c> in the request. 0 to <see cref="MaxTargetBitrateKbps" />.
    /// </summary>
    public int TargetBitrateKbps { get; set; }

    /// <summary>Row-based multithreading inside each tile. Default true (the encoder's own default).</summary>
    public bool RowMultithreading { get; set; } = true;

    /// <summary>
    /// The number of tile columns as a power of two (0 = one column, 1 = two, ... up to 6); <see cref="Default" />
    /// (the default) lets the encoder choose. More tiles let more threads work at once, at a small size cost.
    /// </summary>
    public int TileColumnsLog2 { get; set; } = Default;

    /// <summary>The number of tile rows as a power of two (0 to 6); <see cref="Default" /> (the default) lets the encoder choose.</summary>
    public int TileRowsLog2 { get; set; } = Default;

    /// <summary>
    /// How many frames the encoder may look ahead (0 to 48); <see cref="Default" /> (the default) takes the usage's
    /// value (a few dozen for <see cref="Av1EncoderUsage.GoodQuality" />, 0 for
    /// <see cref="Av1EncoderUsage.Realtime" />). Packets then come out that many frames late; 0 makes every
    /// frame's packet come out of the call that encodes it. IGNORED for a lossless request: the native encoder always
    /// encodes lossless streams without look-ahead, because with look-ahead the encoder's lossless mode is not
    /// bit-exact at 10 bits.
    /// </summary>
    public int LagInFrames { get; set; } = Default;

    /// <summary>
    /// The chroma sample position tag for 4:2:0 streams. Default <see cref="Av1ChromaSamplePosition.Unknown" />
    /// (not signalled). Ignored for 4:4:4 and monochrome streams.
    /// </summary>
    public Av1ChromaSamplePosition ChromaSamplePosition { get; set; } = Av1ChromaSamplePosition.Unknown;

    /// <summary>
    /// Whether the encoder may place key frames of its own (at scene cuts, and at the request's
    /// <c>MaxKeyFrameInterval</c>). Default true. False makes frame 0 and the frames encoded with
    /// <c>forceKeyFrame: true</c> the ONLY key frames, so key-frame positions never depend on picture content -
    /// what two streams that must stay in lock step (a colour stream and its alpha stream) need. A request with
    /// a <c>MaxKeyFrameInterval</c> is refused when this is false: force the key frames instead.
    /// </summary>
    public bool AutomaticKeyFrames { get; set; } = true;

    /// <summary>Checks every value against its range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range; the exception names it.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(Usage)) throw new ArgumentOutOfRangeException(nameof(Usage), Usage, "Unknown usage.");
        if (CpuUsed != Default && (CpuUsed < 0 || CpuUsed > MaxCpuUsed))
            throw new ArgumentOutOfRangeException(nameof(CpuUsed), CpuUsed, $"CpuUsed must be 0 to {MaxCpuUsed}, or -1 for the default.");
        if (!Enum.IsDefined(RateMode)) throw new ArgumentOutOfRangeException(nameof(RateMode), RateMode, "Unknown rate mode.");
        if (TargetBitrateKbps < 0 || TargetBitrateKbps > MaxTargetBitrateKbps)
            throw new ArgumentOutOfRangeException(nameof(TargetBitrateKbps), TargetBitrateKbps, $"TargetBitrateKbps must be 0 (from the request) to {MaxTargetBitrateKbps}.");
        if (TileColumnsLog2 != Default && (TileColumnsLog2 < 0 || TileColumnsLog2 > MaxTilesLog2))
            throw new ArgumentOutOfRangeException(nameof(TileColumnsLog2), TileColumnsLog2, $"TileColumnsLog2 must be 0 to {MaxTilesLog2}, or -1 for the default.");
        if (TileRowsLog2 != Default && (TileRowsLog2 < 0 || TileRowsLog2 > MaxTilesLog2))
            throw new ArgumentOutOfRangeException(nameof(TileRowsLog2), TileRowsLog2, $"TileRowsLog2 must be 0 to {MaxTilesLog2}, or -1 for the default.");
        if (LagInFrames != Default && (LagInFrames < 0 || LagInFrames > MaxLagInFrames))
            throw new ArgumentOutOfRangeException(nameof(LagInFrames), LagInFrames, $"LagInFrames must be 0 to {MaxLagInFrames}, or -1 for the default.");
        if (!Enum.IsDefined(ChromaSamplePosition))
            throw new ArgumentOutOfRangeException(nameof(ChromaSamplePosition), ChromaSamplePosition, "Unknown chroma sample position.");
    }

    /// <summary>Makes an independent copy.</summary>
    /// <returns>The copy.</returns>
    public Av1EncoderOptions Clone() => (Av1EncoderOptions)MemberwiseClone();
}
