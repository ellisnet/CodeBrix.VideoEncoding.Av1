namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// Where the chroma samples of a 4:2:0 picture sit relative to the luma samples
/// (<see cref="Av1EncoderOptions.ChromaSamplePosition" />). A tag written into the stream; nothing is resampled.
/// Ignored for 4:4:4 and monochrome streams.
/// </summary>
public enum Av1ChromaSamplePosition
{
    /// <summary>Not signalled - the default.</summary>
    Unknown = 0,

    /// <summary>Horizontally co-sited with the left luma sample, vertically between two rows ("left", MPEG-2 style).</summary>
    Vertical = 1,

    /// <summary>Co-sited with the top-left luma sample.</summary>
    Colocated = 2
}
