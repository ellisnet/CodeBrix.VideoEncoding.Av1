#!/usr/bin/env bash
# ==============================================================================================
# build-common.sh - shared machinery for the macOS codebrix_av1 builds (osx-arm64, osx-x64)
# ==============================================================================================
#
# Sourced by build-osx-arm64.sh and build-osx-x64.sh; not meant to be run directly.
#
#   Written on Linux on 2026-10-10 as a copy of CodeBrix.Audio.FlacWriter's
#   flac-native-tools/macos/build-common.sh (itself a copy of CodeBrix.Audio.Vorbis's, which Jeremy
#   ran UNCHANGED and successfully on his Apple Silicon Mac the same day - macOS 27.0.1, Xcode 27.0
#   / Apple clang 21.0.0, cmake 4.4.3, GNU Make 3.81, /bin/bash 3.2; osx-x64 under Rosetta 2),
#   adapted only where libaom differs: one vendored tree (aom/, built ENCODER-ONLY through
#   libaom's own CMake), 17 exports, the smoke test is ../shim/smoke-test.c (built by the same CMake
#   run; it links no libaom), the licence file is a composition, and libaom's CMake needs Perl
#   (+ nasm for osx-x64's x86-64 assembly - required on PATH the dav1d way; yasm is never used).
#   AOM_TARGET_CPU is passed explicitly (arm64 / x86_64), because when cross-building with
#   CMAKE_OSX_ARCHITECTURES, CMAKE_SYSTEM_PROCESSOR is the HOST's and libaom would otherwise pick
#   the host's SIMD. Everything else - the `head -1` SIGPIPE trap, strip -> codesign ordering,
#   size measured after signing, the deployment-target stamp CHECKED rather than reported, LC_UUID
#   equal in the stripped and unstripped files - is unchanged.
#
#   FIRST RUN for libaom 2026-10-11 UTC (2026-10-10 evening, local) on the same Mac (/usr/bin/perl
#   5.34, Homebrew NASM 3.02, Rosetta 2): both architecture scripts ran UNCHANGED, passed the
#   complete gate (1871-check smoke test included) and adopted - see ../BUILD-PROVENANCE.txt. If
#   something breaks on another machine, fix it IN THE SCRIPT and record it there.
#
# Everything it needs is in this repository: ../aom (the unmodified libaom 3.15.2 release tree),
# ../shim (the codebrix_av1 shim, its CMakeLists.txt and smoke-test.c), ../patches (empty), and
# ../linux/pins.env (the version pins shared by every platform; only its platform-neutral keys are
# used here). Nothing is downloaded. (../nasm is not built here: the Mac's own nasm is used, as for
# dav1d.)
#
# Tools: the Xcode Command Line Tools (cc, c++, make, otool, nm, lipo, file, dsymutil, dwarfdump,
# strip, codesign, shasum, xcrun), cmake >= 3.16, perl (macOS's /usr/bin/perl is fine) and, for
# osx-x64 only, nasm. No ninja needed (Unix Makefiles).
# ==============================================================================================

set -euo pipefail

trap 'rc=$?; echo "ERROR: the macOS codebrix_av1 build failed (exit $rc) at line $LINENO: $BASH_COMMAND" >&2' ERR

first_line() { sed -n '1p'; }

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"     # av1-native-tools/
REPO_DIR="$(cd "$TOOLS_DIR/.." && pwd)"
PINS_FILE="$TOOLS_DIR/linux/pins.env"

[ -f "$PINS_FILE" ] || {
    echo "ERROR: $PINS_FILE is missing. It is part of the repository; if it vanished after a" >&2
    echo "       clone, check the root .gitignore's blanket '*.env' rule (see ../.gitignore)." >&2
    exit 1; }
# Platform-neutral keys used: AOM_DIR, AOM_VERSION, NASM_VERSION, REQUIRED_SYMBOLS, LICENSE_AOM_NAME.
# shellcheck source=../linux/pins.env
. "$PINS_FILE"

