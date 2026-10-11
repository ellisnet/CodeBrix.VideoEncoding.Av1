using System;
using CodeBrix.VideoEncoding.Av1.Internal;
using CodeBrix.VideoProcessing.Core.Encoding;

namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// The AV1 <see cref="IVideoEncoderFactory" /> - what <see cref="CodeBrixVideoEncodingAv1.Register" /> puts into
/// the CodeBrix.VideoProcessing.Core encoder registry under <see cref="VideoCodecIds.Av1" />.
/// </summary>
/// <remarks>
/// <para>
/// Encoders made here use <see cref="DefaultOptions" /> for their AV1-specific settings; per-stream AV1 settings
/// that the request cannot carry go through the request's <c>CodecOptions</c> (libaom option names), or through
/// an <see cref="Av1Encoder" /> constructed directly.
/// </para>
/// <para>
/// <see cref="Create" /> checks the request completely and STARTS the encoder, so a refused setting or codec
/// option surfaces from <see cref="Create" />. The encoder's <see cref="IVideoEncoder.CodecConfigurationRecord" />
/// follows with the first packet, as the contract allows.
/// </para>
/// <para>Safe to use from several threads at once; the encoders it creates are not.</para>
/// </remarks>
public sealed class Av1EncoderFactory : IVideoEncoderFactory
{
    private readonly Av1EncoderOptions defaultOptions;

    /// <summary>Creates a factory whose encoders use the default <see cref="Av1EncoderOptions" />.</summary>
    public Av1EncoderFactory()
        : this(null)
    {
    }

    /// <summary>Creates a factory whose encoders use the given AV1-specific options.</summary>
    /// <param name="defaultOptions">The options every encoder starts from (copied now), or null for the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public Av1EncoderFactory(Av1EncoderOptions defaultOptions)
    {
        Av1EncoderOptions copy = (defaultOptions ?? new Av1EncoderOptions()).Clone();
        copy.Validate();
        this.defaultOptions = copy;
    }

    /// <summary>"av1" (<see cref="VideoCodecIds.Av1" />).</summary>
    public string CodecId => VideoCodecIds.Av1;

    /// <summary>"AV1 (libaom)".</summary>
    public string DisplayName => "AV1 (libaom)";

    /// <summary>
    /// I420, I444 and Gray frames at 8, 10 or 12 bits; lossless coding; forced key frames. NV12 and RGBA are not
    /// accepted, and I420 needs an even width and height (<see cref="Create" /> refuses odd I420 sizes).
    /// </summary>
    public VideoEncoderCapabilities Capabilities => Av1RequestMapper.Capabilities;

    /// <summary>A copy of the AV1-specific options the encoders this factory creates use.</summary>
    public Av1EncoderOptions DefaultOptions => defaultOptions.Clone();

    /// <inheritdoc />
    /// <returns>A started <see cref="Av1Encoder" />.</returns>
    /// <exception cref="Av1EncoderException">The native library could not be loaded, or the encoder refused the settings.</exception>
    public IVideoEncoder Create(VideoEncoderRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        Av1Encoder encoder = new Av1Encoder(request, defaultOptions);
        try
        {
            encoder.Start();
        }
        catch
        {
            encoder.Dispose();
            throw;
        }

        return encoder;
    }

    /// <inheritdoc />
    public override string ToString() => $"{DisplayName} [{CodecId}]: {Capabilities}";
}
