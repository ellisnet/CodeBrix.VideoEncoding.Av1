namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>One picture decoded by the independent dav1d decoder, copied out of the decoder's pool into plain arrays.</summary>
internal sealed class DecodedPicture
{
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>"Gray", "I420", "I422" or "I444".</summary>
    public string Layout { get; init; }

    public int BitDepth { get; init; }

    public bool IsKeyFrame { get; init; }

    public byte[] Y { get; init; }

    public byte[] U { get; init; }

    public byte[] V { get; init; }

    public int Primaries { get; init; }

    public int Transfer { get; init; }

    public int Matrix { get; init; }

    /// <summary>0 unspecified, 1 limited, 2 full.</summary>
    public int Range { get; init; }
}
