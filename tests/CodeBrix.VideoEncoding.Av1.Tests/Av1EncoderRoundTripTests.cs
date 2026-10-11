using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

/// <summary>
/// Encodes synthetic sequences with the native encoder and decodes every packet with the independent, published
/// dav1d-based decoder package. Small pictures and few frames keep each case fast.
/// </summary>
public class Av1EncoderRoundTripTests
{
    private const int Frames = 32;

    private static (List<TestPicture> Pictures, List<EncodedVideoPacket> Packets, List<DecodedPicture> Decoded, Av1Encoder Encoder) Run(
        VideoEncoderRequest request,
        Av1EncoderOptions options = null,
        int frames = Frames,
        ISet<int> forced = null,
        int stridePadding = 0,
        int seed = 0)
    {
        Av1Encoder encoder = new Av1Encoder(request, options);
        List<TestPicture> pictures = EncodeRun.Pictures(request.PixelLayout, request.BitDepth, frames, request.Width, request.Height, stridePadding, seed);
        List<EncodedVideoPacket> packets = EncodeRun.EncodeAll(encoder, pictures, forced);
        List<DecodedPicture> decoded = Dav1dDecode.Decode(encoder.CodecConfigurationRecord, packets);
        return (pictures, packets, decoded, encoder);
    }

    private static VideoEncoderRequest Lossless(VideoPixelLayout layout, int bitDepth, int width = EncodeRun.Width, int height = EncodeRun.Height)
    {
        VideoEncoderRequest request = EncodeRun.Request(layout, bitDepth, width, height);
        request.Quality = VideoQuality.EngineDefault;
        request.Lossless = true;
        return request;
    }

    [Theory]
    [InlineData(VideoPixelLayout.I420, 8, "I420")]
    [InlineData(VideoPixelLayout.I420, 10, "I420")]
    [InlineData(VideoPixelLayout.I420, 12, "I420")]
    [InlineData(VideoPixelLayout.I444, 8, "I444")]
    [InlineData(VideoPixelLayout.I444, 10, "I444")]
    [InlineData(VideoPixelLayout.I444, 12, "I444")]
    [InlineData(VideoPixelLayout.Gray, 8, "Gray")]
    [InlineData(VideoPixelLayout.Gray, 10, "Gray")]
    [InlineData(VideoPixelLayout.Gray, 12, "Gray")]
    public void lossy_round_trip_keeps_count_layout_depth_and_quality(VideoPixelLayout layout, int bitDepth, string decodedLayout)
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(layout, bitDepth);
        request.Quality = VideoQuality.ConstantQuality(16);

        //Act
        var run = Run(request, frames: 16);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Packets.Count.Should().Be(16);
        run.Decoded.Count.Should().Be(16);
        run.Decoded.Should().OnlyContain(d => d.Layout == decodedLayout && d.BitDepth == bitDepth && d.Width == EncodeRun.Width && d.Height == EncodeRun.Height);
        for (int i = 0; i < run.Decoded.Count; i++)
        {
            EncodeRun.Psnr(run.Pictures[i].Y, run.Decoded[i].Y, bitDepth).Should().BeGreaterThan(40.0);
            if (layout != VideoPixelLayout.Gray) EncodeRun.Psnr(run.Pictures[i].U, run.Decoded[i].U, bitDepth).Should().BeGreaterThan(35.0);
        }

