================================================================================
AGENT-README: CodeBrix.VideoEncoding.Av1
A Guide for AI Coding Agents - CONSUMING the CodeBrix.VideoEncoding.Av1.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.VideoEncoding.Av1 is an AV1 video ENCODER for .NET. It implements the
encoder contract of CodeBrix.VideoProcessing.Core (namespace
CodeBrix.VideoProcessing.Core.Encoding): planar frames (VideoFrameData) go in,
AV1 temporal units (EncodedVideoPacket) come out, plus the av1C codec
configuration record a container stores.

It is built over libaom - the Alliance for Open Media's reference AV1 encoder,
compiled encoder-only - as a native library shipped INSIDE the package for
Windows x64 / ARM64, macOS x64 / ARM64, Linux x64 / ARM64 / RISC-V 64 and
Android 13+ (API 33) ARM64 / x64. Android has no AV1 encoder of its own at that
API level; this package is how a CodeBrix application encodes AV1 there.

What it encodes:
  - layouts I420 (4:2:0), I444 (4:4:4) and Gray (monochrome) - NOT NV12 or
    RGBA (convert first);
  - 8, 10 or 12 bits per sample (10/12-bit samples are 16-bit little-endian
    words with the value in the low bits, as everywhere in Core);
  - lossy (constant quality, constrained quality, VBR, CBR) or mathematically
    lossless;
  - key frames forced exactly where the caller asks, optionally with NO
    automatic key frames, so a colour stream and its alpha stream (a Gray,
    full-range stream) stay in lock step.

Target framework: .NET 10 or later.

INSTALLATION
============
    dotnet add package CodeBrix.VideoEncoding.Av1.BsdLicenseForever

PackageId:  CodeBrix.VideoEncoding.Av1.BsdLicenseForever
Assembly:   CodeBrix.VideoEncoding.Av1
License:    BSD-2-Clause (the native encoder: BSD-2-Clause plus the Alliance
            for Open Media Patent License 1.0; the full texts travel beside
            every native binary as LICENSE-Aom.txt and in
            THIRD-PARTY-NOTICES.txt)

Dependency (arrives automatically): CodeBrix.VideoProcessing.Core.MitLicenseForever
- the contract (VideoEncoderRequest, VideoFrameData, EncodedVideoPacket,
VideoEncoders ...). The package pins the version it was built against.

Nothing else is needed: the natives are in the package under
runtimes/<rid>/native/, and NuGet / the .NET runtime / the Android build pick
the right one. On Android set SupportedOSPlatformVersion 33 and the
RuntimeIdentifiers you ship (android-arm64, android-x64); no manual native
items are needed.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.VideoEncoding.Av1;                 // this package: Register,
                                                      // Av1Encoder, options ...
    using CodeBrix.VideoProcessing.Core.Encoding;     // VideoEncoders,
                                                      // IVideoEncoder,
                                                      // VideoEncoderRequest,
                                                      // EncodedVideoPacket,
                                                      // VideoCodecIds
    using CodeBrix.VideoProcessing.Core.Frames;       // VideoFrameData,
                                                      // VideoPlane,
                                                      // VideoPixelLayout
    using CodeBrix.VideoProcessing.Core.Media;        // FrameRate, colour enums
    using CodeBrix.VideoProcessing.Core.Output;       // VideoQuality

Everything public in this package is in the one namespace
CodeBrix.VideoEncoding.Av1. Its Interop and Internal sub-namespaces are
entirely internal.

CORE API REFERENCE
==================

CodeBrixVideoEncodingAv1  (static)
----------------------------------
    static void Register()
        Loads and checks the native encoder for this platform, then registers
        Factory with VideoEncoders under "av1". Idempotent, locked, safe from
        any thread. Throws Av1EncoderException (message lists the runtime
        identifier and every path tried) when the native cannot be loaded.
    static bool IsRegistered
    static Av1EncoderFactory Factory          the single shared factory
    static void EnsureLoaded()                load + check without registering
    static bool IsNativeAvailable             never throws
    static string NativeLoadError             null when loadable; never throws
    static string NativeLibraryVersion        the encoder library's version
                                              string (loads; throws when it
                                              cannot)
    static string NativeLibraryPath           where it was loaded from, or null
    There is NO module initializer - call Register() yourself.