PATCH_DIR="$TOOLS_DIR/patches"
SMOKE_SRC="$TOOLS_DIR/shim/smoke-test.c"
LIB_NAME="libcodebrix_av1.dylib"     # the name DllImport("codebrix_av1") probes on macOS
# shim/CMakeLists.txt sets INSTALL_NAME_DIR @rpath and no VERSION/SOVERSION, so the build gives
# exactly one regular file with this install name. .NET loads the file by path; the id is not
# consulted, but it is checked so a CMake change cannot slip through unnoticed.
EXPECTED_INSTALL_NAME="@rpath/libcodebrix_av1.dylib"
RUNTIMES_DIR="$REPO_DIR/src/CodeBrix.VideoEncoding.Av1/runtimes"
UNSTRIPPED_DIR="$TOOLS_DIR/unstripped"

# ----------------------------------------------------------------------------------------------
# Deployment targets - the oldest macOS each slice loads on. The same floors as CodeBrix.Sdl3's
# macOS slices, so every CodeBrix native in an app shares one floor per architecture:
#   osx-arm64 11.0   the first macOS that exists on Apple Silicon
#   osx-x64   10.14
# libaom uses only libc/libm/pthreads (and sysctl for the CPU count), so there is nothing newer to
# guard; the gate still checks the stamp AND that no -Wunguarded-availability warning appeared.
# ----------------------------------------------------------------------------------------------
MACOS_MIN_ARM64="11.0"
MACOS_MIN_X64="10.14"

GATE_FAILED=0
UNRUN_ONLY=1        # stays 1 while every failure so far is an "unrun" one (see gate_unrun)
gate_pass()  { echo "  [ok] $1"; }
gate_fail()  { echo "  [FAIL] $1"; GATE_FAILED=1; UNRUN_ONLY=0; }
gate_unrun() { echo "  [FAIL] $1"; GATE_FAILED=1; }

require_tool() {
    local tool="$1" hint="$2"
    command -v "$tool" > /dev/null 2>&1 || {
        echo "ERROR: $tool is not on PATH." >&2
        echo >&2
        echo "$hint" >&2
        echo >&2
        echo "This script installs nothing. See ../README.txt." >&2
        exit 1; }
}

check_common_prerequisites() {
    require_tool cc      'Install the Xcode Command Line Tools:   xcode-select --install'
    require_tool c++     'Install the Xcode Command Line Tools:   xcode-select --install   (libaom'"'"'s CMake project enables C++)'
    require_tool make    'Install the Xcode Command Line Tools:   xcode-select --install'
    require_tool cmake   'Install it with Homebrew:   brew install cmake   (>= 3.16)'
    require_tool perl    'libaom'"'"'s CMake generates its CPU-dispatch headers with Perl. macOS ships /usr/bin/perl; if it is gone:   brew install perl'
    for t in otool nm lipo file dsymutil dwarfdump strip codesign shasum xcrun; do
        require_tool "$t" 'Part of the Xcode Command Line Tools:   xcode-select --install'
    done
    for f in "$TOOLS_DIR/$AOM_DIR/av1/av1_cx_iface.c" "$TOOLS_DIR/$AOM_DIR/CMakeLists.txt" \
             "$TOOLS_DIR/$AOM_DIR/LICENSE" "$TOOLS_DIR/$AOM_DIR/PATENTS" \
             "$TOOLS_DIR/shim/CMakeLists.txt" "$TOOLS_DIR/shim/codebrix_av1.c" "$TOOLS_DIR/shim/codebrix_av1.exports" "$SMOKE_SRC" \
             "$TOOLS_DIR/licenses/LICENSE-Aom.txt" "$TOOLS_DIR/licenses/compose-license-aom.sh"; do
        [ -f "$f" ] || {
            echo "ERROR: $f is missing. It is part of the repository and is not downloaded -" >&2
            echo "       restore it from git." >&2
            exit 1; }
    done
}

