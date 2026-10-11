using System;
using System.Collections.Generic;
using System.Diagnostics;
using global::Android.Media;
using ByteBuffer = global::Java.Nio.ByteBuffer;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>What the device's own AV1 decoder made of a stream.</summary>
internal sealed class MediaCodecResult
{
    public string DecoderName { get; set; }

    public bool SoftwareOnly { get; set; }

    public bool HardwareAccelerated { get; set; }

    public string Profiles { get; set; }

    public int FramesQueued { get; set; }

    public int FramesDecoded { get; set; }

    public string OutputFormat { get; set; }

    public string FirstImageDescription { get; set; }

    /// <summary>The first decoded picture's luma at 8-bit scale, Width x Height, tightly packed; null when unread.</summary>
    public byte[] FirstLuma8 { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double ElapsedMilliseconds { get; set; }

    public string Error { get; set; }

    /// <summary>
    /// True when the decoder itself failed (exception or timeout) before producing a single picture - the device's
    /// decoder is unusable, as opposed to a decoder that ran and produced wrong output.
    /// </summary>
    public bool DecoderUnavailable => Error != null && FramesDecoded == 0;
}

/// <summary>
/// Decodes AV1 temporal units with the device's own MediaCodec "video/av01" decoder, in synchronous mode, with the
/// av1C record as csd-0, into ByteBuffers (no Surface) - and reads the first picture's luma through the Image API.
/// </summary>
internal static class MediaCodecDecode
{
    private const string Mime = "video/av01";
    private const long TimeoutMicroseconds = 10_000;

    public static MediaCodecResult Decode(byte[] av1C, IReadOnlyList<byte[]> units, int width, int height, int frameRate)
    {
        MediaCodecResult result = new MediaCodecResult();
        Stopwatch watch = Stopwatch.StartNew();
        MediaCodec codec = null;
        try
        {
            MediaFormat format = MediaFormat.CreateVideoFormat(Mime, width, height);
            format.SetByteBuffer("csd-0", ByteBuffer.Wrap(av1C));

            string name = new MediaCodecList(MediaCodecListKind.RegularCodecs).FindDecoderForFormat(format);
            codec = name != null ? MediaCodec.CreateByCodecName(name) : MediaCodec.CreateDecoderByType(Mime);
            MediaCodecInfo info = codec.CodecInfo;
            result.DecoderName = codec.Name;
            result.SoftwareOnly = info.IsSoftwareOnly;
            result.HardwareAccelerated = info.IsHardwareAccelerated;
            var profiles = new List<string>();
            foreach (MediaCodecInfo.CodecProfileLevel level in info.GetCapabilitiesForType(Mime).ProfileLevels)
            {
                profiles.Add($"0x{(int)level.Profile:X}/0x{(int)level.Level:X}");
            }

            result.Profiles = string.Join(" ", profiles);

            codec.Configure(format, null, null, MediaCodecConfigFlags.None);
            codec.Start();

            int next = 0;
            bool inputDone = false;
            bool outputDone = false;
            MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();
            while (!outputDone)
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"MediaCodec still running after 60 s ({result.FramesDecoded} frames out)");

                if (!inputDone)
                {
                    int inputIndex = codec.DequeueInputBuffer(TimeoutMicroseconds);
                    if (inputIndex >= 0)
                    {
                        if (next < units.Count)
                        {
                            ByteBuffer input = codec.GetInputBuffer(inputIndex);
                            input.Clear();
                            input.Put(units[next]);
                            long pts = next * 1_000_000L / frameRate;
                            codec.QueueInputBuffer(inputIndex, 0, units[next].Length, pts, MediaCodecBufferFlags.None);
                            next++;
                            result.FramesQueued = next;
                        }
                        else
                        {
                            codec.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                    }
                }

                int outputIndex = codec.DequeueOutputBuffer(bufferInfo, TimeoutMicroseconds);
                if (outputIndex == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    result.OutputFormat = codec.OutputFormat?.ToString();
                }
                else if (outputIndex >= 0)
                {
                    bool endOfStream = (bufferInfo.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                    if (bufferInfo.Size > 0 || !endOfStream)
                    {
                        if (result.FramesDecoded == 0)
                        {
                            result.OutputFormat ??= codec.GetOutputFormat(outputIndex)?.ToString();
                            ReadFirstImage(codec, outputIndex, width, height, result);
                        }

                        result.FramesDecoded++;
                    }

                    codec.ReleaseOutputBuffer(outputIndex, false);
                    if (endOfStream) outputDone = true;
                }
            }
        }
        catch (MediaCodec.CodecException ex)
        {
            result.Error = $"MediaCodec.CodecException {ex.ErrorCode}: {ex.DiagnosticInfo} (transient {ex.IsTransient}, "
                + $"recoverable {ex.IsRecoverable}) after {result.FramesQueued} units queued";
        }
        catch (Java.Lang.Throwable ex)
        {
            result.Error = $"{ex.Class?.Name ?? ex.GetType().Name}: {ex.LocalizedMessage ?? ex.Message} after {result.FramesQueued} units queued";
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message} after {result.FramesQueued} units queued";
        }
        finally
        {
            try { codec?.Stop(); } catch (Exception) { }
            codec?.Release();
            watch.Stop();
            result.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
        }

        return result;
    }

    // Reads the luma plane of the first output picture through the Image API (YUV_420_888 or P010), scaled to 8 bits.
    private static void ReadFirstImage(MediaCodec codec, int outputIndex, int width, int height, MediaCodecResult result)
    {
        using Image image = codec.GetOutputImage(outputIndex);
        if (image == null)
        {
            result.FirstImageDescription = "GetOutputImage returned null";
            return;
        }

        Image.Plane[] planes = image.GetPlanes();
        Image.Plane yPlane = planes[0];
        var crop = image.CropRect;
        int left = crop?.Left ?? 0;
        int top = crop?.Top ?? 0;
        ByteBuffer buffer = yPlane.Buffer;
        byte[] bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        int pixelStride = yPlane.PixelStride;
        int rowStride = yPlane.RowStride;
        int w = Math.Min(width, image.Width - left);
        int h = Math.Min(height, image.Height - top);

        // 16-bit samples: P010 keeps the value in the top 10 bits; detect it rather than trust the format name.
        bool msbAligned = false;
        if (pixelStride == 2)
        {
            int lowBitsSet = 0;
            for (int i = 0; i + 1 < bytes.Length && i < 200_000; i += 2) lowBitsSet |= bytes[i] & 0x3F;
            msbAligned = lowBitsSet == 0;
        }

        byte[] luma = new byte[width * height];
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                int o = (row + top) * rowStride + (col + left) * pixelStride;
                int value;
                if (pixelStride == 2)
                {
                    int word = bytes[o] | (bytes[o + 1] << 8);
                    value = msbAligned ? word >> 8 : word >> 2;
                }
                else
                {
                    value = bytes[o];
                }

                luma[row * width + col] = (byte)value;
            }
        }

        result.FirstLuma8 = luma;
        result.Width = w;
        result.Height = h;
        result.FirstImageDescription = $"Image format {image.Format} ({(int)image.Format}), {image.Width}x{image.Height}, crop {crop}, "
            + $"planes {planes.Length}, Y pixelStride {pixelStride}, rowStride {rowStride}"
            + (pixelStride == 2 ? (msbAligned ? ", 16-bit MSB-aligned (P010)" : ", 16-bit LSB-aligned") : ", 8-bit");
    }
}
