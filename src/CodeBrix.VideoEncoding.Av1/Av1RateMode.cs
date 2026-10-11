namespace CodeBrix.VideoEncoding.Av1;

/// <summary>How the encoder spends bits (<see cref="Av1EncoderOptions.RateMode" />).</summary>
public enum Av1RateMode
{
    /// <summary>
    /// Derived from the request's <c>Quality</c>: a constant-quality request (or the default) gives
    /// <see cref="ConstantQuality" />; a target-bitrate request gives <see cref="VariableBitrate" />. The default.
    /// </summary>
    Automatic = 0,

    /// <summary>Constant quality: every frame at the quality level, letting the size vary. Needs no bitrate.</summary>
    ConstantQuality = 1,

    /// <summary>Constant quality, but never above a bitrate cap. Needs a bitrate.</summary>
    ConstrainedQuality = 2,

    /// <summary>Aim for an average bitrate, letting individual frames vary. Needs a bitrate.</summary>
    VariableBitrate = 3,

    /// <summary>Hold the bitrate as steady as possible (streaming). Needs a bitrate.</summary>
    ConstantBitrate = 4
}
