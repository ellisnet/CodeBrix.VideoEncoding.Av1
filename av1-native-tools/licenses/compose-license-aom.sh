#!/usr/bin/env bash
# ==============================================================================================
# compose-license-aom.sh - THE generator of licenses/LICENSE-Aom.txt (one source of truth)
# ==============================================================================================
#
#   ./compose-license-aom.sh            print the composed text to stdout
#   ./compose-license-aom.sh --write    (re)write licenses/LICENSE-Aom.txt
#   ./compose-license-aom.sh --check    exit 1 unless licenses/LICENSE-Aom.txt is current AND the
#                                       compiled-source notice survey below still holds
#
# LICENSE-Aom.txt is shipped beside every codebrix_av1 binary, byte-identical on all nine RIDs. It
# is, in this order:
#   1. aom/LICENSE  VERBATIM - libaom's BSD-2-Clause licence (Alliance for Open Media)
#   2. aom/PATENTS  VERBATIM - the Alliance for Open Media Patent License 1.0
#   3. the licence texts of the third_party components whose code is compiled into the encoder:
#        third_party/fastfeat/LICENSE  BSD-3-Clause (Edward Rosten) - FAST corner detector, used by
#                                      the encoder's global-motion search; every RID
#        third_party/vector/LICENSE    MIT (Peter Goldsborough) - the vector container the encoder
#                                      uses; every RID
#        third_party/x86inc/LICENSE    ISC (x264 project) - the x86 assembly framework every .asm
#                                      file of libaom includes; compiled into the x86-64 RIDs only
#                                      (linux-x64, android-x64, win-x64, osx-x64), listed for all
#                                      so the file is identical everywhere
#
# NOT included, because none of it is compiled (shim/CMakeLists.txt: CONFIG_LIBYUV=0,
# CONFIG_WEBM_IO=0, CONFIG_HIGHWAY=0 (libaom's default), ENABLE_TESTS=0): third_party/libyuv,
# third_party/libwebm, third_party/highway, third_party/googletest. The linux / android gates
# also check that the only third_party objects in a build are fastfeat's and vector's.
#
# Survey (2026-10-10, libaom 3.15.2): every "Copyright" line in the sources that can be compiled
# into codebrix_av1 - aom/, aom_dsp/, aom_mem/, aom_ports/, aom_scale/, aom_util/, av1/,
# common/args_helper.{c,h} and the three third_party folders above - names the Alliance for Open
# Media, "The WebM project authors" (ARM NEON files that carry the AOM BSD-2-Clause + Patent
# License terms in the same header - checked below), Edward Rosten (fastfeat), Peter Goldsborough
# (vector) or the x264 project (x86inc). --check re-runs that survey so a libaom upgrade that
# brings in a new holder fails loudly here instead of shipping an incomplete licence file.
#
# Every bash build script (linux, android, macos, build-host.sh) runs --check before copying
# licenses/LICENSE-Aom.txt beside the binary; the Windows scripts copy the committed file and
# check it equals what this script composes from the vendored tree (re-implemented in PowerShell).
# Reads only this repository.
# ==============================================================================================
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AOM_DIR="$TOOLS_DIR/aom"
TARGET="$TOOLS_DIR/licenses/LICENSE-Aom.txt"

rule() { printf '%s\n' '----------------------------------------------------------------------------------------------'; }

compose() {
    for f in LICENSE PATENTS third_party/fastfeat/LICENSE third_party/vector/LICENSE third_party/x86inc/LICENSE; do
        [ -f "$AOM_DIR/$f" ] || { echo "ERROR: aom/$f missing" >&2; exit 1; }
    done
    cat "$AOM_DIR/LICENSE"
    printf '\n'
    rule
    printf '%s\n' 'Alliance for Open Media Patent License 1.0 (aom/PATENTS, verbatim):'
    rule
    printf '\n'
    cat "$AOM_DIR/PATENTS"
    printf '\n'
    rule
    printf '%s\n' 'Third-party code compiled into this library, with its licence (verbatim from libaom'"'"'s'
    printf '%s\n' 'third_party/ folder):'
    rule
    printf '\n'
    printf '%s\n' '=== FAST corner detector - third_party/fastfeat (BSD-3-Clause) ==='
    printf '\n'
    cat "$AOM_DIR/third_party/fastfeat/LICENSE"
    printf '\n'
    printf '%s\n' '=== vector - third_party/vector (MIT) ==='
    printf '\n'
    cat "$AOM_DIR/third_party/vector/LICENSE"
    printf '\n'
    printf '%s\n' '=== x86inc.asm - third_party/x86inc (ISC; x86-64 builds only) ==='
    printf '\n'
    cat "$AOM_DIR/third_party/x86inc/LICENSE"
}

survey() {
    local files unexpected webm_without_aom_terms
    files=$(cd "$AOM_DIR" && { find aom aom_dsp aom_mem aom_ports aom_scale aom_util av1 \
                                    third_party/fastfeat third_party/vector third_party/x86inc \
                                    -type f \( -name '*.c' -o -name '*.h' -o -name '*.asm' -o -name '*.S' -o -name '*.cc' -o -name '*.inc' \);
                               ls common/args_helper.c common/args_helper.h; } | sort)
    unexpected=$(cd "$AOM_DIR" && grep -H -i 'copyright' $files \
        | grep -v -E 'Alliance for Open Media|The WebM project authors|Edward Rosten|Peter Goldsborough|x264 project|above copyright|COPYRIGHT HOLDERS|COPYRIGHT OWNER|copyright notice and this permission notice' || true)
    if [ -n "$unexpected" ]; then
        echo "ERROR: a compiled libaom source names a copyright holder this licence file does not cover:" >&2
        echo "$unexpected" >&2
        echo "Review it and, if a notice must travel with the binary, compose it in here." >&2
        exit 1
    fi
    webm_without_aom_terms=$(cd "$AOM_DIR" && grep -l 'The WebM project authors' $files | xargs -r grep -L 'BSD 2 Clause License' || true)
    if [ -n "$webm_without_aom_terms" ]; then
        echo "ERROR: these WebM-project files do not carry libaom's BSD-2-Clause terms:" >&2
        echo "$webm_without_aom_terms" >&2
        exit 1
    fi
}

case "${1:-}" in
    "")      compose ;;
    --write) compose > "$TARGET"; echo "wrote $TARGET" ;;
    --check)
        survey
        tmp="$(mktemp)"; compose > "$tmp"
        if cmp -s "$tmp" "$TARGET"; then rm -f "$tmp"; echo "licenses/LICENSE-Aom.txt is current (compiled-source notice survey clean)"
        else rm -f "$tmp"; echo "ERROR: licenses/LICENSE-Aom.txt is stale or missing - run licenses/compose-license-aom.sh --write" >&2; exit 1; fi ;;
    *) echo "usage: $0 [--write|--check]" >&2; exit 2 ;;
esac
