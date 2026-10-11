using CodeBrix.VideoEncoding.Av1.Internal;
using CodeBrix.VideoProcessing.Core.Media;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1ColorMappingTests
{
    [Theory]
    [InlineData(VideoColorPrimaries.Unspecified, 2)]
    [InlineData(VideoColorPrimaries.Bt709, 1)]
    [InlineData(VideoColorPrimaries.Bt601Pal, 5)]
    [InlineData(VideoColorPrimaries.Bt601Ntsc, 6)]
    [InlineData(VideoColorPrimaries.Bt2020, 9)]
    [InlineData(VideoColorPrimaries.DciP3, 11)]
    [InlineData(VideoColorPrimaries.DisplayP3, 12)]
    [InlineData(VideoColorPrimaries.Other, 2)]
    public void ToAv1_maps_primaries(VideoColorPrimaries primaries, int expected) => Av1ColorMapping.ToAv1(primaries).Should().Be(expected);

    [Theory]
    [InlineData(VideoColorTransfer.Unspecified, 2)]
    [InlineData(VideoColorTransfer.Bt709, 1)]
    [InlineData(VideoColorTransfer.Srgb, 13)]
    [InlineData(VideoColorTransfer.Linear, 8)]
    [InlineData(VideoColorTransfer.Pq, 16)]
    [InlineData(VideoColorTransfer.Hlg, 18)]
    [InlineData(VideoColorTransfer.Other, 2)]
    public void ToAv1_maps_transfer(VideoColorTransfer transfer, int expected) => Av1ColorMapping.ToAv1(transfer).Should().Be(expected);

    [Theory]
    [InlineData(VideoColorMatrix.Unspecified, 2)]
    [InlineData(VideoColorMatrix.Bt601, 6)]
    [InlineData(VideoColorMatrix.Bt709, 1)]
    [InlineData(VideoColorMatrix.Bt2020NonConstant, 9)]
    [InlineData(VideoColorMatrix.Bt2020Constant, 10)]
    [InlineData(VideoColorMatrix.Rgb, 0)]
    [InlineData(VideoColorMatrix.Other, 2)]
    public void ToAv1_maps_matrix(VideoColorMatrix matrix, int expected) => Av1ColorMapping.ToAv1(matrix).Should().Be(expected);

    [Theory]
    [InlineData(VideoColorRange.Unspecified, 0)]
    [InlineData(VideoColorRange.Limited, 0)]
    [InlineData(VideoColorRange.Full, 1)]
    public void ToAv1_maps_range(VideoColorRange range, int expected) => Av1ColorMapping.ToAv1(range).Should().Be(expected);
}
