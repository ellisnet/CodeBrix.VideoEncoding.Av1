namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// The encoder's usage profile (<see cref="Av1EncoderOptions.Usage" />). It chooses the encoder's internal
/// tuning and changes the defaults of several settings - the look-ahead in particular.
/// </summary>
public enum Av1EncoderUsage
{
    /// <summary>
    /// Offline encoding for the best quality per bit - the default, and the right choice for masters. Uses a
    /// look-ahead, so packets come out a number of frames after the frames that make them.
    /// </summary>
    GoodQuality = 0,

    /// <summary>Low-latency encoding: no look-ahead, every frame's packet comes out of the call that encodes it.</summary>
    Realtime = 1,

    /// <summary>Every frame is coded as a key frame (intra only): random access everywhere, at a large size cost.</summary>
    AllIntra = 2
}
