#!/usr/bin/env bash
# ==============================================================================================
# build-nasm.sh - builds the vendored nasm (BUILD-TIME TOOL ONLY, never shipped) for THIS machine
# ==============================================================================================
#
#   ./build-nasm.sh <bin-dir> [scratch-dir]
#
# libaom's x86 / x86_64 assembly needs an assembler; libaom prefers nasm (yasm is the lesser
# choice and is never used here). Neither this laptop nor the manylinux x86_64 image has nasm, so
# it is built from the unmodified release tree in nasm/ (UPSTREAM.txt) with its own plain
# configure + make and the machine's C compiler, into <bin-dir>/nasm. Used by:
#   linux/container-build.sh   inside the x86_64 container  -> /tmp/codebrix-av1-nasm/bin
#   build-host.sh, android/build.sh (x64)  on the Linux host -> output/host-tools/bin
# The arm64 / riscv64 Linux builds and android arm64 need no nasm (no x86 assembly there).
#
# The source is COPIED to the scratch folder and built there; nasm/ itself is never written to.
# A <bin-dir>/nasm that already reports the vendored version is reused. Reads only this repository;
# installs nothing; needs only cc + make (+ the shell tools configure uses).
# ==============================================================================================
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NASM_SRC="$TOOLS_DIR/nasm"
BIN_DIR="${1:?usage: build-nasm.sh <bin-dir> [scratch-dir]}"
SCRATCH="${2:-$TOOLS_DIR/build-scratch/nasm}"

[ -f "$NASM_SRC/configure" ] && [ -f "$NASM_SRC/version" ] \
    || { echo "ERROR: the vendored nasm tree ($NASM_SRC) is missing - restore it from git." >&2; exit 1; }
WANT="$(tr -d '[:space:]' < "$NASM_SRC/version")"

if [ -x "$BIN_DIR/nasm" ] && "$BIN_DIR/nasm" -v 2>/dev/null | grep -q "version $WANT "; then
    echo "  nasm      : $BIN_DIR/nasm ($("$BIN_DIR/nasm" -v)) - already built, reusing"
    exit 0
fi

for t in cc make; do
    command -v "$t" > /dev/null 2>&1 || { echo "ERROR: $t is not installed (this script installs nothing)." >&2; exit 1; }
done

echo "  nasm      : building $WANT from nasm/ (scratch $SCRATCH)"
rm -rf "$SCRATCH"
mkdir -p "$SCRATCH" "$BIN_DIR"
cp -a "$NASM_SRC/." "$SCRATCH/"
(
    cd "$SCRATCH"
    # Plain release build; no documentation (asciidoc / xmlto are not needed for the binary).
    CC=cc ./configure --disable-werror > configure.log 2>&1 \
        || { echo "ERROR: nasm configure failed:" >&2; tail -40 configure.log >&2; exit 1; }
    make -j "$(nproc 2>/dev/null || echo 4)" nasm > make.log 2>&1 \
        || { echo "ERROR: nasm make failed:" >&2; tail -40 make.log >&2; exit 1; }
)
cp "$SCRATCH/nasm" "$BIN_DIR/nasm"
chmod 0755 "$BIN_DIR/nasm"
rm -rf "$SCRATCH"
"$BIN_DIR/nasm" -v | grep -q "version $WANT " \
    || { echo "ERROR: the built nasm does not report version $WANT" >&2; exit 1; }
echo "  nasm      : $BIN_DIR/nasm ($("$BIN_DIR/nasm" -v))"
