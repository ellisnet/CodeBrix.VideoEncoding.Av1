using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.VideoEncoding.Av1.Internal;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1RequestMapperTests
{
    private static VideoEncoderRequest Request() => new VideoEncoderRequest
    {
        Width = 320,
        Height = 180,
        FrameRate = new FrameRate(30000, 1001),
    };

    private static Av1NativeSettings Map(VideoEncoderRequest request, Av1EncoderOptions options = null, int processors = 8) =>
        Av1RequestMapper.Map(request, options ?? new Av1EncoderOptions(), processors);

    private static int Int(Av1NativeSettings settings, int option)
    {
        settings.TryGetInt(option, out int value).Should().BeTrue();
        return value;
    }

    private static bool Has(Av1NativeSettings settings, int option) => settings.TryGetInt(option, out int _);

    [Theory]
    [InlineData(VideoPixelLayout.I420, Av1Constants.LayoutI420)]
    [InlineData(VideoPixelLayout.I444, Av1Constants.LayoutI444)]
    [InlineData(VideoPixelLayout.Gray, Av1Constants.LayoutMonochrome)]
    public void Map_maps_the_layout(VideoPixelLayout layout, int expected)
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.PixelLayout = layout;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        settings.Layout.Should().Be(expected);
    }

    [Theory]
    [InlineData(VideoPixelLayout.Nv12)]
    [InlineData(VideoPixelLayout.Rgba)]
    public void Map_refuses_nv12_and_rgba(VideoPixelLayout layout)
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.PixelLayout = layout;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*I420, I444 or Gray*");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void Map_passes_the_bit_depth(int bitDepth)
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.BitDepth = bitDepth;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        settings.BitDepth.Should().Be(bitDepth);
    }

    [Fact]
    public void Map_uses_the_inverse_frame_rate_as_timebase()
    {
        //Act
        Av1NativeSettings settings = Map(Request());

        //Assert
        settings.TimebaseNumerator.Should().Be(1001);
        settings.TimebaseDenominator.Should().Be(30000);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(16, 20)]
    [InlineData(20, 25)]
    [InlineData(23, 28)]
    [InlineData(51, 63)]
    public void ToCqLevel_maps_linearly(int level, int expected) => Av1RequestMapper.ToCqLevel(level).Should().Be(expected);

    [Fact]
    public void ToCqLevel_is_monotonic_and_covers_the_range()
    {
        //Act
        int[] levels = Enumerable.Range(0, 52).Select(Av1RequestMapper.ToCqLevel).ToArray();

        //Assert
        levels.First().Should().Be(0);
        levels.Last().Should().Be(63);
        for (int i = 1; i < levels.Length; i++) (levels[i] > levels[i - 1]).Should().BeTrue();
    }

    [Fact]
    public void Map_sets_constant_quality_and_cq_level()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.ConstantQuality(16);

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptRateMode).Should().Be(Av1Constants.RateConstantQuality);
        Int(settings, Av1Constants.OptQuality).Should().Be(20);
        Has(settings, Av1Constants.OptLossless).Should().BeFalse();
    }

    [Fact]
    public void Map_leaves_cq_level_unset_for_the_default_quality()
    {
        //Act
        Av1NativeSettings settings = Map(Request());

        //Assert
        Int(settings, Av1Constants.OptRateMode).Should().Be(Av1Constants.RateConstantQuality);
        Has(settings, Av1Constants.OptQuality).Should().BeFalse();
    }

    [Fact]
    public void Map_turns_a_target_bitrate_into_variable_bitrate()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.TargetBitrate(1500);

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptRateMode).Should().Be(Av1Constants.RateVariableBitrate);
        Int(settings, Av1Constants.OptTargetBitrateKbps).Should().Be(1500);
    }

    [Fact]
    public void Map_honours_constant_bitrate_from_the_options()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.TargetBitrate(800);

        //Act
        Av1NativeSettings settings = Map(request, new Av1EncoderOptions { RateMode = Av1RateMode.ConstantBitrate });

        //Assert
        Int(settings, Av1Constants.OptRateMode).Should().Be(Av1Constants.RateConstantBitrate);
        Int(settings, Av1Constants.OptTargetBitrateKbps).Should().Be(800);
    }

    [Fact]
    public void Map_sets_constrained_quality_with_level_and_cap()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.ConstantQuality(30);

        //Act
        Av1NativeSettings settings = Map(request, new Av1EncoderOptions { RateMode = Av1RateMode.ConstrainedQuality, TargetBitrateKbps = 2000 });

        //Assert
        Int(settings, Av1Constants.OptRateMode).Should().Be(Av1Constants.RateConstrainedQuality);
        Int(settings, Av1Constants.OptQuality).Should().Be(Av1RequestMapper.ToCqLevel(30));
        Int(settings, Av1Constants.OptTargetBitrateKbps).Should().Be(2000);
    }

    [Theory]
    [InlineData(Av1RateMode.ConstrainedQuality)]
    [InlineData(Av1RateMode.VariableBitrate)]
    [InlineData(Av1RateMode.ConstantBitrate)]
    public void Map_refuses_a_bitrate_mode_without_a_bitrate(Av1RateMode mode)
    {
        //Act
        Action act = () => Map(Request(), new Av1EncoderOptions { RateMode = mode });

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*needs a bitrate*");
    }

    [Fact]
    public void Map_refuses_a_quality_level_with_a_pure_bitrate_mode()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.ConstantQuality(20);

        //Act
        Action act = () => Map(request, new Av1EncoderOptions { RateMode = Av1RateMode.VariableBitrate, TargetBitrateKbps = 1000 });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_refuses_a_target_bitrate_with_constant_quality_mode()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Quality = VideoQuality.TargetBitrate(1000);

        //Act
        Action act = () => Map(request, new Av1EncoderOptions { RateMode = Av1RateMode.ConstantQuality });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_sets_lossless_without_quality()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Lossless = true;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptLossless).Should().Be(1);
        Has(settings, Av1Constants.OptQuality).Should().BeFalse();
        Has(settings, Av1Constants.OptRateMode).Should().BeFalse();
    }

    [Fact]
    public void Map_refuses_lossless_with_a_quality()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Lossless = true;
        request.Quality = VideoQuality.ConstantQuality(10);

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_refuses_lossless_with_a_bitrate_mode()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Lossless = true;

        //Act
        Action act = () => Map(request, new Av1EncoderOptions { RateMode = Av1RateMode.ConstantBitrate, TargetBitrateKbps = 100 });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_sets_the_default_cpu_used_for_speed_zero()
        => Int(Map(Request()), Av1Constants.OptCpuUsed).Should().Be(6);

    [Fact]
    public void Map_passes_speed_as_cpu_used()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Speed = 7;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptCpuUsed).Should().Be(7);
    }

    [Fact]
    public void Map_lets_options_cpu_used_win_and_reach_zero()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Speed = 7;

        //Act
        Av1NativeSettings settings = Map(request, new Av1EncoderOptions { CpuUsed = 0 });

        //Assert
        Int(settings, Av1Constants.OptCpuUsed).Should().Be(0);
    }

    [Fact]
    public void Map_refuses_speed_above_nine()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Speed = 10;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*cpu-used*");
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(32, 16)]
    [InlineData(1, 1)]
    public void Map_derives_threads_from_the_processor_count(int processors, int expected)
        => Int(Map(Request(), null, processors), Av1Constants.OptThreads).Should().Be(expected);

    [Fact]
    public void Map_passes_explicit_threads()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Threads = 40;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptThreads).Should().Be(40);
    }

    [Fact]
    public void Map_refuses_more_than_64_threads()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Threads = 65;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Map_leaves_key_frame_distance_unset_by_default()
        => Has(Map(Request()), Av1Constants.OptKeyFrameMaxDistance).Should().BeFalse();

    [Fact]
    public void Map_passes_the_key_frame_interval()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.MaxKeyFrameInterval = 120;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptKeyFrameMaxDistance).Should().Be(120);
    }

    [Fact]
    public void Map_refuses_a_key_frame_interval_above_65535()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.MaxKeyFrameInterval = 70000;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Map_refuses_sizes_beyond_the_av1_limit()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Width = 70000;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Map_writes_the_default_bt709_limited_colour_tags()
    {
        //Act
        Av1NativeSettings settings = Map(Request());

        //Assert
        Int(settings, Av1Constants.OptColorPrimaries).Should().Be(1);
        Int(settings, Av1Constants.OptTransferCharacteristics).Should().Be(1);
        Int(settings, Av1Constants.OptMatrixCoefficients).Should().Be(1);
        Int(settings, Av1Constants.OptColorRange).Should().Be(0);
    }

    [Fact]
    public void Map_writes_full_range_unspecified_tags_for_an_alpha_stream()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.PixelLayout = VideoPixelLayout.Gray;
        request.ColorRange = VideoColorRange.Full;
        request.ColorPrimaries = VideoColorPrimaries.Unspecified;
        request.ColorTransfer = VideoColorTransfer.Unspecified;
        request.ColorMatrix = VideoColorMatrix.Unspecified;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptColorRange).Should().Be(1);
        Int(settings, Av1Constants.OptColorPrimaries).Should().Be(2);
        Int(settings, Av1Constants.OptTransferCharacteristics).Should().Be(2);
        Int(settings, Av1Constants.OptMatrixCoefficients).Should().Be(2);
    }

    [Fact]
    public void Map_refuses_the_rgb_matrix_with_subsampled_chroma()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.ColorMatrix = VideoColorMatrix.Rgb;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*I444*");
    }

    [Fact]
    public void Map_allows_the_rgb_matrix_for_i444()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.PixelLayout = VideoPixelLayout.I444;
        request.ColorMatrix = VideoColorMatrix.Rgb;

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        Int(settings, Av1Constants.OptMatrixCoefficients).Should().Be(0);
    }

    [Fact]
    public void Map_sets_chroma_sample_position_for_i420_only()
    {
        //Arrange
        Av1EncoderOptions options = new Av1EncoderOptions { ChromaSamplePosition = Av1ChromaSamplePosition.Colocated };
        VideoEncoderRequest gray = Request();
        gray.PixelLayout = VideoPixelLayout.Gray;

        //Act
        Av1NativeSettings i420 = Map(Request(), options);
        Av1NativeSettings mono = Map(gray, options);

        //Assert
        Int(i420, Av1Constants.OptChromaSamplePosition).Should().Be(2);
        Has(mono, Av1Constants.OptChromaSamplePosition).Should().BeFalse();
    }

    [Fact]
    public void Map_passes_tiles_lag_and_row_mt_from_the_options()
    {
        //Act
        Av1NativeSettings settings = Map(Request(), new Av1EncoderOptions { TileColumnsLog2 = 2, TileRowsLog2 = 1, LagInFrames = 0, RowMultithreading = false });

        //Assert
        Int(settings, Av1Constants.OptTileColumnsLog2).Should().Be(2);
        Int(settings, Av1Constants.OptTileRowsLog2).Should().Be(1);
        Int(settings, Av1Constants.OptLagInFrames).Should().Be(0);
        Int(settings, Av1Constants.OptRowMultithreading).Should().Be(0);
    }

    [Fact]
    public void Map_leaves_tiles_and_lag_unset_by_default()
    {
        //Act
        Av1NativeSettings settings = Map(Request());

        //Assert
        Has(settings, Av1Constants.OptTileColumnsLog2).Should().BeFalse();
        Has(settings, Av1Constants.OptTileRowsLog2).Should().BeFalse();
        Has(settings, Av1Constants.OptLagInFrames).Should().BeFalse();
    }

    [Fact]
    public void Map_passes_the_usage()
        => Map(Request(), new Av1EncoderOptions { Usage = Av1EncoderUsage.AllIntra }).Usage.Should().Be(2);

    [Fact]
    public void Map_passes_codec_options_in_order()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.CodecOptions["tune"] = "psnr";
        request.CodecOptions["enable-tpl-model"] = "0";
        request.CodecOptions["sharpness"] = "2";

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        settings.StringOptions.Select(o => o.Key).Should().ContainInOrder("tune", "enable-tpl-model", "sharpness");
        settings.StringOptions[0].Value.Should().Be("psnr");
    }

    [Theory]
    [InlineData("--tune", "psnr")]
    [InlineData("tune level", "psnr")]
    [InlineData("tüne", "psnr")]
    [InlineData("tune", "")]
    [InlineData("tune", "psénr")]
    public void Map_refuses_codec_options_that_are_not_ascii_option_names(string name, string value)
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.CodecOptions[name] = value;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_validates_the_request_first()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.FrameRate = default;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_validates_the_options()
    {
        //Act
        Action act = () => Map(Request(), new Av1EncoderOptions { CpuUsed = 12 });

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Capabilities_match_the_contract()
    {
        //Act
        VideoEncoderCapabilities capabilities = Av1RequestMapper.Capabilities;

        //Assert
        capabilities.PixelLayouts.Should().Be(VideoEncoderPixelLayouts.I420 | VideoEncoderPixelLayouts.I444 | VideoEncoderPixelLayouts.Gray);
        capabilities.BitDepths.Should().Be(VideoEncoderBitDepths.Depth8 | VideoEncoderBitDepths.Depth10 | VideoEncoderBitDepths.Depth12);
        capabilities.SupportsLossless.Should().BeTrue();
        capabilities.SupportsForcedKeyFrames.Should().BeTrue();
    }

    [Fact]
    public void Map_turns_automatic_key_frames_off()
    {
        //Act
        Av1NativeSettings settings = Map(Request(), new Av1EncoderOptions { AutomaticKeyFrames = false });

        //Assert
        Int(settings, Av1Constants.OptAutomaticKeyFrames).Should().Be(0);
        Has(settings, Av1Constants.OptKeyFrameMaxDistance).Should().BeFalse();
    }

    [Fact]
    public void Map_leaves_automatic_key_frames_at_the_default()
        => Has(Map(Request()), Av1Constants.OptAutomaticKeyFrames).Should().BeFalse();

    [Fact]
    public void Map_refuses_an_interval_without_automatic_key_frames()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.MaxKeyFrameInterval = 30;

        //Act
        Action act = () => Map(request, new Av1EncoderOptions { AutomaticKeyFrames = false });

        //Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("MaxKeyFrameInterval");
    }

    [Fact]
    public void Map_refuses_odd_i420_sizes()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Width = 321;

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(Av1EncoderUsage.GoodQuality, 6)]
    [InlineData(Av1EncoderUsage.Realtime, 8)]
    [InlineData(Av1EncoderUsage.AllIntra, 6)]
    public void Map_uses_the_usage_default_cpu_used(Av1EncoderUsage usage, int expected)
        => Int(Map(Request(), new Av1EncoderOptions { Usage = usage }), Av1Constants.OptCpuUsed).Should().Be(expected);

    [Theory]
    [InlineData(Av1EncoderUsage.GoodQuality, 2, -1, 2)]
    [InlineData(Av1EncoderUsage.Realtime, 9, -1, 9)]
    [InlineData(Av1EncoderUsage.AllIntra, 4, 0, 0)]
    [InlineData(Av1EncoderUsage.GoodQuality, 0, 1, 1)]
    public void Map_lets_explicit_speed_or_cpu_used_override_the_default(Av1EncoderUsage usage, int speed, int cpuUsed, int expected)
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Speed = speed;

        //Act
        Av1NativeSettings settings = Map(request, new Av1EncoderOptions { Usage = usage, CpuUsed = cpuUsed });

        //Assert
        Int(settings, Av1Constants.OptCpuUsed).Should().Be(expected);
    }

    [Fact]
    public void Map_leaves_cpu_used_to_the_caller_for_lossless()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Lossless = true;

        //Act
        Av1NativeSettings settings = Map(request, new Av1EncoderOptions { CpuUsed = 0 });

        //Assert
        Int(settings, Av1Constants.OptCpuUsed).Should().Be(0);
    }

    [Fact]
    public void Map_disables_onesided_compound_prediction_for_lossless_only()
    {
        //Arrange
        VideoEncoderRequest lossless = Request();
        lossless.Lossless = true;
        lossless.CodecOptions["tune"] = "psnr";

        //Act
        Av1NativeSettings losslessSettings = Map(lossless);
        Av1NativeSettings lossySettings = Map(Request());

        //Assert
        losslessSettings.StringOptions.Select(o => o.Key).Should().ContainInOrder("enable-onesided-comp", "tune");
        losslessSettings.StringOptions[0].Value.Should().Be("0");
        lossySettings.StringOptions.Should().BeEmpty();
    }

    [Fact]
    public void Map_refuses_switching_onesided_compound_prediction_back_on_for_lossless()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.Lossless = true;
        request.CodecOptions["enable-onesided-comp"] = "1";

        //Act
        Action act = () => Map(request);

        //Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("CodecOptions");
    }

    [Fact]
    public void Map_allows_onesided_compound_prediction_options_for_lossy()
    {
        //Arrange
        VideoEncoderRequest request = Request();
        request.CodecOptions["enable-onesided-comp"] = "1";

        //Act
        Av1NativeSettings settings = Map(request);

        //Assert
        settings.StringOptions.Single().Value.Should().Be("1");
    }
}
