using System.Linq;
using CodeBrix.VideoProcessing.Core.Encoding;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class CodeBrixVideoEncodingAv1Tests
{
    [Fact]
    public void Register_puts_the_factory_into_the_registry()
    {
        //Act
        CodeBrixVideoEncodingAv1.Register();

        //Assert
        CodeBrixVideoEncodingAv1.IsRegistered.Should().BeTrue();
        VideoEncoders.IsRegistered(VideoCodecIds.Av1).Should().BeTrue();
        VideoEncoders.Get(VideoCodecIds.Av1).Should().BeSameAs(CodeBrixVideoEncodingAv1.Factory);
        VideoEncoders.Get("AV1").Should().BeSameAs(CodeBrixVideoEncodingAv1.Factory);
    }

    [Fact]
    public void Register_twice_registers_once()
    {
        //Act
        CodeBrixVideoEncodingAv1.Register();
        CodeBrixVideoEncodingAv1.Register();

        //Assert
        VideoEncoders.RegisteredFactories.Count(f => ReferenceEquals(f, CodeBrixVideoEncodingAv1.Factory)).Should().Be(1);
    }

    [Fact]
    public void the_registered_factory_creates_working_encoders()
    {
        //Arrange
        CodeBrixVideoEncodingAv1.Register();

        //Act
        using IVideoEncoder encoder = VideoEncoders.Get(VideoCodecIds.Av1).Create(EncodeRun.Request());
        int packets = EncodeRun.EncodeAll(encoder, EncodeRun.Pictures(CodeBrix.VideoProcessing.Core.Frames.VideoPixelLayout.I420, 8, 3)).Count;

        //Assert
        packets.Should().Be(3);
    }

    [Fact]
    public void Factory_is_a_single_instance() => CodeBrixVideoEncodingAv1.Factory.Should().BeSameAs(CodeBrixVideoEncodingAv1.Factory);

    [Fact]
    public void the_native_library_is_available_and_reports_its_version()
    {
        //Act
        bool available = CodeBrixVideoEncodingAv1.IsNativeAvailable;

        //Assert
        available.Should().BeTrue();
        CodeBrixVideoEncodingAv1.NativeLoadError.Should().BeNull();
        CodeBrixVideoEncodingAv1.NativeLibraryVersion.Should().Contain("3.");
        CodeBrixVideoEncodingAv1.NativeLibraryPath.Should().Contain("codebrix_av1");
    }

    [Fact]
    public void EnsureLoaded_is_idempotent()
    {
        //Act
        CodeBrixVideoEncodingAv1.EnsureLoaded();
        CodeBrixVideoEncodingAv1.EnsureLoaded();

        //Assert
        CodeBrixVideoEncodingAv1.IsNativeAvailable.Should().BeTrue();
    }
}
