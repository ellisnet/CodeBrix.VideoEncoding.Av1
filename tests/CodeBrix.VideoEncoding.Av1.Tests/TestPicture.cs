using System;
using CodeBrix.VideoProcessing.Core.Frames;

namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>
/// A synthetic test picture: a smooth gradient with a bright square that moves a few pixels per frame, generated in
/// code (no assets), at any layout and bit depth Core supports, with its planes kept tightly packed for comparisons.
/// </summary>
internal sealed class TestPicture
{
    private TestPicture(VideoFrameData frame, byte[] y, byte[] u, byte[] v)
    {
        Frame = frame;
        Y = y;
        U = u;
        V = v;
    }

    public VideoFrameData Frame { get; }

    public byte[] Y { get; }

    public byte[] U { get; }

    public byte[] V { get; }

    /// <summary>Makes picture <paramref name="index" /> of a moving sequence.</summary>
    /// <param name="layout">I420, I444, Gray or Nv12.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="bitDepth">8, 10 or 12.</param>
    /// <param name="index">The picture's position in the sequence (moves the square).</param>
    /// <param name="stridePadding">Extra bytes at the end of every row of every plane (0 = tightly packed planes).</param>
    /// <param name="seed">Changes the content, so two sequences differ.</param>
    /// <param name="noise">Amplitude, as a fraction of full scale, of deterministic pseudo-random noise added to every
    /// sample (0 = smooth content). Noise makes encoders take the code paths smooth pictures never reach.</param>
    public static TestPicture Create(VideoPixelLayout layout, int width, int height, int bitDepth, int index, int stridePadding = 0, int seed = 0, double noise = 0)
    {
        int bytes = bitDepth > 8 ? 2 : 1;
        int max = (1 << bitDepth) - 1;
        int chromaWidth = layout == VideoPixelLayout.I444 ? width : (width + 1) / 2;
        int chromaHeight = layout == VideoPixelLayout.I444 ? height : (height + 1) / 2;

        byte[] y = new byte[width * height * bytes];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                double value = 0.25 + 0.5 * ((double)col / width) * ((double)(row + seed % 7) / height);
                int boxX = (index * 3 + seed * 11) % Math.Max(1, width - 16);
                int boxY = (index * 2 + seed * 5) % Math.Max(1, height - 16);
                if (col >= boxX && col < boxX + 16 && row >= boxY && row < boxY + 16) value = 0.85;
                value += noise * Noise(col, row, index, seed);
                Put(y, row * width + col, bytes, Clamp(value, max));
            }
        }

        byte[] u = null;
        byte[] v = null;
        if (layout != VideoPixelLayout.Gray)
        {
            u = new byte[chromaWidth * chromaHeight * bytes];
            v = new byte[chromaWidth * chromaHeight * bytes];
            for (int row = 0; row < chromaHeight; row++)
            {
                for (int col = 0; col < chromaWidth; col++)
                {
                    double cu = 0.5 + 0.2 * Math.Sin((col + index + seed) / 9.0);
                    double cv = 0.5 + 0.2 * Math.Cos((row + index) / 7.0);
                    cu += noise * Noise(col, row, index, seed + 101);
                    cv += noise * Noise(col, row, index, seed + 202);
                    Put(u, row * chromaWidth + col, bytes, Clamp(cu, max));
                    Put(v, row * chromaWidth + col, bytes, Clamp(cv, max));
                }
            }
        }

        TimeSpan timestamp = TimeSpan.FromMilliseconds(index * 40);
        VideoPlane yPlane = Plane(y, width * bytes, height, stridePadding);
        VideoFrameData frame;
        switch (layout)
        {
            case VideoPixelLayout.Gray:
                frame = VideoFrameData.CreateGray(width, height, yPlane, timestamp, index, default, bitDepth);
                break;
            case VideoPixelLayout.I444:
                frame = VideoFrameData.CreateI444(width, height, yPlane, Plane(u, chromaWidth * bytes, chromaHeight, stridePadding), Plane(v, chromaWidth * bytes, chromaHeight, stridePadding), timestamp, index, default, bitDepth);
                break;
            case VideoPixelLayout.Nv12:
                byte[] uv = new byte[2 * chromaWidth * chromaHeight * bytes];
                frame = VideoFrameData.CreateNv12(width, height, yPlane, VideoPlane.FromArray(uv, 2 * chromaWidth * bytes, chromaHeight), timestamp, index, default, bitDepth);
                break;
            default:
                frame = VideoFrameData.CreateI420(width, height, yPlane, Plane(u, chromaWidth * bytes, chromaHeight, stridePadding), Plane(v, chromaWidth * bytes, chromaHeight, stridePadding), timestamp, index, default, bitDepth);
                break;
        }

        return new TestPicture(frame, y, u, v);
    }

    /// <summary>
    /// Makes picture <paramref name="index" /> of a MATTE sequence: a soft-edged disc that moves across a flat
    /// background (0 outside, the maximum inside, a 12-pixel smoothstep edge) - the shape of a real overlay's alpha
    /// channel. For I420 / I444 the same disc is drawn into the chroma planes as well. Large flat areas with soft
    /// moving edges reach encoder paths smooth gradients and noise do not (this content exposed the encoder's
    /// non-exact lossless at its slowest speeds).
    /// </summary>
    public static TestPicture CreateMatte(VideoPixelLayout layout, int width, int height, int bitDepth, int index, int frames = 60)
    {
        int bytes = bitDepth > 8 ? 2 : 1;
        int max = (1 << bitDepth) - 1;
        double cx = width * 0.15 + index * (width * 0.7 / frames);
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
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++) Put(y, row * width + col, bytes, (int)Math.Round(Coverage(col + 0.5, row + 0.5) * max));
        }

        TimeSpan timestamp = TimeSpan.FromMilliseconds(index * 40);
        VideoPlane yPlane = VideoPlane.FromArray(y, width * bytes, height);
        if (layout == VideoPixelLayout.Gray)
            return new TestPicture(VideoFrameData.CreateGray(width, height, yPlane, timestamp, index, default, bitDepth), y, null, null);

        int shift = layout == VideoPixelLayout.I444 ? 0 : 1;
        int chromaWidth = (width + shift) >> shift;
        int chromaHeight = (height + shift) >> shift;
        byte[] u = new byte[chromaWidth * chromaHeight * bytes];
        byte[] v = new byte[chromaWidth * chromaHeight * bytes];
        for (int row = 0; row < chromaHeight; row++)
        {
            for (int col = 0; col < chromaWidth; col++)
            {
                double a = Coverage((col << shift) + 0.5 * (1 << shift), (row << shift) + 0.5 * (1 << shift));
                Put(u, row * chromaWidth + col, bytes, (int)Math.Round((0.5 - 0.2 * a) * max));
                Put(v, row * chromaWidth + col, bytes, (int)Math.Round((0.5 + 0.22 * a) * max));
            }
        }

        VideoPlane uPlane = VideoPlane.FromArray(u, chromaWidth * bytes, chromaHeight);
        VideoPlane vPlane = VideoPlane.FromArray(v, chromaWidth * bytes, chromaHeight);
        VideoFrameData frame = layout == VideoPixelLayout.I444
            ? VideoFrameData.CreateI444(width, height, yPlane, uPlane, vPlane, timestamp, index, default, bitDepth)
            : VideoFrameData.CreateI420(width, height, yPlane, uPlane, vPlane, timestamp, index, default, bitDepth);
        return new TestPicture(frame, y, u, v);
    }

    // Deterministic noise in [-0.5, 0.5): a hash of the position, frame and seed.
    private static double Noise(int x, int y, int frame, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(frame * 83492791) ^ (uint)(seed * 2654435761u);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65536.0 - 0.5;
    }

    private static int Clamp(double value, int max) => (int)Math.Round(Math.Clamp(value, 0.0, 1.0) * max);

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

    // A plane over the samples, optionally with padding after every row (copied into a wider buffer).
    private static VideoPlane Plane(byte[] tight, int rowLength, int rows, int padding)
    {
        if (padding == 0) return VideoPlane.FromArray(tight, rowLength, rows);

        int stride = rowLength + padding;
        byte[] padded = new byte[stride * rows];
        for (int row = 0; row < rows; row++)
        {
            Array.Copy(tight, row * rowLength, padded, row * stride, rowLength);
            for (int i = rowLength; i < stride; i++) padded[row * stride + i] = 0xEE;
        }

        return new VideoPlane(padded, stride, rowLength, rows);
    }
}
