#!/usr/bin/env bash
# ==============================================================================================
# build-host.sh - a quick, NOT-SHIPPED build of libcodebrix_av1.so with THIS machine's tools
# ==============================================================================================
#
#   ./build-host.sh
#
# Builds shim/ (+ libaom from the unmodified aom/ tree, through libaom's own CMake) with the host's
# own cmake, C compiler and Perl into output/host/libcodebrix_av1.so and runs the smoke test
# (codebrix_av1_smoke, built by the same CMake) against it. It exists so the managed tests can run
# on a Linux dev box before (or without) a container build. On an x86_64 host the vendored nasm is
# built first (build-nasm.sh -> output/host-tools/bin/nasm, reused afterwards) and put first on
# PATH for libaom's assembly.
#
# The result is NEVER copied into src/.../runtimes: its glibc floor is whatever this machine has.
# Shipped Linux binaries come only from linux/build.sh (manylinux containers, --network none).
# Reads only this repository; installs nothing.
# ==============================================================================================
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRATCH="$TOOLS_DIR/build-scratch/host"
OUT="$TOOLS_DIR/output/host"

for t in cmake cc make perl; do
    command -v "$t" > /dev/null 2>&1 || { echo "ERROR: $t is not installed (this script installs nothing)." >&2; exit 1; }
done

case "$(uname -m)" in
    x86_64|amd64|i?86)
        bash "$TOOLS_DIR/build-nasm.sh" "$TOOLS_DIR/output/host-tools/bin"
        export PATH="$TOOLS_DIR/output/host-tools/bin:$PATH" ;;
esac

rm -rf "$SCRATCH"
mkdir -p "$SCRATCH" "$OUT"
cmake -S "$TOOLS_DIR/shim" -B "$SCRATCH" -DCMAKE_BUILD_TYPE=Release -DCBA_BUILD_SMOKE_TEST=ON > "$SCRATCH/configure.txt"
cmake --build "$SCRATCH" -j "$(nproc)" --target codebrix_av1 codebrix_av1_smoke
cp "$SCRATCH/libcodebrix_av1.so" "$OUT/libcodebrix_av1.so.new"
mv -f "$OUT/libcodebrix_av1.so.new" "$OUT/libcodebrix_av1.so"   # atomic for anyone polling the file
bash "$TOOLS_DIR/licenses/compose-license-aom.sh" --check
cp "$TOOLS_DIR/licenses/LICENSE-Aom.txt" "$OUT/LICENSE-Aom.txt"
cp "$SCRATCH/codebrix_av1_smoke" "$OUT/smoke-test"
"$OUT/smoke-test" "$OUT/libcodebrix_av1.so" "$OUT/smoke"
rm -rf "$SCRATCH"
echo
echo "host build: $OUT/libcodebrix_av1.so ($(stat -c %s "$OUT/libcodebrix_av1.so") bytes) - NOT for shipping"