        encoder.SequenceHeader.BitDepth.Should().Be(bitDepth);
        encoder.SequenceHeader.Monochrome.Should().Be(layout == VideoPixelLayout.Gray);
        encoder.SequenceHeader.Profile.Should().Be(bitDepth == 12 ? 2 : (layout == VideoPixelLayout.I444 ? 1 : 0));
    }

    [Theory]
    [InlineData(VideoPixelLayout.I420, 8)]
    [InlineData(VideoPixelLayout.I420, 10)]
    [InlineData(VideoPixelLayout.Gray, 8)]
    [InlineData(VideoPixelLayout.Gray, 10)]
    [InlineData(VideoPixelLayout.I444, 8)]
    [InlineData(VideoPixelLayout.I444, 10)]
    [InlineData(VideoPixelLayout.I420, 12)]
    public void lossless_round_trip_is_bit_exact(VideoPixelLayout layout, int bitDepth)
    {
        //Act
        var run = Run(Lossless(layout, bitDepth), frames: 12);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Decoded.Count.Should().Be(12);
        for (int i = 0; i < run.Decoded.Count; i++)
        {
            run.Decoded[i].Y.Should().Equal(run.Pictures[i].Y);
            if (layout == VideoPixelLayout.Gray)
            {
                run.Decoded[i].U.Should().BeNull();
                continue;
            }

            run.Decoded[i].U.Should().Equal(run.Pictures[i].U);
            run.Decoded[i].V.Should().Equal(run.Pictures[i].V);
        }
    }

    [Fact]
    public void lossless_round_trip_reads_strided_planes()
    {
        //Act
        var run = Run(Lossless(VideoPixelLayout.I420, 10, 162, 92), frames: 6, stridePadding: 24);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Decoded.Count.Should().Be(6);
        for (int i = 0; i < run.Decoded.Count; i++)
        {
            run.Decoded[i].Y.Should().Equal(run.Pictures[i].Y);
            run.Decoded[i].U.Should().Equal(run.Pictures[i].U);
            run.Decoded[i].V.Should().Equal(run.Pictures[i].V);
        }
    }

    [Theory]
    [InlineData(VideoPixelLayout.I444, 8)]
    [InlineData(VideoPixelLayout.I444, 10)]
    [InlineData(VideoPixelLayout.Gray, 8)]
    [InlineData(VideoPixelLayout.Gray, 10)]
    public void lossless_round_trip_is_bit_exact_at_odd_sizes_for_i444_and_gray(VideoPixelLayout layout, int bitDepth)
    {
        //Act
        var run = Run(Lossless(layout, bitDepth, 161, 91), frames: 6);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Decoded.Count.Should().Be(6);
        run.Decoded[0].Width.Should().Be(161);
        run.Decoded[0].Height.Should().Be(91);
        for (int i = 0; i < run.Decoded.Count; i++)
        {
            run.Decoded[i].Y.Should().Equal(run.Pictures[i].Y);
            if (layout == VideoPixelLayout.Gray) continue;
            run.Decoded[i].U.Should().Equal(run.Pictures[i].U);
            run.Decoded[i].V.Should().Equal(run.Pictures[i].V);
        }
    }

    [Fact]
    public void key_frames_reported_by_the_encoder_match_the_decoder()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.MaxKeyFrameInterval = 10;

        //Act
        var run = Run(request, frames: Frames, forced: new HashSet<int> { 15 });
        using Av1Encoder encoder = run.Encoder;

        //Assert
        List<int> encoderKeys = EncodeRun.KeyFramePositions(run.Packets);
        encoderKeys.Should().Equal(EncodeRun.KeyFramePositions(run.Decoded));
        encoderKeys[0].Should().Be(0);
        encoderKeys.Should().Contain(15);
        for (int i = 1; i < encoderKeys.Count; i++) (encoderKeys[i] - encoderKeys[i - 1]).Should().BeLessThanOrEqualTo(10);
        int last = encoderKeys[encoderKeys.Count - 1];
        (Frames - 1 - last).Should().BeLessThan(10);
    }

    [Fact]
    public void forced_key_frames_are_exactly_where_asked_in_realtime_usage()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();

        //Act
        var run = Run(request, new Av1EncoderOptions { Usage = Av1EncoderUsage.Realtime, AutomaticKeyFrames = false }, frames: 40, forced: new HashSet<int> { 7, 20, 33 });
        using Av1Encoder encoder = run.Encoder;

        //Assert
        EncodeRun.KeyFramePositions(run.Packets).Should().Equal(0, 7, 20, 33);
        EncodeRun.KeyFramePositions(run.Decoded).Should().Equal(0, 7, 20, 33);
    }

    [Fact]
    public void forced_key_frames_are_key_frames_in_good_quality_usage()
    {
        //Act
        var run = Run(EncodeRun.Request(), new Av1EncoderOptions { AutomaticKeyFrames = false }, frames: 40, forced: new HashSet<int> { 7, 20, 33 });
        using Av1Encoder encoder = run.Encoder;

        //Assert
        List<int> keys = EncodeRun.KeyFramePositions(run.Packets);
        keys.Should().Contain(0);
        keys.Should().Contain(7);
        keys.Should().Contain(20);
        keys.Should().Contain(33);
        keys.Should().Equal(EncodeRun.KeyFramePositions(run.Decoded));
    }

    [Fact]
    public void all_intra_usage_makes_every_frame_a_key_frame()
    {
        //Act
        var run = Run(EncodeRun.Request(), new Av1EncoderOptions { Usage = Av1EncoderUsage.AllIntra }, frames: 8);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Packets.Should().OnlyContain(p => p.IsKeyFrame);
        run.Decoded.Should().OnlyContain(d => d.IsKeyFrame);
    }

    [Fact]
    public void key_frame_interval_of_one_makes_every_frame_a_key_frame()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.MaxKeyFrameInterval = 1;

        //Act
        var run = Run(request, frames: 6);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Packets.Should().OnlyContain(p => p.IsKeyFrame);
    }

    [Fact]
    public void colour_and_alpha_encoders_in_lock_step_agree_on_frames_and_key_frames()
    {
        //Arrange
        VideoEncoderRequest colourRequest = EncodeRun.Request(VideoPixelLayout.I420, 10);
        VideoEncoderRequest alphaRequest = EncodeRun.Request(VideoPixelLayout.Gray, 10);
        Av1EncoderOptions lockStep = new Av1EncoderOptions { AutomaticKeyFrames = false };
        alphaRequest.ColorRange = VideoColorRange.Full;
        alphaRequest.ColorPrimaries = VideoColorPrimaries.Unspecified;
        alphaRequest.ColorTransfer = VideoColorTransfer.Unspecified;
        alphaRequest.ColorMatrix = VideoColorMatrix.Unspecified;
        alphaRequest.Quality = VideoQuality.ConstantQuality(8);
        HashSet<int> forced = new HashSet<int> { 0, 16, 21, 32 };
        List<TestPicture> colour = EncodeRun.Pictures(VideoPixelLayout.I420, 10, 48);
        List<TestPicture> alpha = EncodeRun.Pictures(VideoPixelLayout.Gray, 10, 48, seed: 3);
        List<EncodedVideoPacket> colourPackets = new List<EncodedVideoPacket>();
        List<EncodedVideoPacket> alphaPackets = new List<EncodedVideoPacket>();

        //Act
        using (Av1Encoder colourEncoder = new Av1Encoder(colourRequest, lockStep))
        using (Av1Encoder alphaEncoder = new Av1Encoder(alphaRequest, lockStep))
        {
            for (int i = 0; i < colour.Count; i++)
            {
                colourEncoder.Encode(colour[i].Frame, forced.Contains(i));
                alphaEncoder.Encode(alpha[i].Frame, forced.Contains(i));
                while (colourEncoder.TryReadPacket(out EncodedVideoPacket p)) colourPackets.Add(p);
                while (alphaEncoder.TryReadPacket(out EncodedVideoPacket p)) alphaPackets.Add(p);
            }

            colourEncoder.Flush();
            alphaEncoder.Flush();
            while (colourEncoder.TryReadPacket(out EncodedVideoPacket p)) colourPackets.Add(p);
            while (alphaEncoder.TryReadPacket(out EncodedVideoPacket p)) alphaPackets.Add(p);
        }

        //Assert
        colourPackets.Count.Should().Be(48);
        alphaPackets.Count.Should().Be(48);
        alphaPackets.Select(p => p.FrameIndex).Should().Equal(colourPackets.Select(p => p.FrameIndex));
        alphaPackets.Select(p => p.Timestamp).Should().Equal(colourPackets.Select(p => p.Timestamp));
        List<int> keys = EncodeRun.KeyFramePositions(colourPackets);
        EncodeRun.KeyFramePositions(alphaPackets).Should().Equal(keys);
        foreach (int frame in forced) keys.Should().Contain(frame);
    }

    [Fact]
    public void configuration_record_agrees_with_the_decoder()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(VideoPixelLayout.I444, 10);

        //Act
        var run = Run(request, frames: 4);
        using Av1Encoder encoder = run.Encoder;
        byte[] record = encoder.CodecConfigurationRecord.ToArray();
        bool probed = Dav1dDecode.TryProbe(record, out int width, out int height, out int bitDepth, out string layout, out int _, out int _, out int _, out int _);

        //Assert
        probed.Should().BeTrue();
        width.Should().Be(EncodeRun.Width);
        height.Should().Be(EncodeRun.Height);
        bitDepth.Should().Be(10);
        layout.Should().Be("I444");
        record[0].Should().Be((byte)0x81);
        (record[1] >> 5).Should().Be(1);
        (record[1] & 0x1F).Should().Be(encoder.SequenceHeader.LevelIndex);
        ((record[2] >> 6) & 1).Should().Be(1);
        ((record[2] >> 5) & 1).Should().Be(0);
        ((record[2] >> 4) & 1).Should().Be(0);
        ((record[2] >> 3) & 1).Should().Be(0);
        ((record[2] >> 2) & 1).Should().Be(0);
        record.AsSpan(4).ToArray().Should().Equal(encoder.SequenceHeaderObu.ToArray());
        run.Decoded[0].Layout.Should().Be("I444");
    }

    [Fact]
    public void every_key_frame_carries_the_same_sequence_header_as_the_record()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.MaxKeyFrameInterval = 8;

        //Act
        var run = Run(request, frames: 20);
        using Av1Encoder encoder = run.Encoder;
        byte[] obu = encoder.SequenceHeaderObu.ToArray();

        //Assert
        foreach (EncodedVideoPacket packet in run.Packets.Where(p => p.IsKeyFrame))
        {
            byte[] data = packet.Data.ToArray();
            data[0].Should().Be((byte)0x12);
            data[1].Should().Be((byte)0x00);
            data.AsSpan(2, obu.Length).ToArray().Should().Equal(obu);
        }
    }

    [Theory]
    [InlineData(VideoColorPrimaries.Bt709, VideoColorTransfer.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Limited, 1, 1, 1, 1)]
    [InlineData(VideoColorPrimaries.Bt2020, VideoColorTransfer.Pq, VideoColorMatrix.Bt2020NonConstant, VideoColorRange.Full, 9, 16, 9, 2)]
    [InlineData(VideoColorPrimaries.Bt601Ntsc, VideoColorTransfer.Srgb, VideoColorMatrix.Bt601, VideoColorRange.Limited, 6, 13, 6, 1)]
    [InlineData(VideoColorPrimaries.DisplayP3, VideoColorTransfer.Hlg, VideoColorMatrix.Bt709, VideoColorRange.Full, 12, 18, 1, 2)]
    public void colour_tags_round_trip(VideoColorPrimaries primaries, VideoColorTransfer transfer, VideoColorMatrix matrix, VideoColorRange range, int av1Primaries, int av1Transfer, int av1Matrix, int decodedRange)
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.ColorPrimaries = primaries;
        request.ColorTransfer = transfer;
        request.ColorMatrix = matrix;
        request.ColorRange = range;

        //Act
        var run = Run(request, frames: 2);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        encoder.SequenceHeader.ColorPrimaries.Should().Be(av1Primaries);
        encoder.SequenceHeader.TransferCharacteristics.Should().Be(av1Transfer);
        encoder.SequenceHeader.MatrixCoefficients.Should().Be(av1Matrix);
        encoder.SequenceHeader.FullRange.Should().Be(range == VideoColorRange.Full);
        run.Decoded[0].Primaries.Should().Be(av1Primaries);
        run.Decoded[0].Transfer.Should().Be(av1Transfer);
        run.Decoded[0].Matrix.Should().Be(av1Matrix);
        run.Decoded[0].Range.Should().Be(decodedRange);
    }

    [Fact]
    public void a_full_range_untagged_alpha_stream_round_trips()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(VideoPixelLayout.Gray, 8);
        request.ColorRange = VideoColorRange.Full;
        request.ColorPrimaries = VideoColorPrimaries.Unspecified;
        request.ColorTransfer = VideoColorTransfer.Unspecified;
        request.ColorMatrix = VideoColorMatrix.Unspecified;

        //Act
        var run = Run(request, frames: 2);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        encoder.SequenceHeader.Monochrome.Should().BeTrue();
        encoder.SequenceHeader.FullRange.Should().BeTrue();
        run.Decoded[0].Layout.Should().Be("Gray");
        run.Decoded[0].Range.Should().Be(2);
        run.Decoded[0].Primaries.Should().Be(2);
    }

    [Fact]
    public void the_rgb_matrix_round_trips_for_i444()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(VideoPixelLayout.I444, 8);
        request.ColorMatrix = VideoColorMatrix.Rgb;
        request.ColorTransfer = VideoColorTransfer.Srgb;
        request.ColorRange = VideoColorRange.Full;

        //Act
        var run = Run(request, frames: 2);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        encoder.SequenceHeader.MatrixCoefficients.Should().Be(0);
        run.Decoded[0].Matrix.Should().Be(0);
        run.Decoded[0].Layout.Should().Be("I444");
    }

    [Fact]
    public void chroma_sample_position_is_signalled()
    {
        //Act
        var run = Run(EncodeRun.Request(), new Av1EncoderOptions { ChromaSamplePosition = Av1ChromaSamplePosition.Colocated }, frames: 2);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        encoder.SequenceHeader.ChromaSamplePosition.Should().Be(2);
        (encoder.CodecConfigurationRecord.Span[2] & 0x3).Should().Be(2);
    }

    [Fact]
    public void codec_options_pass_through_to_the_encoder()
    {
        //Arrange
        VideoEncoderRequest plain = EncodeRun.Request();
        VideoEncoderRequest tuned = EncodeRun.Request();
        tuned.CodecOptions["tune"] = "psnr";
        tuned.CodecOptions["sharpness"] = "7";
        tuned.CodecOptions["enable-cdef"] = "0";

        //Act
        var a = Run(plain, frames: 8);
        var b = Run(tuned, frames: 8);
        using Av1Encoder first = a.Encoder;
        using Av1Encoder second = b.Encoder;

        //Assert
        b.Decoded.Count.Should().Be(8);
        bool identical = a.Packets.Select(p => p.Data.ToArray()).SequenceEqual(b.Packets.Select(p => p.Data.ToArray()), new BytesComparer());
        identical.Should().BeFalse();
    }

    [Theory]
    [InlineData(Av1RateMode.VariableBitrate)]
    [InlineData(Av1RateMode.ConstantBitrate)]
    public void bitrate_modes_encode_and_decode(Av1RateMode mode)
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.Quality = VideoQuality.TargetBitrate(300);

        //Act
        var run = Run(request, new Av1EncoderOptions { RateMode = mode }, frames: 16);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Decoded.Count.Should().Be(16);
    }

    [Fact]
    public void constrained_quality_encodes_and_decodes()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.Quality = VideoQuality.ConstantQuality(30);

        //Act
        var run = Run(request, new Av1EncoderOptions { RateMode = Av1RateMode.ConstrainedQuality, TargetBitrateKbps = 500 }, frames: 8);
        using Av1Encoder encoder = run.Encoder;

        //Assert
        run.Decoded.Count.Should().Be(8);
    }

    [Fact]
    public void a_lower_quality_level_spends_more_bits()
    {
        //Arrange
        VideoEncoderRequest best = EncodeRun.Request();
        best.Quality = VideoQuality.ConstantQuality(5);
        VideoEncoderRequest worst = EncodeRun.Request();
        worst.Quality = VideoQuality.ConstantQuality(50);

        //Act
        var a = Run(best, frames: 8);
        var b = Run(worst, frames: 8);
        using Av1Encoder first = a.Encoder;
        using Av1Encoder second = b.Encoder;

        //Assert
        a.Packets.Sum(p => p.Data.Length).Should().BeGreaterThan(b.Packets.Sum(p => p.Data.Length));
    }

    [Fact]
    public async Task two_encoders_on_two_threads_produce_identical_streams()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.Threads = 1;
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 24);

        List<EncodedVideoPacket> EncodeOnce()
        {
            using Av1Encoder encoder = new Av1Encoder(request, new Av1EncoderOptions { RowMultithreading = false });
            return EncodeRun.EncodeAll(encoder, pictures);
        }

        //Act
        Task<List<EncodedVideoPacket>> one = Task.Run(EncodeOnce, TestContext.Current.CancellationToken);
        Task<List<EncodedVideoPacket>> two = Task.Run(EncodeOnce, TestContext.Current.CancellationToken);
        List<EncodedVideoPacket>[] results = await Task.WhenAll(one, two);

        //Assert
        results[0].Count.Should().Be(24);
        results[1].Count.Should().Be(24);
        results[0].Select(p => p.Data.ToArray()).SequenceEqual(results[1].Select(p => p.Data.ToArray()), new BytesComparer()).Should().BeTrue();
    }

    [Fact]
    public void IvfWriter_dumps_a_stream_the_decoder_reads_back()
    {
        //Arrange
        var run = Run(EncodeRun.Request(), frames: 6);
        using Av1Encoder encoder = run.Encoder;
        using MemoryStream stream = new MemoryStream();

        //Act
        using (IvfWriter writer = new IvfWriter(stream, EncodeRun.Width, EncodeRun.Height, 30, 1))
        {
            foreach (EncodedVideoPacket packet in run.Packets) writer.WriteFrame(packet.Data.Span, packet.FrameIndex);
        }

        string dump = Environment.GetEnvironmentVariable("CODEBRIX_AV1_TEST_DUMP_DIR");
        if (!string.IsNullOrEmpty(dump)) File.WriteAllBytes(Path.Combine(dump, "av1-roundtrip-i420-8bit.ivf"), stream.ToArray());

        //Assert
        byte[] ivf = stream.ToArray();
        ivf.AsSpan(0, 4).ToArray().Should().Equal((byte)'D', (byte)'K', (byte)'I', (byte)'F');
        ivf.AsSpan(8, 4).ToArray().Should().Equal((byte)'A', (byte)'V', (byte)'0', (byte)'1');
        BitConverter.ToUInt32(ivf, 24).Should().Be(6u);
        ivf.Length.Should().Be(32 + run.Packets.Sum(p => 12 + p.Data.Length));
    }

    private sealed class BytesComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
