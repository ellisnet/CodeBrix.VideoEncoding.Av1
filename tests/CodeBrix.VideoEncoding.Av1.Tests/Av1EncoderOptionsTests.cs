using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1EncoderOptionsTests
{
    [Fact]
    public void defaults_leave_everything_to_the_encoder()
    {
        //Act
        Av1EncoderOptions options = new Av1EncoderOptions();

        //Assert
        options.Usage.Should().Be(Av1EncoderUsage.GoodQuality);
        options.CpuUsed.Should().Be(Av1EncoderOptions.Default);
        options.RateMode.Should().Be(Av1RateMode.Automatic);
        options.TargetBitrateKbps.Should().Be(0);
        options.RowMultithreading.Should().BeTrue();
        options.TileColumnsLog2.Should().Be(-1);
        options.TileRowsLog2.Should().Be(-1);
        options.LagInFrames.Should().Be(-1);
        options.ChromaSamplePosition.Should().Be(Av1ChromaSamplePosition.Unknown);
        options.AutomaticKeyFrames.Should().BeTrue();
    }

    [Fact]
    public void Validate_accepts_the_defaults() => new Av1EncoderOptions().Validate();

    [Fact]
    public void Validate_accepts_the_range_limits()
        => new Av1EncoderOptions { CpuUsed = 9, TileColumnsLog2 = 6, TileRowsLog2 = 0, LagInFrames = 48, TargetBitrateKbps = 2000000 }.Validate();

    [Theory]
    [InlineData(nameof(Av1EncoderOptions.CpuUsed), 10)]
    [InlineData(nameof(Av1EncoderOptions.CpuUsed), -2)]
    [InlineData(nameof(Av1EncoderOptions.TileColumnsLog2), 7)]
    [InlineData(nameof(Av1EncoderOptions.TileRowsLog2), -5)]
    [InlineData(nameof(Av1EncoderOptions.LagInFrames), 49)]
    [InlineData(nameof(Av1EncoderOptions.TargetBitrateKbps), -1)]
    [InlineData(nameof(Av1EncoderOptions.TargetBitrateKbps), 2000001)]
    public void Validate_refuses_out_of_range_values(string property, int value)
    {
        //Arrange
        Av1EncoderOptions options = new Av1EncoderOptions();
        typeof(Av1EncoderOptions).GetProperty(property).SetValue(options, value);

        //Act
        Action act = () => options.Validate();

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(property);
    }

    [Fact]
    public void Validate_refuses_undefined_enums()
    {
        //Arrange
        Av1EncoderOptions options = new Av1EncoderOptions { Usage = (Av1EncoderUsage)7 };

        //Act
        Action act = () => options.Validate();

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Clone_is_independent()
    {
        //Arrange
        Av1EncoderOptions options = new Av1EncoderOptions { CpuUsed = 3 };

        //Act
        Av1EncoderOptions copy = options.Clone();
        options.CpuUsed = 5;

        //Assert
        copy.CpuUsed.Should().Be(3);
    }
}