Av1EncoderFactory : IVideoEncoderFactory
----------------------------------------
    Av1EncoderFactory()                        default Av1EncoderOptions
    Av1EncoderFactory(Av1EncoderOptions defaultOptions)
    string CodecId          "av1"
    string DisplayName      "AV1 (libaom)"
    VideoEncoderCapabilities Capabilities
        PixelLayouts I420 | I444 | Gray; BitDepths 8 | 10 | 12;
        SupportsLossless = true; SupportsForcedKeyFrames = true
    Av1EncoderOptions DefaultOptions           a copy
    IVideoEncoder Create(VideoEncoderRequest request)
        Validates the request and STARTS the encoder, so a refused setting or
        codec option surfaces here. Returns an Av1Encoder.
        ArgumentNullException / ArgumentException (bad request, bad codec
        option) / NotSupportedException (NV12, RGBA, odd I420 size) /
        Av1EncoderException.

Av1Encoder : IVideoEncoder
--------------------------
    Av1Encoder(VideoEncoderRequest request, Av1EncoderOptions options = null)
        Checks everything BEFORE any native call; keeps its own copies of the
        request and options. Starts lazily (first Encode) or via Start().
    VideoEncoderRequest Request; Av1EncoderOptions Options
    void Start()                    create + configure the native encoder now;
                                    second call does nothing
    void Encode(in VideoFrameData frame, bool forceKeyFrame)
    void Flush()                    end of stream; second call does nothing
    bool TryReadPacket(out EncodedVideoPacket packet)
    ReadOnlyMemory<byte> CodecConfigurationRecord    av1C (see below)
    ReadOnlyMemory<byte> SequenceHeaderObu           the OBU as in the stream
    Av1SequenceHeaderInfo SequenceHeader             parsed; null until ready
    long FramesSubmitted; long PacketsProduced
    bool IsStarted; bool IsFlushed; bool IsDisposed
    void Dispose()                  any number of times

    State rules:
      Encode after Flush           -> InvalidOperationException
      Start/Encode/Flush/TryReadPacket after Dispose
                                   -> ObjectDisposedException (the read-only
                                      properties keep their last values)
      Flush on an encoder that never started -> no packets, no record
      frame of another size / layout / bit depth, or empty -> ArgumentException
      native failure               -> Av1EncoderException

    Frames: the Nth Encode call is frame N; its packet has FrameIndex N and
    Timestamp = Request.FrameRate.TimestampOf(N). The frame's own Timestamp /
    FrameIndex are ignored, and so is its A (alpha) plane. Planes are read in
    place during the call (pinned only for the native call, strides in bytes,
    any padding allowed); reuse them as soon as Encode returns.

    Packets: one per frame, in presentation order, each a complete temporal
    unit beginning with a temporal delimiter OBU, in a FRESH array you own.
    IsKeyFrame marks random-access points. With the default (GoodQuality)
    look-ahead, the first packets arrive some frames after the first Encode;
    Flush brings out the rest. Drain TryReadPacket after EVERY Encode and
    after Flush.

    CodecConfigurationRecord / SequenceHeaderObu / SequenceHeader: taken from
    the first key frame the encoder produces, so they are EMPTY (null) until
    the first packet is ready and always set before TryReadPacket returns it.
    They never change afterwards.

Av1EncoderOptions  (AV1-specific settings; all default to "encoder decides")
-----------------
    Av1EncoderUsage Usage = GoodQuality   GoodQuality | Realtime | AllIntra
    int CpuUsed = -1                      speed 0 (slowest/best) .. 9 (fastest);
                                          wins over the request's Speed
    Av1RateMode RateMode = Automatic      Automatic | ConstantQuality |
                                          ConstrainedQuality | VariableBitrate |
                                          ConstantBitrate
    int TargetBitrateKbps = 0             0 = from a TargetBitrate request
    bool RowMultithreading = true
    int TileColumnsLog2 = -1; int TileRowsLog2 = -1     0..6 when set
    int LagInFrames = -1                  0..48 look-ahead when set (ignored
                                          for lossless: always 0)
    Av1ChromaSamplePosition ChromaSamplePosition = Unknown   (I420 only)
    bool AutomaticKeyFrames = true        false: frame 0 and forced frames are
                                          the ONLY key frames (lock step);
                                          then MaxKeyFrameInterval must be 0
    const int Default = -1, MaxCpuUsed = 9, MaxTilesLog2 = 6,
              MaxLagInFrames = 48, MaxTargetBitrateKbps = 2000000
    void Validate()                       ArgumentOutOfRangeException
    Av1EncoderOptions Clone()

