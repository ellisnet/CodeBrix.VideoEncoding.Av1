using System;
using CodeBrix.VideoEncoding.Av1.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1ConfigurationRecordTests
{
    [Fact]
    public void Build_writes_marker_profile_level_and_flags_for_8_bit_420()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 0, Levels = new[] { 8 }, ChromaSamplePosition = 1 }.Obu();
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Act
        byte[] record = Av1ConfigurationRecord.Build(info, obu);

        //Assert
        record[0].Should().Be((byte)0x81);
        record[1].Should().Be((byte)((0 << 5) | 8));
        record[2].Should().Be((byte)((1 << 3) | (1 << 2) | 1));
        record[3].Should().Be((byte)0);
        record.AsSpan(4).ToArray().Should().Equal(obu);
    }

    [Fact]
    public void Build_sets_high_bitdepth_and_monochrome()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 0, HighBitDepth = true, Monochrome = true, FullRange = true, Levels = new[] { 9 } }.Obu();
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Act
        byte[] record = Av1ConfigurationRecord.Build(info, obu);

        //Assert
        record[1].Should().Be((byte)9);
        record[2].Should().Be((byte)((1 << 6) | (1 << 4) | (1 << 3) | (1 << 2)));
    }

    [Fact]
    public void Build_sets_tier_twelve_bit_and_444()
    {
        //Arrange
        byte[] obu = new SequenceHeaderBuilder { Profile = 2, HighBitDepth = true, TwelveBit = true, SubsamplingX = 0, Levels = new[] { 12 }, Tiers = new[] { 1 } }.Obu();
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu);

        //Act
        byte[] record = Av1ConfigurationRecord.Build(info, obu);

        //Assert
        record[1].Should().Be((byte)((2 << 5) | 12));
        record[2].Should().Be((byte)((1 << 7) | (1 << 6) | (1 << 5)));
    }

    [Fact]
    public void Build_refuses_an_empty_obu()
    {
        //Arrange
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(new SequenceHeaderBuilder().Obu());

        //Act
        Action act = () => Av1ConfigurationRecord.Build(info, ReadOnlySpan<byte>.Empty);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_refuses_null_info()
    {
        //Act
        Action act = () => Av1ConfigurationRecord.Build(null, new byte[] { 0x0A });

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
