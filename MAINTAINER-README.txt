================================================================================
MAINTAINER-README: CodeBrix.VideoEncoding.Av1
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, how the native libraries
are produced, how the Core contract is mapped onto the encoder, and the
conventions the source follows.


PURPOSE AND SCOPE
=================
PackageId:      CodeBrix.VideoEncoding.Av1.BsdLicenseForever
Assembly:       CodeBrix.VideoEncoding.Av1 (net10.0)
Project:        src/CodeBrix.VideoEncoding.Av1/CodeBrix.VideoEncoding.Av1.csproj
License:        BSD-2-Clause (LICENSE); the compiled libaom code is BSD-2-Clause
                plus the Alliance for Open Media Patent License 1.0
Consumer doc:   AGENT-README.txt

An AV1 ENCODER: one managed assembly that P/Invokes one native library per
runtime identifier, named codebrix_av1 (libcodebrix_av1.so,
libcodebrix_av1.dylib, codebrix_av1.dll), which statically links libaom built
encoder-only with high bit depth. It exists for CBVF Mode3 "master" authoring
(one AV1 colour stream + an optional AV1 monochrome alpha stream, encoded in
lock step) on every platform, Android included.

THE CORE SEAM. The package implements CodeBrix.VideoProcessing.Core's encoder
contract (namespace CodeBrix.VideoProcessing.Core.Encoding):
  - CodeBrixVideoEncodingAv1.Register() puts ONE shared Av1EncoderFactory into
    VideoEncoders under VideoCodecIds.Av1 ("av1"); Core's
    VideoEncoderNotRegisteredException message names this package and call.
  - Av1EncoderFactory : IVideoEncoderFactory; Create() maps the request
    (refusing before any native call), then STARTS the encoder.
  - Av1Encoder : IVideoEncoder; constructable directly with Av1EncoderOptions.
  - Av1EncoderException : VideoEncodingException.
Core is a nuget.org package reference (pinned in BOTH csprojs - raise them
together), never a project reference.

THE NATIVE CONTRACT is av1-native-tools/shim/codebrix_av1.h: the single source
of truth for the managed/native boundary - fixed ABI (opaque handle, int32 /
int64 / byte pointers only, no C long, no libaom structs, no callbacks),
versioned by CODEBRIX_AV1_API_VERSION. Interop/Av1Library.cs
(ExpectedApiVersion = 1) refuses a library reporting any other version. Change
the header only together with the shim, every native build,
Interop/Av1Native.cs, Internal/Av1Constants.cs and the version constant on
both sides.

