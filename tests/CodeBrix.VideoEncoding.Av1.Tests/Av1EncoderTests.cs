using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1EncoderTests
{
    // ---- Pure managed: everything here is refused or decided before any native call. ----

    [Fact]
    public void constructor_refuses_null_request()
    {
        //Act
        Action act = () => new Av1Encoder(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(VideoPixelLayout.Nv12)]
    [InlineData(VideoPixelLayout.Rgba)]
    public void constructor_refuses_nv12_and_rgba(VideoPixelLayout layout)
    {
        //Act
        Action act = () => new Av1Encoder(EncodeRun.Request(layout));

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(8, 161, 90)]
    [InlineData(8, 160, 91)]
    [InlineData(10, 161, 91)]
    public void constructor_refuses_odd_i420_sizes_before_any_native_call(int bitDepth, int width, int height)
    {
        //Act
        Action act = () => new Av1Encoder(EncodeRun.Request(VideoPixelLayout.I420, bitDepth, width, height));

        //Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*even width and height*");
    }

    [Theory]
    [InlineData(VideoPixelLayout.I444)]
    [InlineData(VideoPixelLayout.Gray)]
    public void constructor_accepts_odd_sizes_for_i444_and_gray(VideoPixelLayout layout)
    {
        //Act
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request(layout, 10, 161, 91));

        //Assert
        encoder.Request.Width.Should().Be(161);
    }

    [Fact]
    public void constructor_keeps_its_own_copies()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.CodecOptions["tune"] = "psnr";
        Av1EncoderOptions options = new Av1EncoderOptions { CpuUsed = 5 };

        //Act
        using Av1Encoder encoder = new Av1Encoder(request, options);
        request.Width = 64;
        request.CodecOptions["tune"] = "ssim";
        options.CpuUsed = 1;

        //Assert
        encoder.Request.Width.Should().Be(EncodeRun.Width);
        encoder.Request.CodecOptions["tune"].Should().Be("psnr");
        encoder.Options.CpuUsed.Should().Be(5);
        encoder.Request.Should().NotBeSameAs(request);
    }

    [Fact]
    public void a_new_encoder_has_not_started()
    {
        //Act
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Assert
        encoder.IsStarted.Should().BeFalse();
        encoder.CodecConfigurationRecord.IsEmpty.Should().BeTrue();
        encoder.SequenceHeaderObu.IsEmpty.Should().BeTrue();
        encoder.SequenceHeader.Should().BeNull();
        encoder.FramesSubmitted.Should().Be(0);
        encoder.PacketsProduced.Should().Be(0);
    }

    [Fact]
    public void Encode_refuses_an_empty_frame()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        Action act = () => encoder.Encode(default, false);

        //Assert
        act.Should().Throw<ArgumentException>();
        encoder.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void Encode_refuses_a_frame_of_the_wrong_size()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.I420, 160, 90, 8, 0).Frame;

        //Act
        Action act = () => encoder.Encode(frame, false);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*160x90*");
        encoder.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void Encode_refuses_a_frame_of_the_wrong_layout()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request(VideoPixelLayout.I420));
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.Gray, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame;

        //Act
        Action act = () => encoder.Encode(frame, false);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Gray*");
    }

    [Fact]
    public void Encode_refuses_an_nv12_frame_on_an_i420_encoder()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request(VideoPixelLayout.I420));
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.Nv12, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame;

        //Act
        Action act = () => encoder.Encode(frame, false);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_refuses_a_frame_of_the_wrong_bit_depth()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request(VideoPixelLayout.I420, 10));
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.I420, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame;

        //Act
        Action act = () => encoder.Encode(frame, false);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*8-bit*");
    }

    [Fact]
    public void Flush_on_a_never_started_encoder_produces_nothing()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        encoder.Flush();
        encoder.Flush();
        bool any = encoder.TryReadPacket(out EncodedVideoPacket packet);

        //Assert
        any.Should().BeFalse();
        packet.IsEmpty.Should().BeTrue();
        encoder.IsFlushed.Should().BeTrue();
        encoder.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void Encode_after_Flush_throws_invalid_operation()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.I420, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame;
        encoder.Flush();

        //Act
        Action act = () => encoder.Encode(frame, false);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dispose_twice_is_safe()
    {
        //Arrange
        Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        encoder.Dispose();
        encoder.Dispose();

        //Assert
        encoder.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void every_call_after_Dispose_throws_object_disposed()
    {
        //Arrange
        Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        VideoFrameData frame = TestPicture.Create(VideoPixelLayout.I420, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame;
        encoder.Dispose();

        //Act
        Action encode = () => encoder.Encode(frame, false);
        Action flush = () => encoder.Flush();
        Action read = () => encoder.TryReadPacket(out EncodedVideoPacket _);
        Action start = () => encoder.Start();

        //Assert
        encode.Should().Throw<ObjectDisposedException>();
        flush.Should().Throw<ObjectDisposedException>();
        read.Should().Throw<ObjectDisposedException>();
        start.Should().Throw<ObjectDisposedException>();
        encoder.Request.Width.Should().Be(EncodeRun.Width);
    }

    // ---- With the native encoder. ----

    [Fact]
    public void Start_leaves_the_record_for_the_first_packet()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        encoder.Start();
        encoder.Start();

        //Assert
        encoder.IsStarted.Should().BeTrue();
        encoder.CodecConfigurationRecord.IsEmpty.Should().BeTrue();
        encoder.SequenceHeader.Should().BeNull();
    }

    [Fact]
    public void the_configuration_record_is_set_before_the_first_packet_is_returned()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 40);
        ReadOnlyMemory<byte> recordAtFirstPacket = default;

        //Act
        foreach (TestPicture picture in pictures)
        {
            encoder.Encode(picture.Frame, false);
            if (encoder.TryReadPacket(out EncodedVideoPacket _) && recordAtFirstPacket.IsEmpty) recordAtFirstPacket = encoder.CodecConfigurationRecord;
            while (encoder.TryReadPacket(out EncodedVideoPacket _)) { }
        }

        encoder.Flush();
        if (recordAtFirstPacket.IsEmpty && encoder.TryReadPacket(out EncodedVideoPacket _)) recordAtFirstPacket = encoder.CodecConfigurationRecord;

        //Assert
        recordAtFirstPacket.IsEmpty.Should().BeFalse();
        recordAtFirstPacket.Span[0].Should().Be((byte)0x81);
        recordAtFirstPacket.Slice(4).ToArray().Should().Equal(encoder.SequenceHeaderObu.ToArray());
        encoder.CodecConfigurationRecord.Equals(recordAtFirstPacket).Should().BeTrue();
        encoder.SequenceHeader.MaxFrameWidth.Should().Be(EncodeRun.Width);
        encoder.SequenceHeader.MaxFrameHeight.Should().Be(EncodeRun.Height);
        encoder.SequenceHeader.Profile.Should().Be(0);
    }

    [Fact]
    public void Encode_starts_lazily()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        encoder.Encode(TestPicture.Create(VideoPixelLayout.I420, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame, false);

        //Assert
        encoder.IsStarted.Should().BeTrue();
        encoder.FramesSubmitted.Should().Be(1);
    }

    [Fact]
    public void packets_are_numbered_by_the_encoder_and_stamped_from_the_frame_rate()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.FrameRate = new FrameRate(30000, 1001);
        using Av1Encoder encoder = new Av1Encoder(request);
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 12);
        List<EncodedVideoPacket> packets = new List<EncodedVideoPacket>();

        //Act
        for (int i = 0; i < pictures.Count; i++)
        {
            VideoFrameData misleading = pictures[i].Frame.WithPosition(TimeSpan.FromHours(1 + i), 1000 - i);
            encoder.Encode(misleading, false);
            while (encoder.TryReadPacket(out EncodedVideoPacket packet)) packets.Add(packet);
        }

        encoder.Flush();
        while (encoder.TryReadPacket(out EncodedVideoPacket packet)) packets.Add(packet);

        //Assert
        packets.Select(p => p.FrameIndex).Should().Equal(Enumerable.Range(0, 12).Select(i => (long)i));
        packets.Select(p => p.Timestamp).Should().Equal(Enumerable.Range(0, 12).Select(i => request.FrameRate.TimestampOf(i)));
        encoder.FramesSubmitted.Should().Be(12);
        encoder.PacketsProduced.Should().Be(12);
        packets[0].IsKeyFrame.Should().BeTrue();
    }

    [Fact]
    public void every_packet_is_a_temporal_unit_in_a_fresh_array()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());

        //Act
        List<EncodedVideoPacket> packets = EncodeRun.EncodeAll(encoder, EncodeRun.Pictures(VideoPixelLayout.I420, 8, 6));

        //Assert
        packets.Count.Should().Be(6);
        HashSet<byte[]> arrays = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        foreach (EncodedVideoPacket packet in packets)
        {
            packet.Data.Span[0].Should().Be((byte)0x12);
            packet.Data.Span[1].Should().Be((byte)0x00);
            MemoryMarshal.TryGetArray(packet.Data, out ArraySegment<byte> segment).Should().BeTrue();
            segment.Offset.Should().Be(0);
            segment.Count.Should().Be(segment.Array.Length);
            arrays.Add(segment.Array).Should().BeTrue();
        }
    }

    [Fact]
    public void realtime_usage_returns_each_packet_from_its_own_Encode_call()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request(), new Av1EncoderOptions { Usage = Av1EncoderUsage.Realtime });
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 8);
        List<int> perCall = new List<int>();

        //Act
        foreach (TestPicture picture in pictures)
        {
            encoder.Encode(picture.Frame, false);
            int count = 0;
            while (encoder.TryReadPacket(out EncodedVideoPacket _)) count++;
            perCall.Add(count);
        }

        //Assert
        perCall.Should().OnlyContain(c => c == 1);
    }

    [Fact]
    public void good_quality_usage_holds_frames_back_until_Flush()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 8);
        int beforeFlush = 0;

        //Act
        foreach (TestPicture picture in pictures)
        {
            encoder.Encode(picture.Frame, false);
            while (encoder.TryReadPacket(out EncodedVideoPacket _)) beforeFlush++;
        }

        encoder.Flush();
        int afterFlush = 0;
        while (encoder.TryReadPacket(out EncodedVideoPacket _)) afterFlush++;

        //Assert
        (beforeFlush + afterFlush).Should().Be(8);
        afterFlush.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Flush_twice_and_reading_past_the_end_are_harmless()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        EncodeRun.EncodeAll(encoder, EncodeRun.Pictures(VideoPixelLayout.I420, 8, 3));

        //Act
        encoder.Flush();
        bool more = encoder.TryReadPacket(out EncodedVideoPacket packet);

        //Assert
        more.Should().BeFalse();
        packet.IsEmpty.Should().BeTrue();
        encoder.PacketsProduced.Should().Be(3);
    }

    [Fact]
    public void Encode_after_Flush_of_a_started_encoder_throws_invalid_operation()
    {
        //Arrange
        using Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        List<TestPicture> pictures = EncodeRun.Pictures(VideoPixelLayout.I420, 8, 2);
        EncodeRun.EncodeAll(encoder, pictures);

        //Act
        Action act = () => encoder.Encode(pictures[0].Frame, false);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dispose_without_Flush_releases_a_started_encoder()
    {
        //Arrange
        Av1Encoder encoder = new Av1Encoder(EncodeRun.Request());
        encoder.Encode(TestPicture.Create(VideoPixelLayout.I420, EncodeRun.Width, EncodeRun.Height, 8, 0).Frame, false);

        //Act
        encoder.Dispose();
        encoder.Dispose();
        Action read = () => encoder.TryReadPacket(out EncodedVideoPacket _);

        //Assert
        read.Should().Throw<ObjectDisposedException>();
        encoder.FramesSubmitted.Should().Be(1);
    }

    [Fact]
    public void an_unknown_codec_option_is_refused_with_the_encoders_reason()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.CodecOptions["no-such-option"] = "1";
        using Av1Encoder encoder = new Av1Encoder(request);

        //Act
        Action act = () => encoder.Start();

        //Assert
        ArgumentException thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("CodecOptions");
        thrown.Message.Should().Contain("no-such-option");
        Av1EncoderException inner = thrown.InnerException.Should().BeOfType<Av1EncoderException>().Which;
        inner.NativeCode.Should().BeLessThan(0);
        inner.NativeMessage.Should().Contain("no-such-option");
        encoder.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void a_bad_codec_option_value_is_refused()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.CodecOptions["tune"] = "not-a-tuning";

        //Act
        Action act = () => new Av1EncoderFactory().Create(request);

        //Assert
        act.Should().Throw<ArgumentException>().Which.InnerException.Should().BeOfType<Av1EncoderException>();
    }

    [Fact]
    public void factory_Create_returns_a_started_encoder()
    {
        //Act
        using IVideoEncoder encoder = new Av1EncoderFactory().Create(EncodeRun.Request(VideoPixelLayout.Gray, 10));
        bool startedAtCreate = ((Av1Encoder)encoder).IsStarted;
        EncodeRun.EncodeAll(encoder, EncodeRun.Pictures(VideoPixelLayout.Gray, 10, 2));

        //Assert
        encoder.Should().BeOfType<Av1Encoder>();
        startedAtCreate.Should().BeTrue();
        encoder.CodecConfigurationRecord.IsEmpty.Should().BeFalse();
        ((Av1Encoder)encoder).SequenceHeader.Monochrome.Should().BeTrue();
        ((Av1Encoder)encoder).SequenceHeader.BitDepth.Should().Be(10);
    }

    [Fact]
    public void factory_encoders_use_the_factory_default_options()
    {
        //Arrange
        Av1EncoderFactory factory = new Av1EncoderFactory(new Av1EncoderOptions { Usage = Av1EncoderUsage.AllIntra });

        //Act
        using IVideoEncoder encoder = factory.Create(EncodeRun.Request());
        List<EncodedVideoPacket> packets = EncodeRun.EncodeAll(encoder, EncodeRun.Pictures(VideoPixelLayout.I420, 8, 4));

        //Assert
        ((Av1Encoder)encoder).Options.Usage.Should().Be(Av1EncoderUsage.AllIntra);
        packets.Should().OnlyContain(p => p.IsKeyFrame);
    }
}