Av1EncoderUsage: GoodQuality (offline, look-ahead), Realtime (no look-ahead:
each Encode returns its packet), AllIntra (every frame a key frame).
Av1RateMode: see above. Av1ChromaSamplePosition: Unknown, Vertical, Colocated.

Av1SequenceHeaderInfo  (read-only)
---------------------
    int Profile (0 Main, 1 High, 2 Professional); bool StillPicture;
    bool ReducedStillPictureHeader; int OperatingPointCount;
    int LevelIndex; string LevelName ("4.0"); int Tier;
    int BitDepth; bool Monochrome; int SubsamplingX; int SubsamplingY;
    int ChromaSamplePosition; bool ColorDescriptionPresent;
    int ColorPrimaries; int TransferCharacteristics; int MatrixCoefficients;
    bool FullRange; int MaxFrameWidth; int MaxFrameHeight;
    bool FilmGrainParamsPresent

Av1EncoderException : VideoEncodingException
--------------------------------------------
    int NativeCode       0 when not from a native call; else -1 invalid
                         argument, -2 out of memory, -3 invalid state, -4 the
                         encoder refused the settings / a codec option, -5
                         buffer too small, -6 other encoder failure
    string NativeMessage the encoder's own explanation; never null
    Catching VideoEncodingException (or VideoProcessingException) catches it.

HOW THE REQUEST IS INTERPRETED
==============================
    Width, Height        1..65536; I420 needs an EVEN width and height (odd
                         I420 sizes -> NotSupportedException before anything
                         native happens); I444 and Gray accept odd sizes
    PixelLayout          I420 / I444 / Gray; NV12 and RGBA -> NotSupported
    BitDepth             8 / 10 / 12 (12 bits uses the Professional profile)
    FrameRate            required; the stream's time base is 1/FrameRate
    Quality              EngineDefault -> the encoder's default quality;
                         ConstantQuality(q), q 0..51 -> the encoder's 0..63
                         scale as round(q * 63 / 51) (16 -> 20, 23 -> 28);
                         TargetBitrate(k) -> variable bitrate at k kbit/s
    Lossless             true -> mathematically lossless; Quality must stay
                         EngineDefault. Lossless streams are always encoded
                         WITHOUT look-ahead (Av1EncoderOptions.LagInFrames is
                         ignored): with look-ahead the encoder's lossless mode
                         is not bit-exact at 10 bits. So each lossless frame's
                         packet comes out of the Encode call that made it.
    Threads              0 -> processor count, capped at 16; 1..64
    MaxKeyFrameInterval  0 -> the encoder's default (rare automatic key
                         frames); 1..65535 -> at most that many frames apart
    Speed                0 -> the encoder's default; 1..9 -> cpu-used (higher
                         is faster); above 9 -> ArgumentOutOfRangeException
    Colour tags          written as the AV1 code points: Bt709 1,
                         Bt601Pal 5, Bt601Ntsc 6, Bt2020 9, DciP3 11,
                         DisplayP3 12; transfer Bt709 1, Linear 8, Srgb 13,
                         Pq 16, Hlg 18; matrix Rgb 0, Bt709 1, Bt601 6,
                         Bt2020NonConstant 9, Bt2020Constant 10; Unspecified
                         and Other -> 2 (unspecified); range Full -> full,
                         anything else -> limited. Matrix Rgb needs I444.
    CodecOptions         passed to the encoder by name, in order, after
                         everything above: the option names of the libaom
                         command-line encoder without leading dashes ("tune",
                         "sharpness", "enable-cdef", "arnr-strength" ...).
                         Names must be ASCII letters, digits, '-' or '_';
                         values printable ASCII. A name or value the encoder
                         refuses -> ArgumentException (ParamName
                         "CodecOptions") from Create / Start, with an
                         Av1EncoderException inner exception carrying the
                         encoder's reason.

THE av1C RECORD
===============
CodecConfigurationRecord is the AV1CodecConfigurationRecord of the AV1 ISO
media file format binding, ready to store as a track's codec-private data:
    byte 0  0x81 (marker 1, version 1)
    byte 1  seq_profile (3 bits), seq_level_idx_0 (5 bits)
    byte 2  seq_tier_0, high_bitdepth, twelve_bit, monochrome,
            chroma_subsampling_x, chroma_subsampling_y,
            chroma_sample_position (2 bits)
    byte 3  0 (no initial presentation delay)
    then    the sequence header OBU exactly as the stream carries it

