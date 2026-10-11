using System;
using CodeBrix.VideoProcessing.Core;
using CodeBrix.VideoProcessing.Core.Encoding;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1EncoderExceptionTests
{
    [Fact]
    public void is_a_video_encoding_exception()
        => new Av1EncoderException("x").Should().BeAssignableTo<VideoEncodingException>();

    [Fact]
    public void is_caught_as_a_video_processing_exception()
        => typeof(VideoProcessingException).IsAssignableFrom(typeof(Av1EncoderException)).Should().BeTrue();

    [Fact]
    public void carries_native_code_and_message()
    {
        //Act
        Av1EncoderException exception = new Av1EncoderException("failed", -4, "option \"tune\": Invalid parameter");

        //Assert
        exception.Message.Should().Be("failed");
        exception.NativeCode.Should().Be(-4);
        exception.NativeMessage.Should().Be("option \"tune\": Invalid parameter");
    }

    [Fact]
    public void native_message_is_never_null()
    {
        //Act
        Av1EncoderException fromNull = new Av1EncoderException("failed", -6, null);
        Av1EncoderException plain = new Av1EncoderException();
        Av1EncoderException inner = new Av1EncoderException("outer", new InvalidOperationException("inner"));

        //Assert
        fromNull.NativeMessage.Should().Be(string.Empty);
        plain.NativeMessage.Should().Be(string.Empty);
        inner.NativeMessage.Should().Be(string.Empty);
        inner.InnerException.Should().BeOfType<InvalidOperationException>();
        plain.NativeCode.Should().Be(0);
    }
}