# ----------------------------------------------------------------------------------------------
# aom/ shim/ are copied to scratch and built there; the vendored tree is never written.
# ----------------------------------------------------------------------------------------------
copy_source_to_scratch() {
    local scratch="$1"
    rm -rf "$scratch"
    mkdir -p "$scratch"
    cp -a "$TOOLS_DIR/$AOM_DIR" "$scratch/aom"
    cp -a "$TOOLS_DIR/shim" "$scratch/shim"
    echo "  copied aom/ shim/ -> $scratch"

    PATCHES_APPLIED="none"
    if [ -d "$PATCH_DIR" ] && ls "$PATCH_DIR"/*.patch > /dev/null 2>&1; then
        PATCHES_APPLIED=""
        for p in "$PATCH_DIR"/*.patch; do
            echo "  applying $(basename "$p")"
            ( cd "$scratch" && patch -p1 --forward --batch < "$p" ) \
                || { echo "ERROR: patch $(basename "$p") did not apply. Patches are never applied best-effort." >&2; exit 1; }
            PATCHES_APPLIED="$PATCHES_APPLIED $(basename "$p")"
        done
        PATCHES_APPLIED="${PATCHES_APPLIED# }"
    fi
    echo "  patches applied: $PATCHES_APPLIED"
}

# ----------------------------------------------------------------------------------------------
# Configure + build.   $1 scratch   $2 build dir   $3 arch (arm64 | x86_64)   $4 deployment target
# -g so dsymutil has DWARF to collect (the shipped file is stripped afterwards); the prefix map
# keeps the scratch path out of the debug info. CBA_BUILD_SMOKE_TEST=ON: the same CMake also
# builds codebrix_av1_smoke (the gate's program) for the same architecture and deployment target.
# AOM_TARGET_CPU = the slice's architecture (see the header). For x86_64 the shim's CMakeLists
# requires nasm on PATH and pins it as libaom's assembler (Mach-O macho64 objects).
# ----------------------------------------------------------------------------------------------
build_lib() {
    local scratch="$1" build="$2" arch="$3" minos="$4"
    rm -rf "$build"
    mkdir -p "$build"
    export MACOSX_DEPLOYMENT_TARGET="$minos"
    local cflags="-g -ffile-prefix-map=$scratch=av1-native-tools -ffile-prefix-map=$build=build"

    CMAKE_COMMAND_LINE="cmake -S <scratch>/shim -B <build> -G \"Unix Makefiles\" -DCMAKE_BUILD_TYPE=Release -DCBA_BUILD_SMOKE_TEST=ON -DAOM_TARGET_CPU=$arch -DCMAKE_OSX_ARCHITECTURES=$arch -DCMAKE_OSX_DEPLOYMENT_TARGET=$minos -DCMAKE_C_FLAGS=\"-g -ffile-prefix-map=...\" -DCMAKE_CXX_FLAGS=(same)"
    echo "  $CMAKE_COMMAND_LINE"
    cmake -S "$scratch/shim" -B "$build" -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=Release \
        -DCBA_BUILD_SMOKE_TEST=ON \
        -DAOM_TARGET_CPU="$arch" \
        -DCMAKE_OSX_ARCHITECTURES="$arch" \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$minos" \
        -DCMAKE_C_FLAGS="$cflags" \
        -DCMAKE_CXX_FLAGS="$cflags" \
        > "$build/configure.log" 2>&1 \
        || { echo "ERROR: cmake configure failed - see $build/configure.log" >&2; tail -30 "$build/configure.log" >&2; exit 1; }
    grep -E 'CMake Warning|CMake Error' "$build/configure.log" | sed 's/^/  /' || true

    echo "  building (log: $build/build.log) ..."
    cmake --build "$build" -j "$(sysctl -n hw.ncpu)" --target codebrix_av1 codebrix_av1_smoke -- VERBOSE=1 > "$build/build.log" 2>&1 \
        || { echo "ERROR: build failed - see $build/build.log" >&2; grep -E -B2 -A8 'error:' "$build/build.log" | sed -n '1,60p' >&2; exit 1; }
    SHIM_WARNINGS="$(grep 'warning:' "$build/build.log" | grep -c -E 'codebrix_av1\.c|smoke-test\.c' || true)"
    ALL_WARNINGS="$(grep -c 'warning:' "$build/build.log" || true)"
    AOM_CONFIG="$(grep -E '^#define (AOM_ARCH_[A-Z0-9_]+|HAVE_[A-Z0-9_]+|CONFIG_(RUNTIME_CPU_DETECT|MULTITHREAD|AV1_HIGHBITDEPTH|AV1_ENCODER|AV1_DECODER|LIBYUV|WEBM_IO|PIC)) ' "$build/aom/config/aom_config.h" \
        | awk '$3 != "0" || $2 ~ /^CONFIG_/ {print $2"="$3}' | tr '\n' ' ' | sed 's/ *$//' || true)"
    ASM_USED="$(cat "$build"/aom/CMakeFiles/*/CMakeASM_NASMCompiler.cmake "$build"/CMakeFiles/*/CMakeASM_NASMCompiler.cmake 2>/dev/null | sed -n 's/^set(CMAKE_ASM_NASM_COMPILER "\(.*\)")$/\1/p' | sed -n '1p' || true)"
    THIRD_PARTY="$(find "$build" -name '*.o' -path '*third_party*' | sed -E 's|.*/third_party/([^/]+)/.*|\1|' | LC_ALL=C sort -u | tr '\n' ' ' | sed 's/ *$//')"
    echo "  built; warnings: shim + smoke test $SHIM_WARNINGS, total incl. libaom $ALL_WARNINGS"
    echo "  libaom configuration: $AOM_CONFIG"
    echo "  assembler: ${ASM_USED:-none}; third_party compiled: $THIRD_PARTY"

    UNGUARDED_AVAILABILITY_WARNINGS="$(grep -c -E 'Wunguarded-availability' "$build/build.log" || true)"
    echo "  -Wunguarded-availability warnings in the build log: $UNGUARDED_AVAILABILITY_WARNINGS"
}

