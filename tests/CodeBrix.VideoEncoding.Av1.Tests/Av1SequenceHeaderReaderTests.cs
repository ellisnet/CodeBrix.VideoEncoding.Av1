using System;
using CodeBrix.VideoEncoding.Av1.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1SequenceHeaderReaderTests
{
    [Fact]
    public void ReadObu_reads_a_main_profile_8_bit_420_header()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 0, Levels = new[] { 8 }, MaxWidth = 320, MaxHeight = 180, ChromaSamplePosition = 1 }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.Profile.Should().Be(0);
        info.StillPicture.Should().BeFalse();
        info.LevelIndex.Should().Be(8);
        info.LevelName.Should().Be("4.0");
        info.Tier.Should().Be(0);
        info.BitDepth.Should().Be(8);
        info.Monochrome.Should().BeFalse();
        info.SubsamplingX.Should().Be(1);
        info.SubsamplingY.Should().Be(1);
        info.ChromaSamplePosition.Should().Be(1);
        info.MaxFrameWidth.Should().Be(320);
        info.MaxFrameHeight.Should().Be(180);
        info.ColorDescriptionPresent.Should().BeFalse();
        info.ColorPrimaries.Should().Be(2);
        info.FullRange.Should().BeFalse();
    }

    [Fact]
    public void ReadObu_reads_a_10_bit_monochrome_full_range_header()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 0, HighBitDepth = true, Monochrome = true, FullRange = true, ColorDescription = true, Primaries = 2, Transfer = 2, Matrix = 2 }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.BitDepth.Should().Be(10);
        info.Monochrome.Should().BeTrue();
        info.FullRange.Should().BeTrue();
        info.SubsamplingX.Should().Be(1);
        info.SubsamplingY.Should().Be(1);
        info.ChromaSamplePosition.Should().Be(0);
    }

    [Fact]
    public void ReadObu_reads_a_high_profile_444_header_with_colour()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 1, HighBitDepth = true, ColorDescription = true, Primaries = 9, Transfer = 16, Matrix = 9, FullRange = false }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.Profile.Should().Be(1);
        info.BitDepth.Should().Be(10);
        info.Monochrome.Should().BeFalse();
        info.SubsamplingX.Should().Be(0);
        info.SubsamplingY.Should().Be(0);
        info.ColorPrimaries.Should().Be(9);
        info.TransferCharacteristics.Should().Be(16);
        info.MatrixCoefficients.Should().Be(9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void ReadObu_reads_professional_12_bit_subsampling(int subsamplingX, int subsamplingY)
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 2, HighBitDepth = true, TwelveBit = true, SubsamplingX = subsamplingX, SubsamplingY = subsamplingY, ChromaSamplePosition = 2 }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.Profile.Should().Be(2);
        info.BitDepth.Should().Be(12);
        info.SubsamplingX.Should().Be(subsamplingX);
        info.SubsamplingY.Should().Be(subsamplingY);
        info.ChromaSamplePosition.Should().Be(subsamplingX == 1 && subsamplingY == 1 ? 2 : 0);
    }

    [Fact]
    public void ReadObu_reads_a_professional_10_bit_422_header()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 2, HighBitDepth = true, TwelveBit = false }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.BitDepth.Should().Be(10);
        info.SubsamplingX.Should().Be(1);
        info.SubsamplingY.Should().Be(0);
    }

    [Fact]
    public void ReadObu_reads_the_srgb_identity_special_case()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 1, ColorDescription = true, Primaries = 1, Transfer = 13, Matrix = 0, FilmGrain = true }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.FullRange.Should().BeTrue();
        info.SubsamplingX.Should().Be(0);
        info.SubsamplingY.Should().Be(0);
        info.MatrixCoefficients.Should().Be(0);
        info.FilmGrainParamsPresent.Should().BeTrue();
    }

    [Fact]
    public void ReadObu_reads_timing_decoder_model_and_display_delay_info()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder
        {
            TimingInfo = true,
            EqualPictureInterval = true,
            DecoderModelInfo = true,
            InitialDisplayDelay = true,
            Levels = new[] { 13, 4 },
            Tiers = new[] { 1, 0 },
            FrameIdNumbers = true,
            MaxWidth = 1920,
            MaxHeight = 1080,
        }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.OperatingPointCount.Should().Be(2);
        info.LevelIndex.Should().Be(13);
        info.LevelName.Should().Be("5.1");
        info.Tier.Should().Be(1);
        info.MaxFrameWidth.Should().Be(1920);
        info.MaxFrameHeight.Should().Be(1080);
        info.BitDepth.Should().Be(8);
    }

    [Fact]
    public void ReadObu_reads_screen_content_and_integer_mv_branches()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { ChooseScreenContentTools = false, ForceScreenContentTools = true, ChooseIntegerMv = false, EnableOrderHint = false, ColorDescription = true, Primaries = 6, Transfer = 6, Matrix = 6 }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.ColorPrimaries.Should().Be(6);
        info.MatrixCoefficients.Should().Be(6);
    }

    [Fact]
    public void ReadObu_reads_a_reduced_still_picture_header()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { StillPicture = true, Reduced = true, Levels = new[] { 5 } }.Obu();

        //Act
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        info.StillPicture.Should().BeTrue();
        info.ReducedStillPictureHeader.Should().BeTrue();
        info.LevelIndex.Should().Be(5);
        info.OperatingPointCount.Should().Be(1);
    }

    [Fact]
    public void ReadObu_skips_an_extension_header_and_accepts_a_missing_size_field()
    {
        //Arrange
        SequenceHeaderBuilder builder = new SequenceHeaderBuilder { Levels = new[] { 9 } };

        //Act
        Av1SequenceHeaderInfo withExtension = Av1SequenceHeaderReader.ReadObu(builder.Obu(withSize: true, withExtension: true));
        Av1SequenceHeaderInfo withoutSize = Av1SequenceHeaderReader.ReadObu(builder.Obu(withSize: false));

        //Assert
        withExtension.LevelIndex.Should().Be(9);
        withoutSize.LevelIndex.Should().Be(9);
    }

    [Fact]
    public void ReadObu_refuses_another_obu_type()
    {
        //Arrange
        byte[] obu = BitWriter.Obu(2, Array.Empty<byte>());

        //Act
        Action act = () => Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        act.Should().Throw<FormatException>().WithMessage("*type 2*");
    }

    [Fact]
    public void ReadObu_refuses_a_truncated_header()
    {
        //Arrange
        byte[] payload = new SequenceHeaderBuilder().Payload();
        byte[] obu = BitWriter.Obu(1, payload.AsSpan(0, 4).ToArray());

        //Act
        Action act = () => Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ReadObu_refuses_a_size_field_larger_than_the_data()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder().Obu();
        byte[] cut = obu.AsSpan(0, obu.Length - 2).ToArray();

        //Act
        Action act = () => Av1SequenceHeaderReader.ReadObu(cut);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ReadObu_refuses_the_forbidden_bit()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder().Obu();
        obu[0] |= 0x80;

        //Act
        Action act = () => Av1SequenceHeaderReader.ReadObu(obu);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ReadPayload_refuses_a_reserved_profile()
    {
        //Arrange
        byte[] payload = new BitWriter().Bits(5, 3).Bits(0, 13).ToArray();

        //Act
        Action act = () => Av1SequenceHeaderReader.ReadPayload(payload);

        //Assert
        act.Should().Throw<FormatException>();
    }
}
