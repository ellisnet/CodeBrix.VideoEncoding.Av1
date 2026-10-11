#!/usr/bin/env bash
# ==============================================================================================
# build-osx-x64.sh - build libcodebrix_av1.dylib for the osx-x64 runtime identifier
# ==============================================================================================
#
#   Written on Linux on 2026-10-10 as a copy of CodeBrix.Audio.FlacWriter's script of the same
#   name (a copy of CodeBrix.Audio.Vorbis's, run successfully, unchanged, by Jeremy on his Apple
#   Silicon Mac that day), adapted to libaom.
#   FIRST RUN for libaom 2026-10-11 UTC (2026-10-10 evening, local), same Mac with Rosetta 2 and
#   Homebrew NASM 3.02: ran UNCHANGED, the complete gate passed under Rosetta 2 and the result was
#   adopted - see ../BUILD-PROVENANCE.txt. If something breaks on another machine, fix it IN THE
#   SCRIPT (or build-common.sh) and record it there.
#
# USAGE
#     cd av1-native-tools/macos
#     ./build-osx-x64.sh              # builds, gates, and (gate COMPLETE) adopts into src/
#     ADOPT=0 ./build-osx-x64.sh      # build + gate only
#
# TWO ROUTES, chosen automatically from the host:
#   * On an Intel Mac (uname -m = x86_64): native. The whole gate runs.
#   * On an Apple Silicon Mac: a cross build via -DCMAKE_OSX_ARCHITECTURES=x86_64. ROSETTA 2 IS
#     NEEDED TO VERIFY, not to build: the smoke test has to RUN x86_64 code. Without Rosetta it
#     is reported as a FAILURE (nothing adopted):
#         softwareupdate --install-rosetta
#
# NEEDS nasm on PATH (libaom's x86-64 assembly; yasm is never used) - the dav1d way:
#     brew install nasm          (dav1d's build-osx-x64.sh notes nasm 3.02 on Jeremy's Mac)
# build-common.sh checks it (require_tool) and the gate checks libaom really used it.
#
# Deployment target 10.14 (MACOS_MIN_X64 in build-common.sh). The two macOS slices stay TWO THIN
# DYLIBS in two RID folders, deliberately not a universal binary.
# Output: ../output/osx-x64/.  It installs nothing.
# ==============================================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=build-common.sh
. "$SCRIPT_DIR/build-common.sh"

[ "$(uname -s)" = "Darwin" ] || { echo "ERROR: run this on macOS." >&2; exit 1; }

if [ "$(uname -m)" = "x86_64" ]; then
    CAN_RUN=yes
    ROUTE="native (Intel Mac)"
else
    ROUTE="cross-compiled on $(uname -m) via CMAKE_OSX_ARCHITECTURES=x86_64"
    # Rosetta 2 present? (the same test CodeBrix.Sdl3 / llama-native-tools use)
    if /usr/bin/pgrep -q oahd 2>/dev/null || [ -f /Library/Apple/usr/libexec/oah/libRosettaRuntime ]; then
        CAN_RUN=yes
        ROUTE="$ROUTE, verified under Rosetta 2"
    else
        CAN_RUN=no
        ROUTE="$ROUTE - Rosetta 2 NOT installed, smoke test cannot run"
    fi
fi

run_macos_build osx-x64 x86_64 "$MACOS_MIN_X64" "$CAN_RUN" "$ROUTE" build-osx-x64.sh
