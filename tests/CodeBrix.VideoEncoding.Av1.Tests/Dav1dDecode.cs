using System;
using System.Collections.Generic;
using CodeBrix.VideoPlayback.Dav1d;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;
using CorePacket = CodeBrix.VideoProcessing.Core.Encoding.EncodedVideoPacket;

namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>
/// Decodes the encoder's packets with the PUBLISHED dav1d-based decoder package - an AV1 decoder that shares no
/// code with the encoder - and copies every picture out. This file is the only one that touches the playback
/// types, whose names (VideoPixelLayout, VideoCodecIds ...) overlap Core's.
/// </summary>
internal static class Dav1dDecode
{
    /// <summary>Decodes every packet, in order, then drains the decoder.</summary>
    public static List<DecodedPicture> Decode(ReadOnlyMemory<byte> codecPrivate, IReadOnlyList<CorePacket> packets)
    {
        List<DecodedPicture> pictures = new List<DecodedPicture>();
        using PinnedFrameBufferPool pool = new PinnedFrameBufferPool();
        VideoDecoderOptions options = new VideoDecoderOptions { BufferPool = pool, Threads = 2, MaxFrameDelay = 1 };

        using (IVideoDecoder decoder = CodeBrixVideoPlaybackDav1d.Factory.CreateDecoder(VideoCodecIds.Av1, codecPrivate, options))
        {
            foreach (CorePacket packet in packets)
            {
                VideoPacket input = new VideoPacket(packet.Data, packet.Timestamp, packet.IsKeyFrame);
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

    /// <summary>Probes an av1C record (or a packet) with the decoder package's own sequence-header parser.</summary>
    public static bool TryProbe(ReadOnlySpan<byte> data, out int width, out int height, out int bitDepth, out string layout, out int primaries, out int transfer, out int matrix, out int range)
    {
        if (!Dav1dDecoderFactory.TryProbe(data, out VideoStreamInfo info))
        {
            width = height = bitDepth = primaries = transfer = matrix = range = 0;
            layout = null;
            return false;
        }

        width = info.Width;
        height = info.Height;
        bitDepth = info.BitDepth;
        layout = info.Layout.ToString();
        primaries = (int)info.Color.Primaries;
        transfer = (int)info.Color.Transfer;
        matrix = (int)info.Color.Matrix;
        range = (int)info.Color.Range;
        return true;
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
            Primaries = (int)frame.Color.Primaries,
            Transfer = (int)frame.Color.Transfer,
            Matrix = (int)frame.Color.Matrix,
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