# ----------------------------------------------------------------------------------------------
# Collect: unstripped copy, dSYM, strip, sign, licences.   $1 build dir   $2 output dir
# Order matters: strip, THEN codesign (stripping invalidates a signature).
# ----------------------------------------------------------------------------------------------
collect_dylib() {
    local build="$1" out="$2"
    [ -f "$build/$LIB_NAME" ] && [ ! -L "$build/$LIB_NAME" ] || {
        echo "ERROR: $build/$LIB_NAME was not produced as a regular file." >&2; ls -l "$build"/*.dylib >&2 || true; exit 1; }

    rm -rf "$out"
    mkdir -p "$out/unstripped"
    cp "$build/$LIB_NAME" "$out/unstripped/$LIB_NAME"
    cp "$build/$LIB_NAME" "$out/$LIB_NAME"
    chmod 0755 "$out/$LIB_NAME" "$out/unstripped/$LIB_NAME"
    SIZE_UNSTRIPPED="$(stat -f %z "$out/unstripped/$LIB_NAME")"

    dsymutil "$out/unstripped/$LIB_NAME" -o "$out/$LIB_NAME.dSYM" || \
        echo "  [warn] dsymutil failed; crash reports from this build will be harder to read."

    strip -x "$out/$LIB_NAME"
    codesign --force --sign - "$out/$LIB_NAME"
    SIZE_STRIPPED="$(stat -f %z "$out/$LIB_NAME")"     # after signing: the shipped size

    bash "$TOOLS_DIR/licenses/compose-license-aom.sh" --check
    cp "$TOOLS_DIR/licenses/$LICENSE_AOM_NAME" "$out/$LICENSE_AOM_NAME"
    echo "  unstripped: $SIZE_UNSTRIPPED bytes -> stripped + signed: $SIZE_STRIPPED bytes"
    echo "  $LICENSE_AOM_NAME = licenses/LICENSE-Aom.txt (libaom LICENSE + PATENTS + fastfeat / vector / x86inc)"
}

# The smoke test, built by the same CMake run for the TARGET architecture at the same deployment
# target (codebrix_av1_smoke: shim/smoke-test.c; it links no libaom).
build_smoke_test() {
    local gate="$1" build="$2"
    mkdir -p "$gate"
    [ -f "$build/codebrix_av1_smoke" ] || { echo "ERROR: $build/codebrix_av1_smoke was not built - see $build/build.log" >&2; exit 1; }
    cp "$build/codebrix_av1_smoke" "$gate/smoke-test"
    echo "  $gate/smoke-test ($(lipo -archs "$gate/smoke-test" 2>/dev/null || echo "arch unknown"))"
}

# ----------------------------------------------------------------------------------------------
# THE GATE.   $1 staged dylib   $2 arch   $3 minimum macOS   $4 gate dir   $5 can run (yes|no)
# ----------------------------------------------------------------------------------------------
verify_dylib() {
    local dylib="$1" want_arch="$2" want_minos="$3" gate="$4" can_run="$5"
    SMOKE_OUTPUT="  not run"

    # 1. Architecture, two ways; must be THIN (one file per RID, never universal).
    local file_out lipo_out
    file_out="$(file -b "$dylib")"
    case "$file_out" in
        *"Mach-O 64-bit"*"$want_arch"*) gate_pass "file: $file_out" ;;
        *)                              gate_fail "architecture mismatch: expected a $want_arch Mach-O, file says: $file_out" ;;
    esac
    lipo_out="$(lipo -info "$dylib" 2>&1)"
    case "$lipo_out" in
        *"Non-fat file"*"is architecture: $want_arch"*) gate_pass "lipo: thin, $want_arch" ;;
        *)                                              gate_fail "lipo -info: '$lipo_out' (expected a non-fat $want_arch file)" ;;
    esac

    # 2. Exports: EXACTLY the 17 functions of codebrix_av1.h; every libaom symbol hidden.
    local expected actual missing extra
    expected="$(printf '%s\n' $REQUIRED_SYMBOLS | LC_ALL=C sort -u)"
    actual="$(nm -gU "$dylib" | awk '{print $NF}' | sed 's/^_//' | LC_ALL=C sort -u)"
    missing="$(LC_ALL=C comm -23 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") | tr '\n' ' ')"
    extra="$(LC_ALL=C comm -13 <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") | tr '\n' ' ')"
    [ -z "${missing// /}" ] || gate_fail "missing exports: $missing"
    [ -z "${extra// /}" ]   || gate_fail "unexpected exports (libaom must stay hidden): $(printf '%s' "$extra" | cut -c1-300)"
    if [ -z "${missing// /}" ] && [ -z "${extra// /}" ]; then
        gate_pass "exports are exactly the $(printf '%s\n' "$expected" | grep -c .) codebrix_av1_* functions"
    fi

    # 3. Install name.
    local install_name
    install_name="$(otool -D "$dylib" | sed -n '2p')"
    if [ "$install_name" = "$EXPECTED_INSTALL_NAME" ]; then gate_pass "install name: $install_name"
    else gate_fail "install name is '$install_name', expected $EXPECTED_INSTALL_NAME"; fi

    # 4. Dependencies: system libraries only (libSystem). Nothing from /usr/local or /opt (Homebrew).
    local deps bad=""
    deps="$(otool -L "$dylib" | tail -n +2 | awk '{print $1}' | grep -v -x "$EXPECTED_INSTALL_NAME" || true)"
    for d in $deps; do
        case "$d" in
            /usr/lib/*|/System/Library/Frameworks/*) ;;
            *) bad="$bad $d" ;;
        esac
    done
    if [ -n "$bad" ]; then gate_fail "non-system dynamic dependencies:$bad"
    else gate_pass "dependencies are system-only: $(printf '%s ' $deps)"; fi

    # 5. No LC_RPATH.
    if otool -l "$dylib" | grep -q 'cmd LC_RPATH'; then gate_fail "the dylib carries an LC_RPATH"
    else gate_pass "no LC_RPATH"; fi

    # 6. Deployment target - CHECKED, not merely reported.
    local minos
    minos="$(otool -l "$dylib" | awk '/LC_BUILD_VERSION/{f=1} f && /minos/{print $2; exit}')"
    if [ -z "$minos" ]; then
        minos="$(otool -l "$dylib" | awk '/LC_VERSION_MIN_MACOSX/{f=1} f && /version/{print $2; exit}')"
    fi
    if [ "$minos" = "$want_minos" ]; then gate_pass "minimum macOS: $minos"
    else gate_fail "minimum macOS is '$minos', expected $want_minos - a build on a newer Mac must not silently raise the floor"; fi
    MINOS_FOUND="$minos"

    # 7. ...and the stamp is a real floor.
    if [ "${UNGUARDED_AVAILABILITY_WARNINGS:-0}" -eq 0 ]; then gate_pass "no -Wunguarded-availability warnings in the build log"
    else gate_fail "$UNGUARDED_AVAILABILITY_WARNINGS -Wunguarded-availability warning lines in build.log - the $want_minos floor is not real"; fi
    WEAK_IMPORTS="$(nm -m "$dylib" | grep -c 'weak external' || true)"
    echo "  [info] weak imports: $WEAK_IMPORTS"

    # 8. Code signature, valid after strip.
    if codesign -v "$dylib" > /dev/null 2>&1; then gate_pass "code signature valid (ad-hoc)"
    else gate_fail "codesign -v fails - Apple Silicon refuses an unsigned or broken-signature arm64 dylib"; fi

    # 9. Licence file exact, third_party == fastfeat + vector, x86_64 assembled by nasm.
    local outdir; outdir="$(dirname "$dylib")"
    if cmp -s "$outdir/$LICENSE_AOM_NAME" <(bash "$TOOLS_DIR/licenses/compose-license-aom.sh"); then
        gate_pass "$LICENSE_AOM_NAME is exact (licenses/compose-license-aom.sh)"
    else
        gate_fail "$LICENSE_AOM_NAME differs from licenses/compose-license-aom.sh output"
    fi
    if [ "$THIRD_PARTY" = "fastfeat vector" ]; then gate_pass "third_party compiled: exactly fastfeat + vector"
    else gate_fail "third_party compiled: '$THIRD_PARTY' (expected 'fastfeat vector') - review ../licenses/compose-license-aom.sh"; fi
    if [ "$want_arch" = "x86_64" ]; then
        case "$ASM_USED" in
            */nasm) gate_pass "libaom's x86-64 assembly built with nasm ($ASM_USED)" ;;
            *)      gate_fail "libaom's assembler is '$ASM_USED', not nasm" ;;
        esac
    fi

    if [ "$can_run" != "yes" ]; then
        gate_unrun "smoke test NOT RUN - this machine cannot execute $want_arch code. Reported as a failure on purpose: an unrun check is not a passed check."
        return
    fi

    # 10. dlopen smoke test against the STAGED file, copied beside the program and passed by full
    #     path, so the shipped bytes are what loads.
    echo "  --- dlopen smoke test ---"
    cp "$dylib" "$gate/$LIB_NAME"
    local rc=0
    SMOKE_OUTPUT="$("$gate/smoke-test" "$gate/$LIB_NAME" 2>&1)" || rc=$?
    SMOKE_OUTPUT="$(printf '%s\n' "$SMOKE_OUTPUT" | grep -v '^ok:' || true)"     # ~1900 "ok:" lines
    printf '%s\n' "$SMOKE_OUTPUT" | sed 's/^/    /'
    if [ "$rc" -eq 0 ]; then gate_pass "smoke test ($(printf '%s\n' "$SMOKE_OUTPUT" | grep -E '^[0-9]+ checks' | first_line))"
    else gate_fail "smoke test exited $rc"; fi
}

