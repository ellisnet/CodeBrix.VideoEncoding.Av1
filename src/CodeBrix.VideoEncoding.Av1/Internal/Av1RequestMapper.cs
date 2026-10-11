using System;
using System.Collections.Generic;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// Turns a Core <see cref="VideoEncoderRequest" /> plus <see cref="Av1EncoderOptions" /> into the native
/// encoder's settings, refusing everything the native side would refuse BEFORE any native call is made.
/// </summary>
/// <remarks>
/// The mapping rules (also in MAINTAINER-README.txt):
/// <list type="bullet">
/// <item>Layout I420 / I444 / Gray -> CBA_LAYOUT_I420 / I444 / MONOCHROME; Nv12 and Rgba are refused, and so are
/// I420 requests with an odd width or height (libaom loses the last chroma column; I444 and Gray accept odd sizes).</item>
/// <item>Timebase = 1 / frame rate: numerator = FrameRate.Denominator, denominator = FrameRate.Numerator; the
/// frame index is the pts.</item>
/// <item>Quality: a constant-quality level 0..51 -> cq-level round(level * 63 / 51); the default leaves the
/// encoder's cq-level; a target bitrate -> variable bitrate at that rate (unless the options choose another
/// bitrate mode). Lossless -> CBA_OPT_LOSSLESS and no quality.</item>
/// <item>Speed 0 -> the encoder's cpu-used; 1..9 -> cpu-used; Av1EncoderOptions.CpuUsed (0..9) wins over it.</item>
/// <item>Threads 0 -> min(processor count, 16); 1..64 as given.</item>
/// <item>MaxKeyFrameInterval 0 -> the encoder's default; 1..65535 -> kf-max-dist. AutomaticKeyFrames = false ->
/// CBA_OPT_AUTOMATIC_KEY_FRAMES 0 (only frame 0 and forced frames are key frames; refused with an interval).</item>
/// <item>Colour tags -> AV1 code points (<see cref="Av1ColorMapping" />), always written.</item>
/// <item>CodecOptions -> passed through by name, in the dictionary's order, after everything else.</item>
/// </list>
/// </remarks>
internal static class Av1RequestMapper
{
    /// <summary>The capabilities the encoder advertises - also what the mapper accepts.</summary>
    public static readonly VideoEncoderCapabilities Capabilities = new VideoEncoderCapabilities
    {
        PixelLayouts = VideoEncoderPixelLayouts.I420 | VideoEncoderPixelLayouts.I444 | VideoEncoderPixelLayouts.Gray,
        BitDepths = VideoEncoderBitDepths.Depth8 | VideoEncoderBitDepths.Depth10 | VideoEncoderBitDepths.Depth12,
        SupportsLossless = true,
        SupportsForcedKeyFrames = true,
    };

    /// <summary>Checks a request and options and works out the native settings.</summary>
    /// <param name="request">The request; not changed.</param>
    /// <param name="options">The options; not changed.</param>
    /// <param name="processorCount">The processor count to derive the default thread count from.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> or <paramref name="options" /> is null.</exception>
    /// <exception cref="ArgumentException">A value is out of range or a combination is invalid; the message names it.</exception>
    /// <exception cref="NotSupportedException">The layout (NV12, RGBA) is not one the encoder takes, or an I420 size is odd.</exception>
    public static Av1NativeSettings Map(VideoEncoderRequest request, Av1EncoderOptions options, int processorCount)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (options == null) throw new ArgumentNullException(nameof(options));

        request.Validate();
        if (!Capabilities.SupportsLayout(request.PixelLayout))
        {
            throw new NotSupportedException(
                $"The AV1 encoder does not accept {request.PixelLayout} frames; it takes planar I420, I444 or Gray "
                + "(monochrome) frames. Convert the frames (RGBA to YUV, NV12 to I420) before encoding.");
        }

        Capabilities.ThrowIfUnsupported(request);
        if (request.PixelLayout == VideoPixelLayout.I420 && ((request.Width & 1) != 0 || (request.Height & 1) != 0))
        {
            throw new NotSupportedException(
                $"The AV1 encoder needs an even width and height for I420 frames; the request is {request.Width}x{request.Height}. "
                + "Pad or crop to even dimensions, or use I444 or Gray, which accept odd sizes.");
        }

