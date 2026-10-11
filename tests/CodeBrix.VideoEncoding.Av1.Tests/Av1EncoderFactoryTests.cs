using System;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Output;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1EncoderFactoryTests
{
    [Fact]
    public void CodecId_is_av1() => new Av1EncoderFactory().CodecId.Should().Be(VideoCodecIds.Av1);

    [Fact]
    public void DisplayName_names_the_library() => new Av1EncoderFactory().DisplayName.Should().Contain("AV1");

    [Fact]
    public void Capabilities_advertise_i420_i444_gray_at_three_depths_lossless_and_forced_key_frames()
    {
        //Act
        VideoEncoderCapabilities capabilities = new Av1EncoderFactory().Capabilities;

        //Assert
        capabilities.SupportsLayout(VideoPixelLayout.I420).Should().BeTrue();
        capabilities.SupportsLayout(VideoPixelLayout.I444).Should().BeTrue();
        capabilities.SupportsLayout(VideoPixelLayout.Gray).Should().BeTrue();
        capabilities.SupportsLayout(VideoPixelLayout.Nv12).Should().BeFalse();
        capabilities.SupportsLayout(VideoPixelLayout.Rgba).Should().BeFalse();
        capabilities.SupportsBitDepth(8).Should().BeTrue();
        capabilities.SupportsBitDepth(10).Should().BeTrue();
        capabilities.SupportsBitDepth(12).Should().BeTrue();
        capabilities.SupportsLossless.Should().BeTrue();
        capabilities.SupportsForcedKeyFrames.Should().BeTrue();
    }

    [Fact]
    public void Create_refuses_null()
    {
        //Act
        Action act = () => new Av1EncoderFactory().Create(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(VideoPixelLayout.Nv12)]
    [InlineData(VideoPixelLayout.Rgba)]
    public void Create_refuses_unsupported_layouts_before_any_native_call(VideoPixelLayout layout)
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(layout);

        //Act
        Action act = () => new Av1EncoderFactory().Create(request);

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void Create_refuses_odd_i420_sizes_before_any_native_call(int bitDepth)
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request(VideoPixelLayout.I420, bitDepth, 161, 91);

        //Act
        Action act = () => new Av1EncoderFactory().Create(request);

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Create_refuses_an_invalid_request_before_any_native_call()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.Width = 0;

        //Act
        Action act = () => new Av1EncoderFactory().Create(request);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_refuses_lossless_with_a_quality()
    {
        //Arrange
        VideoEncoderRequest request = EncodeRun.Request();
        request.Lossless = true;
        request.Quality = VideoQuality.ConstantQuality(10);

        //Act
        Action act = () => new Av1EncoderFactory().Create(request);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void constructor_refuses_invalid_default_options()
    {
        //Act
        Action act = () => new Av1EncoderFactory(new Av1EncoderOptions { LagInFrames = 100 });

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultOptions_is_a_copy()
    {
        //Arrange
        Av1EncoderOptions options = new Av1EncoderOptions { CpuUsed = 4 };
        Av1EncoderFactory factory = new Av1EncoderFactory(options);

        //Act
        options.CpuUsed = 9;
        factory.DefaultOptions.CpuUsed = 8;

        //Assert
        factory.DefaultOptions.CpuUsed.Should().Be(4);
    }
}