COMPLETE EXAMPLES
=================

Through the registry
--------------------
    using System;
    using CodeBrix.VideoEncoding.Av1;
    using CodeBrix.VideoProcessing.Core.Encoding;
    using CodeBrix.VideoProcessing.Core.Frames;
    using CodeBrix.VideoProcessing.Core.Media;
    using CodeBrix.VideoProcessing.Core.Output;

    CodeBrixVideoEncodingAv1.Register();

    var request = new VideoEncoderRequest
    {
        Width = 1920, Height = 1080,
        PixelLayout = VideoPixelLayout.I420, BitDepth = 10,
        FrameRate = new FrameRate(30, 1),
        Quality = VideoQuality.ConstantQuality(24),
        MaxKeyFrameInterval = 120,
    };

    using (IVideoEncoder encoder = VideoEncoders.Get(VideoCodecIds.Av1).Create(request))
    {
        foreach (VideoFrameData frame in frames)
        {
            encoder.Encode(frame, forceKeyFrame: false);
            while (encoder.TryReadPacket(out EncodedVideoPacket packet)) Store(packet);
        }

        encoder.Flush();
        while (encoder.TryReadPacket(out EncodedVideoPacket packet)) Store(packet);

        ReadOnlyMemory<byte> av1C = encoder.CodecConfigurationRecord;
    }

Directly, with AV1 options and a pass-through option
----------------------------------------------------
    var request = new VideoEncoderRequest
    {
        Width = 1280, Height = 720, PixelLayout = VideoPixelLayout.I444,
        BitDepth = 8, FrameRate = new FrameRate(25, 1),
        Quality = VideoQuality.ConstantQuality(18), Speed = 6,
    };
    request.CodecOptions["tune"] = "psnr";

    var options = new Av1EncoderOptions { TileColumnsLog2 = 1, LagInFrames = 16 };
    using var encoder = new Av1Encoder(request, options);

Lossless, monochrome, 10-bit
----------------------------
    var request = new VideoEncoderRequest
    {
        Width = 640, Height = 360, PixelLayout = VideoPixelLayout.Gray,
        BitDepth = 10, FrameRate = new FrameRate(30, 1),
        Lossless = true, ColorRange = VideoColorRange.Full,
    };
    using var encoder = new Av1Encoder(request);

Colour + alpha in lock step
---------------------------
    var lockStep = new Av1EncoderOptions { AutomaticKeyFrames = false };
    var colourRequest = new VideoEncoderRequest { Width = w, Height = h,
        BitDepth = 10, FrameRate = rate };
    var alphaRequest = new VideoEncoderRequest { Width = w, Height = h,
        BitDepth = 10, FrameRate = rate, PixelLayout = VideoPixelLayout.Gray,
        ColorRange = VideoColorRange.Full,
        ColorPrimaries = VideoColorPrimaries.Unspecified,
        ColorTransfer = VideoColorTransfer.Unspecified,
        ColorMatrix = VideoColorMatrix.Unspecified };

    using var colour = new Av1Encoder(colourRequest, lockStep);
    using var alpha = new Av1Encoder(alphaRequest, lockStep);
    for (int i = 0; i < frames.Count; i++)
    {
        bool key = i % 60 == 0;
        colour.Encode(frames[i], key);
        alpha.Encode(VideoFrameData.CreateGray(w, h, frames[i].A,
            frames[i].Timestamp, i, default, 10), key);
        // drain both
    }
    // Flush both, drain both. Frame counts, FrameIndex, Timestamp and the
    // key-frame positions of the two streams are identical.

MINIMUM VIABLE PROJECT TEMPLATE
===============================
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.VideoEncoding.Av1.BsdLicenseForever" Version="*" />
      </ItemGroup>
    </Project>

    using System;
    using CodeBrix.VideoEncoding.Av1;

    CodeBrixVideoEncodingAv1.Register();
    Console.WriteLine($"AV1 encoder {CodeBrixVideoEncodingAv1.NativeLibraryVersion}");

PERFORMANCE TIPS
================
* Speed (cpu-used) is the big lever: 6 is a good offline default, 8-9 for
  previews and tests, 0-3 only when time does not matter.