        options.Validate();

        if (request.Width > Av1Constants.MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(request.Width), request.Width, $"AV1 allows widths up to {Av1Constants.MaxDimension}.");
        if (request.Height > Av1Constants.MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(request.Height), request.Height, $"AV1 allows heights up to {Av1Constants.MaxDimension}.");

        Av1NativeSettings settings = new Av1NativeSettings
        {
            Width = request.Width,
            Height = request.Height,
            BitDepth = request.BitDepth,
            Layout = ToLayout(request.PixelLayout),
            TimebaseNumerator = request.FrameRate.Denominator,
            TimebaseDenominator = request.FrameRate.Numerator,
            Usage = (int)options.Usage,
        };

        MapSpeed(request, options, settings);
        MapRate(request, options, settings);
        MapThreads(request, options, processorCount, settings);

        if (request.MaxKeyFrameInterval > Av1Constants.MaxKeyFrameDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxKeyFrameInterval),
                request.MaxKeyFrameInterval,
                $"The AV1 encoder allows key-frame intervals up to {Av1Constants.MaxKeyFrameDistance} frames (0 = the encoder's default).");
        }

        if (!options.AutomaticKeyFrames)
        {
            if (request.MaxKeyFrameInterval > 0)
            {
                throw new ArgumentException(
                    "MaxKeyFrameInterval cannot be combined with Av1EncoderOptions.AutomaticKeyFrames = false (then only "
                    + "frame 0 and forced frames are key frames); force the key frames you want instead.",
                    nameof(request.MaxKeyFrameInterval));
            }

            settings.IntOptions.Add(Pair(Av1Constants.OptAutomaticKeyFrames, 0));
        }
        else if (request.MaxKeyFrameInterval > 0)
        {
            settings.IntOptions.Add(Pair(Av1Constants.OptKeyFrameMaxDistance, request.MaxKeyFrameInterval));
        }

        MapColor(request, options, settings);
        MapCodecOptions(request.CodecOptions, settings);
        return settings;
    }

    /// <summary>Maps a Core constant-quality level (0..51) onto libaom's cq-level (0..63), linearly, rounding to nearest.</summary>
    /// <param name="qualityLevel">0..51.</param>
    /// <returns>0..63.</returns>
    public static int ToCqLevel(int qualityLevel)
    {
        if (qualityLevel < VideoQuality.BestQualityLevel || qualityLevel > VideoQuality.WorstQualityLevel)
            throw new ArgumentOutOfRangeException(nameof(qualityLevel), qualityLevel, "The quality level must be between 0 and 51.");

        return ((qualityLevel * Av1Constants.MaxCqLevel) + (VideoQuality.WorstQualityLevel / 2)) / VideoQuality.WorstQualityLevel;
    }

    /// <summary>The <c>CBA_LAYOUT_*</c> value for a Core layout the encoder takes.</summary>
    /// <param name="layout">I420, I444 or Gray.</param>
    /// <returns>The native layout.</returns>
    /// <exception cref="NotSupportedException">Any other layout.</exception>
    public static int ToLayout(VideoPixelLayout layout) => layout switch
    {
        VideoPixelLayout.I420 => Av1Constants.LayoutI420,
        VideoPixelLayout.I444 => Av1Constants.LayoutI444,
        VideoPixelLayout.Gray => Av1Constants.LayoutMonochrome,
        _ => throw new NotSupportedException($"The AV1 encoder does not accept {layout} frames; it takes I420, I444 or Gray frames."),
    };

    private static void MapSpeed(VideoEncoderRequest request, Av1EncoderOptions options, Av1NativeSettings settings)
    {
        if (request.Speed > Av1EncoderOptions.MaxCpuUsed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Speed),
                request.Speed,
                $"For AV1, Speed is libaom's cpu-used: 1 (slowest, best) to {Av1EncoderOptions.MaxCpuUsed} (fastest), or 0 for the encoder's default.");
        }

        int cpuUsed = options.CpuUsed != Av1EncoderOptions.Default ? options.CpuUsed : (request.Speed > 0 ? request.Speed : -1);
        if (cpuUsed >= 0) settings.IntOptions.Add(Pair(Av1Constants.OptCpuUsed, cpuUsed));
    }

    private static void MapRate(VideoEncoderRequest request, Av1EncoderOptions options, Av1NativeSettings settings)
    {
        VideoQuality quality = request.Quality;

        if (request.Lossless)
        {
            if (options.RateMode != Av1RateMode.Automatic && options.RateMode != Av1RateMode.ConstantQuality)
            {
                throw new ArgumentException(
                    $"A lossless request takes no bitrate; leave Av1EncoderOptions.RateMode at Automatic (it is {options.RateMode}).",
                    nameof(options));
            }

            settings.IntOptions.Add(Pair(Av1Constants.OptLossless, 1));
            return;
        }

        Av1RateMode mode = options.RateMode;
        if (mode == Av1RateMode.Automatic)
            mode = quality.Mode == VideoQualityMode.TargetBitrate ? Av1RateMode.VariableBitrate : Av1RateMode.ConstantQuality;

        int bitrate = options.TargetBitrateKbps > 0
            ? options.TargetBitrateKbps
            : (quality.Mode == VideoQualityMode.TargetBitrate ? quality.BitrateKilobitsPerSecond : 0);

        if (bitrate > Av1EncoderOptions.MaxTargetBitrateKbps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Quality),
                bitrate,
                $"The AV1 encoder allows bitrates up to {Av1EncoderOptions.MaxTargetBitrateKbps} kbit/s.");
        }

        switch (mode)
        {
            case Av1RateMode.ConstantQuality:
                if (quality.Mode == VideoQualityMode.TargetBitrate)
                {
                    throw new ArgumentException(
                        "The request asks for a target bitrate but Av1EncoderOptions.RateMode is ConstantQuality; use a "
                        + "constant-quality request, or a bitrate mode.",
                        nameof(options));
                }

                settings.IntOptions.Add(Pair(Av1Constants.OptRateMode, Av1Constants.RateConstantQuality));
                AddQualityLevel(quality, settings);
                break;

            case Av1RateMode.ConstrainedQuality:
                RequireBitrate(mode, bitrate);
                settings.IntOptions.Add(Pair(Av1Constants.OptRateMode, Av1Constants.RateConstrainedQuality));
                AddQualityLevel(quality, settings);
                settings.IntOptions.Add(Pair(Av1Constants.OptTargetBitrateKbps, bitrate));
                break;

            default:
                RequireBitrate(mode, bitrate);
                if (quality.Mode == VideoQualityMode.ConstantQuality)
                {
                    throw new ArgumentException(
                        $"A constant-quality level cannot be combined with Av1EncoderOptions.RateMode {mode}; use "
                        + "ConstrainedQuality for a quality level with a bitrate cap.",
                        nameof(options));
                }

                settings.IntOptions.Add(Pair(
                    Av1Constants.OptRateMode,
                    mode == Av1RateMode.VariableBitrate ? Av1Constants.RateVariableBitrate : Av1Constants.RateConstantBitrate));
                settings.IntOptions.Add(Pair(Av1Constants.OptTargetBitrateKbps, bitrate));
                break;
        }
    }

    private static void AddQualityLevel(VideoQuality quality, Av1NativeSettings settings)
    {
        if (quality.Mode == VideoQualityMode.ConstantQuality)
            settings.IntOptions.Add(Pair(Av1Constants.OptQuality, ToCqLevel(quality.QualityLevel)));
    }

    private static void RequireBitrate(Av1RateMode mode, int bitrate)
    {
        if (bitrate > 0) return;
        throw new ArgumentException(
            $"Av1EncoderOptions.RateMode {mode} needs a bitrate: set Av1EncoderOptions.TargetBitrateKbps, or give the "
            + "request a VideoQuality.TargetBitrate.",
            "options");
    }

    private static void MapThreads(VideoEncoderRequest request, Av1EncoderOptions options, int processorCount, Av1NativeSettings settings)
    {
        if (request.Threads > Av1Constants.MaxThreads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Threads),
                request.Threads,
                $"The AV1 encoder allows up to {Av1Constants.MaxThreads} threads (0 = the processor count, capped at {Av1Constants.DefaultThreadCap}).");
        }

        int threads = request.Threads > 0 ? request.Threads : Math.Clamp(processorCount, 1, Av1Constants.DefaultThreadCap);
        settings.IntOptions.Add(Pair(Av1Constants.OptThreads, threads));
        settings.IntOptions.Add(Pair(Av1Constants.OptRowMultithreading, options.RowMultithreading ? 1 : 0));
        if (options.TileColumnsLog2 != Av1EncoderOptions.Default) settings.IntOptions.Add(Pair(Av1Constants.OptTileColumnsLog2, options.TileColumnsLog2));
        if (options.TileRowsLog2 != Av1EncoderOptions.Default) settings.IntOptions.Add(Pair(Av1Constants.OptTileRowsLog2, options.TileRowsLog2));
        if (options.LagInFrames != Av1EncoderOptions.Default) settings.IntOptions.Add(Pair(Av1Constants.OptLagInFrames, options.LagInFrames));
    }

    private static void MapColor(VideoEncoderRequest request, Av1EncoderOptions options, Av1NativeSettings settings)
    {
        int matrix = Av1ColorMapping.ToAv1(request.ColorMatrix);
        if (matrix == Av1ColorMapping.MatrixIdentity && request.PixelLayout != VideoPixelLayout.I444)
        {
            throw new ArgumentException(
                "AV1 allows the RGB (identity) matrix only for I444 frames - chroma cannot be subsampled when the "
                + $"planes are G, B and R. The request is {request.PixelLayout}.",
                nameof(request.ColorMatrix));
        }

        settings.IntOptions.Add(Pair(Av1Constants.OptColorPrimaries, Av1ColorMapping.ToAv1(request.ColorPrimaries)));
        settings.IntOptions.Add(Pair(Av1Constants.OptTransferCharacteristics, Av1ColorMapping.ToAv1(request.ColorTransfer)));
        settings.IntOptions.Add(Pair(Av1Constants.OptMatrixCoefficients, matrix));
        settings.IntOptions.Add(Pair(Av1Constants.OptColorRange, Av1ColorMapping.ToAv1(request.ColorRange)));

        if (request.PixelLayout == VideoPixelLayout.I420 && options.ChromaSamplePosition != Av1ChromaSamplePosition.Unknown)
            settings.IntOptions.Add(Pair(Av1Constants.OptChromaSamplePosition, (int)options.ChromaSamplePosition));
    }

    private static void MapCodecOptions(IDictionary<string, string> codecOptions, Av1NativeSettings settings)
    {
        foreach (KeyValuePair<string, string> option in codecOptions)
        {
            if (!IsOptionName(option.Key))
            {
                throw new ArgumentException(
                    $"'{option.Key}' is not a libaom option name: names are ASCII letters, digits, '-' and '_' (for "
                    + "example \"tune\" or \"enable-tpl-model\"), without leading dashes.",
                    nameof(VideoEncoderRequest.CodecOptions));
            }

            if (!IsOptionValue(option.Value))
            {
                throw new ArgumentException(
                    $"The value of codec option '{option.Key}' must be non-empty printable ASCII.",
                    nameof(VideoEncoderRequest.CodecOptions));
            }

            settings.StringOptions.Add(new KeyValuePair<string, string>(option.Key, option.Value));
        }
    }

    private static bool IsOptionName(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '-') return false;
        foreach (char c in name)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }

        return true;
    }

    private static bool IsOptionValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char c in value)
        {
            if (c < 0x20 || c > 0x7E) return false;
        }

        return true;
    }

    private static KeyValuePair<int, int> Pair(int option, int value) => new KeyValuePair<int, int>(option, value);
}