# ----------------------------------------------------------------------------------------------
# THE WHOLE BUILD, for one RID.
#   $1 RID   $2 arch (arm64 | x86_64)   $3 deployment target   $4 can run (yes | no)
#   $5 route text   $6 script name
# Exit code: 0 = every check ran and passed (and, unless ADOPT=0, the result was adopted);
#            1 = a check failed OR could not run (nothing adopted).
# ----------------------------------------------------------------------------------------------
run_macos_build() {
    RID="$1"; local arch="$2" minos="$3" can_run="$4" route="$5" script_name="$6"
    local out="$TOOLS_DIR/output/$RID"
    local scratch="/tmp/codebrix-av1-src-$RID"
    local build="/tmp/codebrix-av1-build-$RID"
    local gate="/tmp/codebrix-av1-gate-$RID"
    local started_at start_seconds="$SECONDS"
    started_at="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"

    echo "=============================================================================="
    echo " codebrix_av1 (libaom $AOM_VERSION) - $RID"
    echo "=============================================================================="
    echo " started : $started_at"
    echo " host    : $(uname -m), macOS $(sw_vers -productVersion 2>/dev/null || echo unknown)"
    echo " route   : $route"
    echo " source  : $TOOLS_DIR/{aom,shim}  (vendored - nothing is downloaded)"
    echo

    echo "--- prerequisites ---"
    check_common_prerequisites
    local cc_version sdk_version
    cc_version="$(cc --version | first_line)"
    sdk_version="$(xcrun --show-sdk-version 2>/dev/null || echo unknown)"
    echo "  cc       : $cc_version"
    echo "  SDK      : macOS $sdk_version"
    echo "  cmake    : $(cmake --version | first_line)"
    PERL_ACTUAL="$(perl -e 'printf "perl %vd", $^V')"
    echo "  perl     : $PERL_ACTUAL"
    NASM_ACTUAL="not needed (no x86 assembly for $arch)"
    if [ "$arch" = "x86_64" ]; then
        require_tool nasm "libaom's x86-64 assembly needs nasm (the Linux/Android builds use $NASM_VERSION; any 2.14+ works). Install it with Homebrew:   brew install nasm"
        NASM_ACTUAL="$(nasm -v | first_line)  [$(command -v nasm)]"
    fi
    echo "  nasm     : $NASM_ACTUAL"
    echo "  min macOS: $minos"
    if [ "$can_run" = "yes" ]; then
        echo "  execute  : $arch code runs on this machine - the full gate will be applied"
    else
        echo "  execute  : NOT AVAILABLE for $arch on this machine. The build and the static checks run;"
        echo "             the smoke test cannot, and is reported as a FAILURE."
    fi
    echo

    echo "--- source ---"
    copy_source_to_scratch "$scratch"
    echo

    echo "--- building ---"
    build_lib "$scratch" "$build" "$arch" "$minos"
    echo

    echo "--- smoke test program ---"
    rm -rf "$gate"
    build_smoke_test "$gate" "$build"
    echo

    echo "--- collecting ---"
    collect_dylib "$build" "$out"
    echo

    echo "--- verifying ---"
    verify_dylib "$out/$LIB_NAME" "$arch" "$minos" "$gate" "$can_run"

    if [ "$GATE_FAILED" -ne 0 ] && [ "$UNRUN_ONLY" -ne 1 ]; then
        echo
        echo "VERIFICATION FAILED for $RID - see above. $out is left for inspection, but this build"
        echo "must not be adopted into the package."
        exit 1
    fi
    local gate_status
    if [ "$GATE_FAILED" -eq 0 ]; then gate_status="COMPLETE - every check ran and passed"
    else gate_status="INCOMPLETE - every check that could run passed; the smoke test is UNRUN ($arch cannot execute here)."; fi
    echo

    # --- record ---------------------------------------------------------------------------------
    local sha sha_unstripped uuid uuid_unstripped elapsed
    sha="$(shasum -a 256 "$out/$LIB_NAME" | cut -d' ' -f1)"
    sha_unstripped="$(shasum -a 256 "$out/unstripped/$LIB_NAME" | cut -d' ' -f1)"
    uuid="$(dwarfdump --uuid "$out/$LIB_NAME" 2>/dev/null | awk '{print $2}' | first_line)"
    uuid_unstripped="$(dwarfdump --uuid "$out/unstripped/$LIB_NAME" 2>/dev/null | awk '{print $2}' | first_line)"
    if [ -z "$uuid" ] || [ "$uuid" != "$uuid_unstripped" ]; then
        echo "  [FAIL] LC_UUID '$uuid' (stripped) != '$uuid_unstripped' (unstripped)"
        exit 1
    fi
    ( cd "$out" && shasum -a 256 "$LIB_NAME" "unstripped/$LIB_NAME" "$LICENSE_AOM_NAME" > SHA256SUMS.txt )
    cp "$build/build.log" "$out/build.log" 2>/dev/null || true
    elapsed=$(( SECONDS - start_seconds ))

    cat > "$out/BUILD-INFO.txt" <<INFO
codebrix_av1 native library - build information
==============================================================================
RID              : $RID
Gate status      : $gate_status
Built            : $started_at
Build duration   : ${elapsed}s
Built by         : av1-native-tools/macos/$script_name  ($route)

Build machine
------------------------------------------------------------------------------
macOS            : $(sw_vers -productVersion 2>/dev/null || echo unknown) ($(uname -m))
Compiler         : $cc_version
SDK              : macOS $sdk_version
cmake            : $(cmake --version | first_line)
make             : $(make --version 2>/dev/null | first_line)
$arch execution : $can_run
perl             : $PERL_ACTUAL
nasm             : $NASM_ACTUAL

Source (vendored in-repo; nothing fetched at build time)
------------------------------------------------------------------------------
libaom           : $AOM_VERSION (aom/)
Shim             : shim/codebrix_av1.c + codebrix_av1.h (API version 1)
Patches applied  : $PATCHES_APPLIED

Configuration
------------------------------------------------------------------------------
cmake            : $CMAKE_COMMAND_LINE
Deployment target: $minos (MACOSX_DEPLOYMENT_TARGET and CMAKE_OSX_DEPLOYMENT_TARGET; checked: $MINOS_FOUND)
Unguarded-availability warnings: $UNGUARDED_AVAILABILITY_WARNINGS
Weak imports     : $WEAK_IMPORTS
libaom config    : $AOM_CONFIG
assembler        : ${ASM_USED:-none}
third_party      : $THIRD_PARTY
Build warnings   : shim + smoke test $SHIM_WARNINGS; total incl. libaom (its own warning set) $ALL_WARNINGS

Result
------------------------------------------------------------------------------
File             : $LIB_NAME
Install name     : $(otool -D "$out/$LIB_NAME" | sed -n '2p')
Size (stripped)  : $SIZE_STRIPPED bytes (after signing - the shipped size)
Size (unstripped): $SIZE_UNSTRIPPED bytes -> unstripped/$LIB_NAME
SHA256           : $sha
SHA256 unstripped: $sha_unstripped
LC_UUID          : $uuid  (unstripped: $uuid_unstripped)
Debug symbols    : $LIB_NAME.dSYM (not shipped)
Signature        : ad-hoc (codesign --sign -), applied after strip
Dependencies     : $(otool -L "$out/$LIB_NAME" | tail -n +2 | awk '{print $1}' | grep -v -x "$EXPECTED_INSTALL_NAME" | tr '\n' ' ')
Licence beside   : $LICENSE_AOM_NAME (libaom BSD-2-Clause + AOM Patent License 1.0 + fastfeat BSD-3 / vector MIT / x86inc ISC)

Smoke test ($([ "$can_run" = "yes" ] && echo 'ran on this machine' || echo 'NOT RUN'))
------------------------------------------------------------------------------
$(printf '%s\n' "$SMOKE_OUTPUT" | sed 's/^/  /')
INFO

    echo "--- done ---"
    echo "  $out/$LIB_NAME"
    echo "  sha256 $sha"
    echo "  LC_UUID $uuid"
    echo "  ${elapsed}s"
    echo
    if [ "$GATE_FAILED" -ne 0 ]; then
        echo "GATE INCOMPLETE: this dylib has NOT been executed and was NOT adopted. Re-run the script"
        echo "for $RID on a Mac that runs $arch code (native, or x86_64 under Rosetta 2)."
        exit 1
    fi

    # --- adopt -------------------------------------------------------------------------------------
    if [ "${ADOPT:-1}" = "0" ]; then
        echo "ADOPT=0: not copied into the package. Result stays in output/$RID."
    else
        local dest="$RUNTIMES_DIR/$RID/native"
        mkdir -p "$dest" "$UNSTRIPPED_DIR/$RID"
        cp "$out/$LIB_NAME" "$out/$LICENSE_AOM_NAME" "$dest/"
        cp "$out/unstripped/$LIB_NAME" "$UNSTRIPPED_DIR/$RID/$LIB_NAME"
        echo "adopted -> src/CodeBrix.VideoEncoding.Av1/runtimes/$RID/native/ ($LIB_NAME + $LICENSE_AOM_NAME)"
        echo "        -> av1-native-tools/unstripped/$RID/$LIB_NAME"
    fi
    echo "Record the build in ../BUILD-PROVENANCE.txt (replace the block for $RID) from"
    echo "output/$RID/BUILD-INFO.txt, then commit the adopted files."
}