* Leave Threads at 0 (processor count, capped at 16) unless several encoders
  run at once - then divide the cores between them. Tiles
  (TileColumnsLog2 = 1 or 2) let more threads work on large pictures.
* Realtime usage returns each packet from its own Encode call; GoodQuality
  holds frames back for its look-ahead and spends the memory for that.
* Lossless streams are large; use them for masters, not delivery.

COMMON PITFALLS TO AVOID
========================
* Passing NV12 or RGBA frames - convert to I420 / I444 (and alpha to a Gray
  frame) first; the encoder refuses them (NotSupportedException).
* Odd I420 sizes - I420 needs an even width and height (refused with
  NotSupportedException); pad or crop, or use I444 / Gray, which take odd
  sizes. A Gray alpha stream beside an odd-sized I444 picture is fine.
* Expecting EXACTLY the forced key frames with the default GoodQuality usage -
  the encoder also makes the frame right after each forced key frame a key
  frame (its look-ahead behaviour). Every forced frame IS a key frame, and two
  lock-step encoders with the same settings and the same forced frames produce
  IDENTICAL key-frame sets, so lock step is unaffected. Realtime and AllIntra
  place key frames exactly where asked.
* Reading CodecConfigurationRecord right after Start/Create - it is empty
  until the first packet is ready. Read it after the first TryReadPacket.
* Draining only at the end - call TryReadPacket after EVERY Encode and after
  Flush; skipping Flush loses the frames held back by the look-ahead.
* Expecting the frame's own timestamps to be used - the encoder numbers frames
  itself (constant frame rate).
* Letting automatic key frames place key frames in a lock-step pair - with
  AutomaticKeyFrames = true the encoder may put key frames at scene cuts,
  which differ between a colour picture and its alpha. Use
  AutomaticKeyFrames = false and force the same frames in both.
* Setting Quality together with Lossless - Core's validation refuses it.
* Expecting Speed 0 to mean "slowest" - 0 means "encoder default"; use
  Av1EncoderOptions.CpuUsed = 0 for the slowest setting.
* Calling Register() from a library - the application registers, once.

WHAT THIS PACKAGE DOES NOT DO
=============================
* No decoding (use an AV1 decoder package for that).
* No containers: it writes no IVF, WebM, MP4 or CBVF files - store the
  packets and the av1C record yourself.
* No pixel conversion (RGBA -> YUV, NV12 -> I420), no scaling, no alpha
  extraction - the caller prepares planar frames.
* No 4:2:2 input (Core has no I422 layout).
* No two-pass encoding and no film-grain synthesis controls of its own (the
  encoder's own options can still be passed through CodecOptions).

WORKING EXAMPLES ON GITHUB
==========================
    https://github.com/ellisnet/CodeBrix.VideoEncoding.Av1/tree/main/tests/CodeBrix.VideoEncoding.Av1.Tests

    Av1EncoderRoundTripTests.cs   encode + decode: layouts, depths, lossless,
                                  key frames, lock step, colour tags, codec
                                  options, rate modes, two threads
    Av1EncoderTests.cs            lifecycle, refusals, packet numbering,
                                  Start / Flush / Dispose rules
    Av1EncoderFactoryTests.cs     the factory and its capabilities
    CodeBrixVideoEncodingAv1Tests.cs  Register and the registry
    Av1RequestMapperTests.cs      how each request field is interpreted
    Av1SequenceHeaderReaderTests.cs / Av1ConfigurationRecordTests.cs
                                  the parsed header and the av1C record

QUICK REFERENCE CARD
====================
    CodeBrixVideoEncodingAv1.Register()          once, at start-up
    VideoEncoders.Get(VideoCodecIds.Av1).Create(request)   -> IVideoEncoder
    new Av1Encoder(request, new Av1EncoderOptions { ... })
    encoder.Encode(frame, forceKeyFrame)         then drain TryReadPacket
    encoder.Flush()                              then drain TryReadPacket
    encoder.CodecConfigurationRecord             av1C, after the first packet
    encoder.SequenceHeader                       profile / level / depth ...
    Av1EncoderOptions.AutomaticKeyFrames = false lock-step key frames
    Av1EncoderOptions.CpuUsed 0..9               speed
    request.CodecOptions["name"] = "value"       encoder option pass-through
    Av1EncoderException.NativeCode / .NativeMessage
================================================================================
