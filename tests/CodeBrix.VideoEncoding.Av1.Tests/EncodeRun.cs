using System;
using System.Collections.Generic;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;

namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>Shared encode loops and requests for the native tests: small pictures, few frames, a fast speed.</summary>
internal static class EncodeRun
{
    public const int Width = 320;
    public const int Height = 180;

    public static VideoEncoderRequest Request(VideoPixelLayout layout = VideoPixelLayout.I420, int bitDepth = 8, int width = Width, int height = Height) =>
        new VideoEncoderRequest
        {
            Width = width,
            Height = height,
            PixelLayout = layout,
            BitDepth = bitDepth,
            FrameRate = new FrameRate(30, 1),
            Quality = VideoQuality.ConstantQuality(20),
            Threads = 2,
            Speed = 8,
        };

    /// <summary>Encodes pictures 0..count-1 of a test sequence, draining after every frame and after the flush.</summary>
    public static List<EncodedVideoPacket> EncodeAll(IVideoEncoder encoder, IReadOnlyList<TestPicture> pictures, ISet<int> forcedKeyFrames = null)
    {
        List<EncodedVideoPacket> packets = new List<EncodedVideoPacket>();
        for (int i = 0; i < pictures.Count; i++)
        {
            encoder.Encode(pictures[i].Frame, forcedKeyFrames != null && forcedKeyFrames.Contains(i));
            while (encoder.TryReadPacket(out EncodedVideoPacket packet)) packets.Add(packet);
        }

        encoder.Flush();
        while (encoder.TryReadPacket(out EncodedVideoPacket packet)) packets.Add(packet);
        return packets;
    }

    public static List<TestPicture> Pictures(VideoPixelLayout layout, int bitDepth, int count, int width = Width, int height = Height, int stridePadding = 0, int seed = 0, double noise = 0)
    {
        List<TestPicture> pictures = new List<TestPicture>(count);
        for (int i = 0; i < count; i++) pictures.Add(TestPicture.Create(layout, width, height, bitDepth, i, stridePadding, seed, noise));
        return pictures;
    }

    public static List<int> KeyFramePositions(IReadOnlyList<EncodedVideoPacket> packets)
    {
        List<int> positions = new List<int>();
        for (int i = 0; i < packets.Count; i++)
        {
            if (packets[i].IsKeyFrame) positions.Add(i);
        }

        return positions;
    }

    public static List<int> KeyFramePositions(IReadOnlyList<DecodedPicture> pictures)
    {
        List<int> positions = new List<int>();
        for (int i = 0; i < pictures.Count; i++)
        {
            if (pictures[i].IsKeyFrame) positions.Add(i);
        }

        return positions;
    }

    /// <summary>PSNR in dB between two planes of samples (8-bit bytes or 16-bit little-endian words).</summary>
    public static double Psnr(byte[] expected, byte[] actual, int bitDepth)
    {
        if (expected.Length != actual.Length) throw new ArgumentException("The planes differ in size.");
        int bytes = bitDepth > 8 ? 2 : 1;
        double max = (1 << bitDepth) - 1;
        double sum = 0;
        int samples = expected.Length / bytes;
        for (int i = 0; i < samples; i++)
        {
            int a = bytes == 1 ? expected[i] : expected[2 * i] | (expected[2 * i + 1] << 8);
            int b = bytes == 1 ? actual[i] : actual[2 * i] | (actual[2 * i + 1] << 8);
            double d = a - b;
            sum += d * d;
        }

        if (sum == 0) return double.PositiveInfinity;
        return 10 * Math.Log10(max * max / (sum / samples));
    }
}
