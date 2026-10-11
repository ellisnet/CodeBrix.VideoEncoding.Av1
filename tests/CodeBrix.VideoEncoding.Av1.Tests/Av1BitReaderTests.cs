using System;
using CodeBrix.VideoEncoding.Av1.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1BitReaderTests
{
    [Fact]
    public void ReadBits_reads_most_significant_bit_first()
    {
        //Arrange
        Av1BitReader reader = new Av1BitReader(new byte[] { 0b1010_0110, 0xFF });

        //Act
        uint first = reader.ReadBits(3);
        uint second = reader.ReadBits(5);
        uint third = reader.ReadBits(8);

        //Assert
        first.Should().Be(5u);
        second.Should().Be(6u);
        third.Should().Be(255u);
        reader.BitPosition.Should().Be(16);
    }

    [Fact]
    public void ReadBits_reads_32_bits()
    {
        //Arrange
        Av1BitReader reader = new Av1BitReader(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        //Act
        uint value = reader.ReadBits(32);

        //Assert
        value.Should().Be(0xDEADBEEFu);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(6u)]
    [InlineData(1000u)]
    public void ReadUvlc_round_trips(uint value)
    {
        //Arrange
        byte[] data = new BitWriter().Uvlc(value).Bits(0, 8).ToArray();
        Av1BitReader reader = new Av1BitReader(data);

        //Act
        uint read = reader.ReadUvlc();

        //Assert
        read.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(300000)]
    public void ReadLeb128_round_trips(int value)
    {
        //Arrange
        Av1BitReader reader = new Av1BitReader(BitWriter.Leb128(value));

        //Act
        ulong read = reader.ReadLeb128();

        //Assert
        read.Should().Be((ulong)value);
    }

    [Fact]
    public void ReadBits_throws_at_the_end_of_the_data()
    {
        //Act
        Action act = () =>
        {
            Av1BitReader reader = new Av1BitReader(new byte[] { 0x01 });
            reader.ReadBits(9);
        };

        //Assert
        act.Should().Throw<FormatException>();
    }
}
