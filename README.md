# CodeBrix.VideoEncoding.Av1

AV1 video encoding for [CodeBrix.VideoProcessing](https://github.com/ellisnet/CodeBrix.VideoProcessing): planar YUV or monochrome frames in, AV1 temporal units out, over a native AV1 encoder that ships inside the package for every supported platform - including Android.
CodeBrix.VideoEncoding.Av1 is provided as a .NET 10 library and associated `CodeBrix.VideoEncoding.Av1.BsdLicenseForever` NuGet package.

CodeBrix.VideoEncoding.Av1 supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.VideoEncoding.Av1.BsdLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.VideoEncoding.Av1`:

* NuGet package ID: `CodeBrix.VideoEncoding.Av1.BsdLicenseForever`
* Assembly and primary namespace: `CodeBrix.VideoEncoding.Av1` - i.e. `using CodeBrix.VideoEncoding.Av1;`

XML documentation (IntelliSense) ships alongside the assembly.

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.VideoProcessing.Core.MitLicenseForever` - the shared video contracts (frames, frame rates, colour tags) and the encoder registry this package registers its AV1 encoder with.

## CodeBrix.VideoEncoding.Av1 supports:

* Encoding planar I420 (4:2:0), I444 (4:4:4) and monochrome (Gray) frames at 8, 10 or 12 bits per sample (I420 at even sizes; I444 and Gray at any size) into an AV1 elementary stream - one temporal unit per frame, in presentation order, each in a fresh buffer
* Constant quality on the familiar 0..51 scale, constrained quality, variable or constant bitrate, and mathematically lossless coding
* Key frames where you ask for them, an optional maximum key-frame interval, or no automatic key frames at all - so two streams (a colour picture and its alpha channel as a monochrome picture) can be kept in exact lock step
* Colour signalling - primaries, transfer characteristics, matrix, full or limited range and chroma sample position - written into the stream as tags
* The av1C codec configuration record a container stores, plus the parsed sequence header (profile, level, tier, bit depth, subsampling, colour)
* Usage profiles for offline quality, real-time and all-intra encoding, a speed setting, worker threads, tiles and look-ahead, and pass-through of any further encoder option by name
* `CodeBrixVideoEncodingAv1.Register()`, which makes the encoder available to any code that asks the CodeBrix.VideoProcessing encoder registry for "av1"
* Native encoders in the package for Windows x64 and ARM64, macOS Intel and Apple Silicon, Linux x64, ARM64 and RISC-V 64, and Android 13+ ARM64 and x64 - the application chooses nothing

## Native assets

The package carries one native library per platform in the standard NuGet `runtimes/<rid>/native/` layout, each beside the licence text of the code it was built from. NuGet and the .NET runtime pick the right one; on Android it is packed into the APK. If the native cannot be found at run time, the exception names the runtime identifier and every path that was tried.

## Sample Code

### Encode frames through the registry

```csharp
using System;
using CodeBrix.VideoEncoding.Av1;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;

CodeBrixVideoEncodingAv1.Register();   // once, at application start-up

var request = new VideoEncoderRequest
{
    Width = 1920,
    Height = 1080,
    PixelLayout = VideoPixelLayout.I420,
    BitDepth = 10,
    FrameRate = new FrameRate(30, 1),
    Quality = VideoQuality.ConstantQuality(24),
};

using IVideoEncoder encoder = VideoEncoders.Get(VideoCodecIds.Av1).Create(request);
foreach (VideoFrameData frame in frames)
{
    encoder.Encode(frame, forceKeyFrame: false);
    while (encoder.TryReadPacket(out EncodedVideoPacket packet)) Store(packet);
}

encoder.Flush();
while (encoder.TryReadPacket(out EncodedVideoPacket packet)) Store(packet);

ReadOnlyMemory<byte> av1C = encoder.CodecConfigurationRecord;   // for the container
```

### A colour stream and its alpha stream in lock step

```csharp
using CodeBrix.VideoEncoding.Av1;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;

var keyFramesOnRequestOnly = new Av1EncoderOptions { AutomaticKeyFrames = false };

var colourRequest = new VideoEncoderRequest { Width = 1280, Height = 720, BitDepth = 10, FrameRate = new FrameRate(30, 1) };
var alphaRequest = new VideoEncoderRequest
{
    Width = 1280, Height = 720, BitDepth = 10, FrameRate = new FrameRate(30, 1),
    PixelLayout = VideoPixelLayout.Gray,
    ColorRange = VideoColorRange.Full,
};

using var colour = new Av1Encoder(colourRequest, keyFramesOnRequestOnly);
using var alpha = new Av1Encoder(alphaRequest, keyFramesOnRequestOnly);

for (int i = 0; i < frames.Count; i++)
{
    bool key = i % 60 == 0;   // the same frames are key frames in both streams
    colour.Encode(frames[i], key);
    alpha.Encode(VideoFrameData.CreateGray(1280, 720, frames[i].A, frames[i].Timestamp, i, default, 10), key);
    // ... drain both encoders with TryReadPacket ...
}
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.VideoEncoding.Av1.Tests` project:
https://github.com/ellisnet/CodeBrix.VideoEncoding.Av1/tree/main/tests/CodeBrix.VideoEncoding.Av1.Tests

## License

CodeBrix.VideoEncoding.Av1 is licensed under the BSD 2-Clause License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.VideoEncoding.Av1/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.VideoEncoding.Av1/blob/main/THIRD-PARTY-NOTICES.txt).
