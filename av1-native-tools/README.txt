================================================================================
av1-native-tools - how the codebrix_av1 native libraries are built
================================================================================

WHAT IS BUILT
--------------------------------------------------------------------------------
ONE native library per runtime identifier, `codebrix_av1`
(libcodebrix_av1.so / codebrix_av1.dll / libcodebrix_av1.dylib), which
CodeBrix.VideoEncoding.Av1 P/Invokes. It is the C shim in shim/codebrix_av1.c
with libaom (3.15.2, built ENCODER-ONLY with high bit depth) statically linked
in. Encoding only - no AV1 decoder is compiled.

The shim implements shim/codebrix_av1.h - THE fixed-ABI contract with the
managed side: opaque handles, int32_t / int64_t / byte pointers only, no libaom
type, no callback and no C `long` (32-bit on Windows), so one managed import set
works on every RID. Do not change the header without bumping
CODEBRIX_AV1_API_VERSION and changing the managed side in the same change.
Only the header's 17 functions are exported (every object, libaom's included, is
compiled with hidden visibility; Linux/Android also link with --exclude-libs,ALL
and the version script shim/codebrix_av1.map, macOS with the export list
shim/codebrix_av1.exports); every gate checks the exact export set.

How the shim drives libaom (details in the comments of codebrix_av1.c):
  * create / set_* only record; start builds the aom_codec_enc_cfg_t (profile
    derived from layout + depth, monochrome, timebase, rate mode - AOM_Q with
    cq-level 30 by default -, kf-min-dist 0, threads, lag), calls
    aom_codec_enc_init, then the controls (cq-level, cpu-used, lossless, row-mt,
    tiles, chroma subsampling, colour description, chroma sample position), then
    the pass-through string options via aom_codec_set_option, in order.
  * encode wraps the caller's planes in an aom_image_t without copying. Two
    cases use shim memory, sized once: MONOCHROME (libaom's image still needs
    chroma planes - they point at a mid-grey plane the shim owns) and I420/I444
    input with unequal U/V strides (libaom requires equal ones; V is copied).
  * Packets: every AOM_CODEC_CX_FRAME_PKT is copied into a queue with its pts and
    AOM_FRAME_IS_KEY. libaom emits one temporal unit per shown frame, each
    beginning with a temporal delimiter.
  * The sequence header OBU is taken from the FIRST key-frame packet; it is
    CBA_E_INVALID_STATE until a packet exists. aom_codec_get_global_headers() is
    NOT used: right after init it reports tool flags (and, at 12 bits, the
    subsampling) that differ from what libaom then writes into the stream.
  * libaom 3.15.2 limitations the shim enforces (measured 2026-10-10 with
    libaom's own aomenc too - see BUILD-PROVENANCE.txt "Known libaom
    behaviours"): LOSSLESS always encodes with lag-in-frames 0 (with look-ahead
    it is not bit-exact at 10 bits), and I420 refuses odd width/height (the last
    chroma column/row is not coded faithfully); matrix coefficients 0 (identity)
    are refused unless the layout is I444.

