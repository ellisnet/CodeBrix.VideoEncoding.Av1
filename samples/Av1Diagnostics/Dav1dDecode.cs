using System;
using System.Collections.Generic;
using CodeBrix.VideoPlayback.Dav1d;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>One picture decoded by the dav1d decoder package, copied out of the decoder's pool into plain arrays.</summary>
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

    /// <summary>0 unspecified, 1 limited, 2 full.</summary>
    public int Range { get; init; }
}

/// <summary>
/// Decodes temporal units with the PUBLISHED dav1d-based decoder package (an AV1 decoder that shares no code with the
/// encoder) and copies every picture out. This file is the only one that touches the playback types, whose names
/// (VideoPixelLayout, VideoCodecIds ...) overlap Core's.
/// </summary>
internal static class Dav1dDecode
{
    /// <summary>The decoder native's version string (loads it).</summary>
    public static string NativeVersion
    {
        get
        {
            CodeBrixVideoPlaybackDav1d.Register();
            return CodeBrixVideoPlaybackDav1d.NativeVersion;
        }
    }

    /// <summary>Decodes every temporal unit, in order, then drains the decoder.</summary>
    public static List<DecodedPicture> Decode(byte[] codecPrivate, IReadOnlyList<byte[]> units, IReadOnlyList<bool> keyFlags, int frameRate)
    {
        List<DecodedPicture> pictures = new List<DecodedPicture>();
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool, Threads = 2, MaxFrameDelay = 1 };

        using (IVideoDecoder decoder = CodeBrixVideoPlaybackDav1d.Factory.CreateDecoder(VideoCodecIds.Av1, codecPrivate, options))
        {
            for (int i = 0; i < units.Count; i++)
            {
                VideoPacket input = new VideoPacket(units[i], TimeSpan.FromSeconds((double)i / frameRate), keyFlags[i]);
                while (!decoder.SendPacket(input))
                {
                    if (decoder.TryReceiveFrame(out VideoFrame parked))
                    {
                        using (parked) pictures.Add(Copy(parked));
                    }
                }

                while (decoder.TryReceiveFrame(out VideoFrame frame))
                {
                    using (frame) pictures.Add(Copy(frame));
                }
            }

            decoder.Drain();
            while (decoder.TryReceiveFrame(out VideoFrame frame))
            {
                using (frame) pictures.Add(Copy(frame));
            }
        }

        return pictures;
    }

    private static DecodedPicture Copy(VideoFrame frame) =>
        new DecodedPicture
        {
            Width = frame.Width,
            Height = frame.Height,
            Layout = frame.Layout.ToString(),
            BitDepth = frame.BitDepth,
            IsKeyFrame = frame.IsKeyFrame,
            Y = CopyPlane(frame.Y),
            U = frame.U.IsEmpty ? null : CopyPlane(frame.U),
            V = frame.V.IsEmpty ? null : CopyPlane(frame.V),
            Range = (int)frame.Color.Range,
        };

    private static byte[] CopyPlane(VideoFramePlane plane)
    {
        int rowBytes = plane.Width * plane.BytesPerSample;
        byte[] copy = new byte[rowBytes * plane.Height];
        for (int row = 0; row < plane.Height; row++) plane.GetRowBytes(row).CopyTo(copy.AsSpan(row * rowBytes, rowBytes));
        return copy;
    }
}
