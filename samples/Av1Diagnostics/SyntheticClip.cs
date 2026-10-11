using System;
using CodeBrix.VideoProcessing.Core.Frames;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>
/// One picture of the synthetic clip, generated in code (no assets): a smooth colour gradient with a soft-edged
/// disc that moves across it, and the matching alpha matte (the same disc, full range, soft edges). Planes are
/// tightly packed, 16-bit little-endian words for 10 bits, so they can be compared directly with decoder output.
/// </summary>
internal sealed class SyntheticPicture
{
    public byte[] Y { get; init; }

    public byte[] U { get; init; }

    public byte[] V { get; init; }

    public byte[] Alpha { get; init; }

    public int BitDepth { get; init; }

    /// <summary>The colour picture as a Core I420 frame over <see cref="Y" />, <see cref="U" />, <see cref="V" />.</summary>
    public VideoFrameData ColourFrame(int width, int height, int index) =>
        VideoFrameData.CreateI420(width, height,
            VideoPlane.FromArray(Y, width * Bytes, height),
            VideoPlane.FromArray(U, (width / 2) * Bytes, height / 2),
            VideoPlane.FromArray(V, (width / 2) * Bytes, height / 2),
            TimeSpan.Zero, index, default, BitDepth);

    /// <summary>The alpha matte as a Core Gray frame over <see cref="Alpha" />.</summary>
    public VideoFrameData AlphaFrame(int width, int height, int index) =>
        VideoFrameData.CreateGray(width, height, VideoPlane.FromArray(Alpha, width * Bytes, height), TimeSpan.Zero, index, default, BitDepth);

    private int Bytes => BitDepth > 8 ? 2 : 1;
}

/// <summary>Generates <see cref="SyntheticPicture" />s deterministically, so the source can be rebuilt for comparison.</summary>
internal static class SyntheticClip
{
    /// <summary>Makes picture <paramref name="index" />; colour is limited range, alpha full range.</summary>
    public static SyntheticPicture Create(int width, int height, int bitDepth, int index)
    {
        int bytes = bitDepth > 8 ? 2 : 1;
        int max = (1 << bitDepth) - 1;
        double scale = max / 255.0; // limited-range code points are defined at 8 bits and scale up
        int chromaWidth = width / 2;
        int chromaHeight = height / 2;

        // The disc: moves left to right and bobs up and down; 12-pixel soft edge (smoothstep).
        double cx = width * 0.15 + index * (width * 0.7 / 60.0);
        double cy = height * 0.5 + height * 0.22 * Math.Sin(index / 9.0);
        double radius = height * 0.22;
        const double edge = 12.0;

        double Coverage(double x, double y)
        {
            double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            double t = Math.Clamp((radius + edge / 2 - d) / edge, 0, 1);
            return t * t * (3 - 2 * t);
        }

        byte[] y = new byte[width * height * bytes];
        byte[] alpha = new byte[width * height * bytes];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                double a = Coverage(col + 0.5, row + 0.5);
                double background = 0.25 + 0.45 * ((double)col / width) + 0.1 * Math.Sin((row + index * 2) / 23.0);
                double disc = 0.8 + 0.1 * Math.Cos((col - cx) / 11.0);
                double luma = background * (1 - a) + disc * a;
                Put(y, row * width + col, bytes, (int)Math.Round((16 + 219 * luma) * scale));
                Put(alpha, row * width + col, bytes, (int)Math.Round(a * max));
            }
        }

        byte[] u = new byte[chromaWidth * chromaHeight * bytes];
        byte[] v = new byte[chromaWidth * chromaHeight * bytes];
        for (int row = 0; row < chromaHeight; row++)
        {
            for (int col = 0; col < chromaWidth; col++)
            {
                double a = Coverage(col * 2 + 1.0, row * 2 + 1.0);
                double bu = 0.5 + 0.12 * Math.Sin((col + index) / 17.0);
                double bv = 0.5 + 0.10 * Math.Cos((row - index) / 13.0);
                double cu = bu * (1 - a) + 0.30 * a; // the disc is orange-ish
                double cv = bv * (1 - a) + 0.72 * a;
                Put(u, row * chromaWidth + col, bytes, (int)Math.Round((16 + 224 * cu) * scale));
                Put(v, row * chromaWidth + col, bytes, (int)Math.Round((16 + 224 * cv) * scale));
            }
        }

        return new SyntheticPicture { Y = y, U = u, V = v, Alpha = alpha, BitDepth = bitDepth };
    }

    /// <summary>Reads sample <paramref name="sample" /> of a tightly packed plane.</summary>
    public static int Get(byte[] plane, int sample, int bytes) =>
        bytes == 1 ? plane[sample] : plane[sample * 2] | (plane[sample * 2 + 1] << 8);

    /// <summary>Sum of squared differences between two equally sized planes.</summary>
    public static double SquaredError(byte[] expected, byte[] actual, int bytes)
    {
        if (expected.Length != actual.Length) throw new ArgumentException($"plane sizes differ: {expected.Length} vs {actual.Length}");
        double sum = 0;
        int samples = expected.Length / bytes;
        for (int i = 0; i < samples; i++)
        {
            double d = Get(expected, i, bytes) - Get(actual, i, bytes);
            sum += d * d;
        }

        return sum;
    }

    /// <summary>PSNR in dB from a total squared error over <paramref name="samples" /> samples.</summary>
    public static double Psnr(double squaredError, long samples, int bitDepth)
    {
        if (squaredError == 0) return double.PositiveInfinity;
        double max = (1 << bitDepth) - 1;
        return 10 * Math.Log10(max * max / (squaredError / samples));
    }

    private static void Put(byte[] plane, int sample, int bytes, int value)
    {
        if (bytes == 1)
        {
            plane[sample] = (byte)value;
            return;
        }

        plane[sample * 2] = (byte)(value & 0xFF);
        plane[sample * 2 + 1] = (byte)(value >> 8);
    }
}