THE RULE: SCRIPTS NEVER REACH OUTSIDE THIS REPOSITORY
--------------------------------------------------------------------------------
Every build reads only what is committed here. Nothing is downloaded, cloned, or
installed at build time:
  * aom/ is the unmodified libaom release tarball, unpacked; nasm/ the unmodified
    nasm release tarball (a BUILD TOOL only); both tarballs are in
    upstream-downloads/ (UPSTREAM.txt has their sha256s, the two-source
    verification, what of aom/ is and is not compiled, and the no-network
    re-verification recipe). Never edit those trees; a needed change goes in
    patches/*.patch, applied to a scratch copy.
  * Linux builds run in podman with --network none (and --pull=never). The
    derived images (linux/Containerfile.<arch>) install NOTHING - the pinned
    manylinux base images already carry gcc/g++, cmake, make, binutils and Perl -
    so even building the derived image needs no network. nasm is built from
    nasm/ INSIDE the x86_64 container on every build (build-nasm.sh).
  * Tools on a build machine (podman, the NDK, Visual Studio, Xcode CLT, cmake,
    Perl, nasm on Windows/macOS x64) are prerequisites the scripts check for and
    NAME when missing; they never install them.

WHAT BUILDS WHERE
--------------------------------------------------------------------------------
  RID            script                          machine                 status
  linux-x64      linux/build.sh x64              any Linux + podman      built here
  linux-arm64    linux/build.sh arm64            ditto (+ qemu binfmt)   built here
  linux-riscv64  linux/build.sh riscv64          ditto (+ qemu binfmt)   built here
  android-arm64  android/build.sh arm64          Linux/macOS + NDK 30    built here
  android-x64    android/build.sh x64            Linux/macOS + NDK 30    built here
  win-x64        windows/build-win-x64.ps1       x64 Windows + VS C++    built (Jeremy's x64 box)
                                                 + Perl + nasm
  win-arm64      windows/build-win-arm64.ps1     ARM64 Windows (or x64,  cross-built there;
                                                 cross; smoke test unrun) smoke test UNRUN
  osx-arm64      macos/build-osx-arm64.sh        Apple Silicon Mac       NOT YET BUILT (Jeremy)
  osx-x64        macos/build-osx-x64.sh          Intel Mac, or Apple     NOT YET BUILT (Jeremy)
                                                 Silicon + Rosetta 2;
                                                 + nasm
  (host)         build-host.sh                   this Linux dev box      NOT shipped

The Windows and macOS scripts were written on Linux on 2026-10-10 as copies of
CodeBrix.Audio.FlacWriter's - which Jeremy ran successfully that day - changed
only where libaom differs: one vendored tree built through libaom's own CMake,
Perl required, nasm required for the x64 slices (checked on PATH the dav1d way,
and the gate checks libaom really assembled with it), AOM_TARGET_CPU passed
explicitly (a cross build's CMAKE_SYSTEM_PROCESSOR is the host's), one composed
licence file (re-composed and compared by the Windows gate), 17 exports, and the
smoke test at shim/smoke-test.c. The Windows scripts first ran for libaom on
2026-10-10 on Jeremy's x64 Windows 11 machine (Visual Studio 2026, Strawberry
Perl 5.42, NASM 3.02) and ran UNCHANGED with no fix needed anywhere; win-arm64
built with MSVC's ARM64 toolset on the first try (NEON, CRC32, dot-product and
I8MM kernels, runtime-dispatched - the clang-cl fallback described in
build-win-arm64.ps1's header was not needed). It was cross-built with
-AdoptCrossBuild, so its smoke test is still UNRUN - finish it on an ARM64
Windows device (Surface Pro X) as that header describes. The macOS scripts have
NOT been run for libaom. They all gate themselves exactly as the FlacWriter
ones do; if one breaks, fix it in the script and record it in
BUILD-PROVENANCE.txt.

HOW TO REBUILD
--------------------------------------------------------------------------------
  cd av1-native-tools/linux   && ./build.sh            # all three Linux RIDs
  cd av1-native-tools/android && ./build.sh            # both Android ABIs
       DEVICE_ARM64=<adb serial> DEVICE_X64=<adb serial> ./build.sh   # + on-device smoke test
  cd av1-native-tools\windows ; .\build-win-x64.ps1 ; .\build-win-arm64.ps1
  cd av1-native-tools/macos   && ./build-osx-arm64.sh && ./build-osx-x64.sh
  cd av1-native-tools         && ./build-host.sh       # dev-box .so for the tests

libaom is a big build: linux-x64 and the Android ABIs take a minute or two;
linux-arm64 and linux-riscv64 run under qemu-user emulation and take much
longer. Never kill a build that is merely slow.

Every script: copies aom/ shim/ to scratch (patches applied there), configures
shim/CMakeLists.txt with -DCBA_BUILD_SMOKE_TEST=ON and builds the two targets
codebrix_av1 + codebrix_av1_smoke, keeps an unstripped twin, strips, writes
LICENSE-Aom.txt beside the binary, runs THE GATE (architecture, exact exports,
system-only dependencies, glibc floor / 16 KB alignment / deployment target as
the platform needs, third_party objects == fastfeat + vector, nasm used on x64,
licence file, and the smoke test against the STAGED file), writes
output/<rid>/BUILD-INFO.txt (including the libaom SIMD configuration the RID
got), and - only when the gate passed - ADOPTS the result: copies <binary> +
LICENSE-Aom.txt into ../src/CodeBrix.VideoEncoding.Av1/runtimes/<rid>/native/
and the unstripped twin (or .pdb) into unstripped/<rid>/. ADOPT=0 (bash) /
-NoAdopt (PowerShell) skips that. Then update that RID's block in
BUILD-PROVENANCE.txt from BUILD-INFO.txt.

THE SMOKE TEST
--------------------------------------------------------------------------------
shim/smoke-test.c loads the library BY PATH (dlopen / LoadLibrary, as .NET
does) and resolves all 17 entry points by name. No decoder exists in this build
(and the smoke test links no libaom), so every check is at the bitstream level:
it parses OBUs, the sequence header and the start of key-frame headers itself.
Coverage: 48 frames at 320x180 in I420 8/10-bit, I444 8-bit, MONOCHROME 8/10-bit
full range, plus I420/I444 12-bit (Professional) and I444 10-bit; forced key
frame honoured; packets == frames; pts == frame index; temporal delimiter first;
one shown frame per packet; is_key_frame == shown KEY_FRAME; the sequence header
(profile, depth, mono, subsampling, colour description and range, chroma
position) equal to the one inside every key frame; lossless key frames have
base_q_idx 0 and no delta-q; kf-max-dist 0 / ALL_INTRA all-key; REALTIME one
packet per frame; unequal U/V strides byte-identical; odd sizes for I444 and
MONOCHROME; the LOCK-STEP property (colour with a scene cut + alpha without,
automatic key frames off: identical key-frame positions; with them on libaom
keys the cut); and every error path of the contract. ~1870 checks;
"SMOKE TEST PASSED" + exit 0. Bit-exact lossless round trips are proven by the
managed test suite (it decodes with dav1d).

THE LICENCE FILE (identical on all nine RIDs)
--------------------------------------------------------------------------------
  LICENSE-Aom.txt    licenses/LICENSE-Aom.txt, written by
                     licenses/compose-license-aom.sh --write:
                       aom/LICENSE (BSD-2-Clause, Alliance for Open Media)
                       + aom/PATENTS (AOM Patent License 1.0)
                       + third_party/fastfeat/LICENSE (BSD-3-Clause)
                       + third_party/vector/LICENSE (MIT)
                       + third_party/x86inc/LICENSE (ISC; compiled into the x86-64
                         RIDs only, listed everywhere so the file is identical)
  The bash scripts run `compose-license-aom.sh --check` (re-composes and
  re-runs the survey of every copyright line in the compilable sources) before
  copying it; every gate compares the copy again, and checks the build compiled
  no third_party code other than fastfeat and vector. The Windows gate
  re-composes it in PowerShell from aom/ and compares bytes.
  .gitattributes here marks aom/**, nasm/** and licenses/LICENSE-Aom.txt -text;
  the repository-root .gitattributes does the same for the ADOPTED copies
  (src/CodeBrix.VideoEncoding.Av1/runtimes/**).

FILES
--------------------------------------------------------------------------------
  aom/                    unmodified libaom 3.15.2 tree (UPSTREAM.txt)
  nasm/                   unmodified nasm 2.16.03 tree - build tool only (UPSTREAM.txt)
  upstream-downloads/     the release tarballs they came from (+ libaom's .asc)
  shim/                   codebrix_av1.h (contract), codebrix_av1.c, CMakeLists.txt,
                          codebrix_av1.map / .exports (export lists), smoke-test.c
  licenses/               LICENSE-Aom.txt + its generator / survey
  linux/                  build.sh, container-build.sh, Containerfile.<arch>, pins.env
                          (pins.env also feeds the Windows and macOS scripts)
  android/                build.sh, layout-check.c (ABI static asserts)
  windows/                build-common.ps1, build-win-x64.ps1, build-win-arm64.ps1
  macos/                  build-common.sh, build-osx-arm64.sh, build-osx-x64.sh
  build-host.sh           the not-shipped dev-box build
  build-nasm.sh           builds nasm/ for the machine it runs on (Linux x64 + host)
  patches/                empty (see its README)
  unstripped/             committed pre-strip twins for crash triage
  output/                 build results, git-ignored (except its README)
  BUILD-PROVENANCE.txt    what was built, when, with what, and the result