Contract points the managed side depends on:
  - set_* options are recorded and applied at start; string options go
    through aom_codec_set_option in order; a refused one makes start fail
    with last_error = 'option "<name>": <libaom reason>' - Av1Encoder turns
    that into ArgumentException(ParamName "CodecOptions") wrapping the
    Av1EncoderException.
  - The sequence header is taken by the shim from the FIRST KEY-FRAME packet
    (libaom's global header after init reports different tool flags) and is
    CBA_E_INVALID_STATE until a packet exists. Av1Encoder therefore builds
    the av1C record while draining, before the first packet is queued -
    which is what Core's contract promises ("available once TryReadPacket
    has returned the first packet").
  - pts = frame index, timebase = 1/frame rate.


REPOSITORY LAYOUT
=================
src/CodeBrix.VideoEncoding.Av1/
    CodeBrixVideoEncodingAv1.cs      Register / EnsureLoaded / diagnostics
    Av1EncoderFactory.cs             the "av1" IVideoEncoderFactory
    Av1Encoder.cs                    the IVideoEncoder (lifecycle, pinning,
                                     packet queue, av1C)
    Av1EncoderOptions.cs             AV1-specific settings + Validate + Clone
    Av1EncoderException.cs           NativeCode + NativeMessage
    Av1SequenceHeaderInfo.cs         the parsed sequence header (public)
    Av1EncoderUsage.cs / Av1RateMode.cs / Av1ChromaSamplePosition.cs   enums
    Internal/Av1RequestMapper.cs     request + options -> native settings
                                     (ALL refusals before any native call)
    Internal/Av1ColorMapping.cs      Core colour enums -> AV1 code points
    Internal/Av1NativeSettings.cs    the mapped settings
    Internal/Av1Constants.cs         the header's constants by C name
    Internal/Av1BitReader.cs         f(n) / uvlc / leb128 reader
    Internal/Av1SequenceHeaderReader.cs   sequence_header_obu() parser
    Internal/Av1ConfigurationRecord.cs    av1C builder
    Interop/Av1Library.cs            native loader + DllImportResolver
    Interop/Av1Native.cs             LibraryImport declarations (the header)
    Interop/Av1EncoderHandle.cs      SafeHandle over the native encoder
    Interop/Av1Result.cs             CBA_* codes, messages, last_error
    InternalsVisibleTo.cs            -> CodeBrix.VideoEncoding.Av1.Tests
    runtimes/<rid>/native/           COMMITTED native binaries, each beside
                                     LICENSE-Aom.txt
tests/CodeBrix.VideoEncoding.Av1.Tests/   xUnit v3 test project
av1-native-tools/                    native build tooling (EXTRAS-README.txt)

There is deliberately no Encoding/ folder or namespace: inside this assembly a
"CodeBrix.VideoEncoding.Av1.Encoding" namespace would shadow
System.Text.Encoding and clash in name with Core's
CodeBrix.VideoProcessing.Core.Encoding. Every public type is in the root
namespace, so consumers need one using.

Root files: .gitignore (family canonical), .gitattributes (eol=lf, binaries,
-text on runtimes/), LICENSE, README.md, AGENT-README.txt,
MAINTAINER-README.txt, EXTRAS-README.txt, README-INDEX.txt,
THIRD-PARTY-NOTICES.txt, icon-codebrix-128.png, global.json,
CodeBrix.VideoEncoding.Av1.slnx and the 8 AI-pointer stubs (byte-identical to
CodeBrix.SkiaSvg's). The .slnx Solution Items are the canonical ten files:
.gitignore, AGENT-README.txt, EXTRAS-README.txt, global.json,
icon-codebrix-128.png, LICENSE, MAINTAINER-README.txt, README-INDEX.txt,
README.md, THIRD-PARTY-NOTICES.txt; the Tests folder holds the test project.
global.json selects the Microsoft.Testing.Platform test runner; it pins no SDK.


BUILDING
========
    dotnet build --configuration Release CodeBrix.VideoEncoding.Av1.slnx

Expect 0 warnings and 0 errors (Debug and Release). GenerateDocumentationFile
is on, so CS1591 is fixed by writing the doc comment, never with NoWarn.
AllowUnsafeBlocks is on for the source-generated LibraryImport interop and
the pinned-plane encode call.

The managed build does NOT build natives; it packs and copies whatever is
committed under src/CodeBrix.VideoEncoding.Av1/runtimes/.


TESTING
=======
xUnit v3 + SilverAssertions. The test runner IS Microsoft.Testing.Platform,
selected by the root global.json - do not delete it; without it `dotnet test`
falls back to the VSTest bridge, which fails on the .NET 10 SDK.

    dotnet test --solution CodeBrix.VideoEncoding.Av1.slnx
    dotnet test --solution CodeBrix.VideoEncoding.Av1.slnx -c Release

If `dotnet test --solution` reports zero tests (a known SDK 10.0.4xx quirk),
run the test assembly directly:

    dotnet tests/CodeBrix.VideoEncoding.Av1.Tests/bin/Debug/net10.0/CodeBrix.VideoEncoding.Av1.Tests.dll

The test csproj copies src/CodeBrix.VideoEncoding.Av1/runtimes/** into its
output under runtimes/<rid>/native/, so the tests load exactly the committed
binary through the library's own resolver. BOOTSTRAP: while no linux-x64
native is committed, a conditional item copies the local host build
av1-native-tools/output/host/libcodebrix_av1.so (never committed) into the
same place; it is inert once runtimes/linux-x64/native/libcodebrix_av1.so
exists. The loader probes the output folder itself BEFORE
runtimes/<rid>/native/, so a stray libcodebrix_av1.so in bin/ shadows the
committed one - delete it before trusting a run.

Every round trip is decoded with the PUBLISHED
CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever (test project only; its
VideoPlayback types share names with Core's, so only Dav1dDecode.cs touches
them). Lossless is checked bit-exact, lossy against a PSNR floor; key-frame
flags are compared with what the decoder reports. Pictures are synthetic
(TestPicture.cs); there are no video assets. Encodes stay at or below 64
frames and 320x180, so a full run takes seconds. Set
CODEBRIX_AV1_TEST_DUMP_DIR to have the IVF test write its stream for
inspection with other tools.

No coverage collector is referenced (the family dropped coverlet.collector).


PACKAGING / PUBLISHING
======================
The csproj carries the family's canonical date-stamped version block
(1.<years since 2026>.<day of year>.<minute of day>, UTC); never hardcode a
<Version>. GeneratePackageOnBuild is on, so every build drops a fresh .nupkg in
src/CodeBrix.VideoEncoding.Av1/bin/<Configuration>/.

The nupkg contains: lib/net10.0/CodeBrix.VideoEncoding.Av1.dll + .xml,
README.md, AGENT-README.txt, THIRD-PARTY-NOTICES.txt, icon-codebrix-128.png,
and runtimes/<rid>/native/{binary, LICENSE-Aom.txt} for every RID committed.
The LICENSE file is NOT packed (the family convention is the licence
expression, BSD-2-Clause). Exactly one dependency:
CodeBrix.VideoProcessing.Core.MitLicenseForever. The runtimes are packed with
PackagePath="runtimes\" (BARE - NuGet appends the recursive directory itself;
spelling %(RecursiveDir) out doubles the path). After any csproj change,
unzip -l the Release nupkg and check every runtimes/<rid>/native/ path
appears once and correctly.

Do not publish until all nine RIDs are committed (see the matrix below) - a
package missing a RID fails at run time on that platform. Jeremy publishes.


NATIVE LIBRARIES
================
All native build tooling lives in av1-native-tools/ - read
av1-native-tools/README.txt before touching it. Per-RID build records
(toolchain, container digest, hashes, date) are in
av1-native-tools/BUILD-PROVENANCE.txt; upstream identity and tarball hashes in
av1-native-tools/UPSTREAM.txt.

Recipe in one paragraph: libaom (vendored unmodified in av1-native-tools/aom/)
is configured with CMake ENCODER-ONLY (CONFIG_AV1_DECODER=0,
CONFIG_AV1_ENCODER=1, CONFIG_AV1_HIGHBITDEPTH=1, no tools / tests / examples /
docs), built as a STATIC library with hidden visibility and linked into the
shared codebrix_av1 shim (shim/codebrix_av1.c), which exports only the
functions of codebrix_av1.h. The x86-64 builds assemble libaom's SIMD code
with nasm, built from the vendored av1-native-tools/nasm/ (a build-time tool,
never shipped). licenses/compose-license-aom.sh writes LICENSE-Aom.txt (libaom
LICENSE + PATENTS + the notices of the third-party code actually linked),
identical bytes for every RID.

Build matrix - which machine builds which RID:

    RID              Built where                     How
    ---------------  ------------------------------  ---------------------------
    linux-x64        this Linux dev machine          manylinux container
    linux-arm64      this Linux dev machine          manylinux container
    linux-riscv64    this Linux dev machine          manylinux container
    android-arm64    this Linux dev machine          Android NDK (API 33,
                                                     16 KB page alignment)
    android-x64      this Linux dev machine          Android NDK (API 33)
    win-x64          Jeremy, on Windows              PowerShell script
    win-arm64        Jeremy, on Windows              PowerShell script
    osx-arm64        Jeremy, on a Mac                bash script
    osx-x64          Jeremy, on a Mac                bash script

Each build writes the binary plus LICENSE-Aom.txt into
src/CodeBrix.VideoEncoding.Av1/runtimes/<rid>/native/. A host build made for
local testing (av1-native-tools/output/host/) is never committed into
runtimes/.

THE NEVER-REACH-OUTSIDE RULE. Every build script reads only from this
repository: the vendored aom/ and nasm/ trees, the shim, the scripts. No
downloads, no git clones, no package installs at build time; containers run
with networking disabled. av1-native-tools/aom/ is never modified.


MAPPING TABLES (Core -> native)
===============================
Implemented in Internal/Av1RequestMapper.cs and Internal/Av1ColorMapping.cs;
every refusal happens there, before any native call.

    Core request / options            native (codebrix_av1.h)
    --------------------------------  -----------------------------------------
    PixelLayout I420 / I444 / Gray    CBA_LAYOUT_I420 / _I444 / _MONOCHROME
    PixelLayout Nv12 / Rgba           NotSupportedException (Core contract)
    I420 with odd width or height     NotSupportedException (libaom drops the
                                      last chroma column of odd 4:2:0
                                      pictures; the native create() refuses
                                      them too); I444 / Gray take odd sizes
    BitDepth 8 / 10 / 12              bit_depth (profile chosen by the shim:
                                      Main 420+mono 8/10, High 444 8/10,
                                      Professional 12)
    FrameRate num/den                 timebase num = den, den = num; pts = index
    Width / Height > 65536            ArgumentOutOfRangeException
    Quality EngineDefault             RATE_MODE = CONSTANT_QUALITY, QUALITY unset
                                      (shim default cq-level 30)
    Quality ConstantQuality(q)        QUALITY = (q * 63 + 25) / 51
                                      (0->0, 16->20, 20->25, 23->28, 51->63)
    Quality TargetBitrate(k)          RATE_MODE = VBR, TARGET_BITRATE_KBPS = k
    options.RateMode explicit         CQ / CONSTRAINED / VBR / CBR; bitrate
                                      modes need a bitrate (options or
                                      request); contradictions refused
    Lossless                          LOSSLESS = 1, no rate options; the shim
                                      forces lag-in-frames 0 (LagInFrames is
                                      ignored - see NOTES)
    Speed 0 / 1..9 / >9               unset / CPU_USED / refused
    options.CpuUsed 0..9              CPU_USED (wins over Speed)
    Threads 0 / 1..64 / >64           min(ProcessorCount, 16) / as given /
                                      refused
    options.RowMultithreading         ROW_MULTITHREADING 0/1 (always set)
    options.TileColumns/RowsLog2      TILE_COLUMNS/ROWS_LOG2 when not -1
    options.LagInFrames               LAG_IN_FRAMES when not -1
    MaxKeyFrameInterval 0 / n         unset / KEY_FRAME_MAX_DISTANCE n (<=65535)
    options.AutomaticKeyFrames false  AUTOMATIC_KEY_FRAMES 0 (refused together
                                      with a MaxKeyFrameInterval)
    options.Usage                     usage argument of create (GOOD / REALTIME
                                      / ALL_INTRA)
    options.ChromaSamplePosition      CHROMA_SAMPLE_POSITION (I420 only)
    CodecOptions                      set_string_option in dictionary order;
                                      names [A-Za-z0-9_-], values printable
                                      ASCII, else ArgumentException

    Colour tag            Core value              AV1 code point
    --------------------  ----------------------  --------------
    ColorPrimaries        Bt709 / Bt601Pal /      1 / 5 / 6 / 9 / 11 / 12
                          Bt601Ntsc / Bt2020 /
                          DciP3 / DisplayP3
                          Unspecified, Other      2
    ColorTransfer         Bt709 / Linear / Srgb   1 / 8 / 13
                          / Pq / Hlg              / 16 / 18
                          Unspecified, Other      2
    ColorMatrix           Rgb / Bt709 / Bt601 /   0 / 1 / 6 / 9 / 10
                          Bt2020NonConstant /     (Rgb only with I444 -
                          Bt2020Constant           AV1 conformance)
                          Unspecified, Other      2
    ColorRange            Full / other            CBA_RANGE_FULL / _LIMITED

THE av1C BUILDER (Internal/Av1ConfigurationRecord.cs): 0x81; seq_profile << 5
| seq_level_idx[0]; seq_tier[0] << 7 | high_bitdepth << 6 | twelve_bit << 5 |
mono_chrome << 4 | subsampling_x << 3 | subsampling_y << 2 |
chroma_sample_position; 0x00; then the sequence header OBU as read from the
shim. Every field comes from Av1SequenceHeaderReader's parse of that same OBU.


CODING CONVENTIONS
==================
- File layout: usings at the top (System.* first, alphabetical), one blank line,
  file-scoped namespace, one blank line, the type. No global usings, no
  ImplicitUsings, no #nullable.
- Folder = namespace (Interop/ -> CodeBrix.VideoEncoding.Av1.Interop,
  Internal/ -> CodeBrix.VideoEncoding.Av1.Internal). The public types live at
  the root namespace.
- NRT is off: no `?` on reference types, no `!`. Value-type nullables are fine.
- XML doc comments on every public and protected member; no NoWarn.
- Native signatures stay blittable (pointers, ints) so LibraryImport generates
  no marshalling.
- Tests: <Class>Tests.cs (scenario files such as Av1EncoderRoundTripTests.cs
  named descriptively), snake_case method names, //Arrange //Act //Assert in
  multi-statement bodies, expression-bodied single-statement tests,
  SilverAssertions fluent style, TestContext.Current.CancellationToken on any
  cancellable call.


PROVENANCE / VENDORED SOURCES
=============================
av1-native-tools/aom/ is the unmodified libaom release; av1-native-tools/nasm/
the unmodified nasm release (build tool only). Tarballs are in
av1-native-tools/upstream-downloads/. Details and the full licence texts:
THIRD-PARTY-NOTICES.txt and av1-native-tools/UPSTREAM.txt. The managed code
and the shim are original to this repository.

Two git traps around the vendored trees:
- av1-native-tools/aom/.gitignore IGNORES ITSELF (it lists ".gitignore").
  After vendoring (or re-vendoring) libaom, add it once by force:
      git add -f av1-native-tools/aom/.gitignore
  A tracked file is not affected by ignore rules, so this is a one-time step.
- av1-native-tools/.gitignore starts with "!*", which re-includes everything
  the root Visual Studio .gitignore would otherwise drop from the vendored
  trees (folders named x86/, arm/, arm64/, build/ ...). Never delete that
  line. Verify after any upstream bump with:
      git ls-files --others --ignored --exclude-standard av1-native-tools/aom av1-native-tools/nasm
  which must list nothing but aom/.gitignore itself.


NOTES
=====
- Encode after Flush throws InvalidOperationException; every call after
  Dispose throws ObjectDisposedException; Flush and Dispose are idempotent;
  Flush on a never-started encoder starts nothing and yields nothing.
- Frames are pinned (VideoPlane.Data.Pin) only for the native call; packets
  are fresh arrays; nothing managed is retained between calls.
- PacketsProduced counts packets HANDED OUT by TryReadPacket (Core's
  definition), not the native packets_produced.
- Known native behaviours, documented and tested where they matter:
  * GoodQuality usage puts an extra key frame right after each forced one
    (libaom's two-pass-strategy key-frame search counts the forced frame
    itself as the next key frame; NOT patched - the vendored tree stays
    unmodified). Every forced frame is still a key frame and lock-step
    encoders produce identical key-frame sets (tested); Realtime and AllIntra
    place key frames exactly (tested).
  * LOSSLESS IS ENCODED WITHOUT LOOK-AHEAD: the shim forces lag-in-frames 0
    for lossless streams, because libaom's lossless mode with look-ahead is
    not bit-exact at 10 bits (reproduced with libaom's own command-line
    encoder; exact without look-ahead across the native sweep). The
    bit-exact round-trip tests (8 and 10 bits, colour and Gray) gate this.
  * ODD I420 SIZES ARE REFUSED: libaom loses the last chroma column of an
    odd-width 4:2:0 picture (seen with both wrapped and allocated images), so
    the shim's create() and the managed mapper refuse odd I420 widths and
    heights; I444 and Gray round-trip bit-exact at odd sizes (tested).
- CodeBrix.VideoProcessing.Core is pinned in BOTH csprojs (library and tests);
  raise them together.


================================================================================
END OF MAINTAINER-README
================================================================================
