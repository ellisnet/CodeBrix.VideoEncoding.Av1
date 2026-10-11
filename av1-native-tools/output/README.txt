av1-native-tools/output/ - build results (NOT committed, except this README)
================================================================================
Every build script writes here, one folder per RID:

  output/<rid>/<library>          the stripped library, exactly as the package ships it
  output/<rid>/LICENSE-Aom.txt    licenses/LICENSE-Aom.txt (libaom LICENSE + PATENTS +
                                  the fastfeat / vector / x86inc notices)
  output/<rid>/unstripped/        the pre-strip twin (Linux / Android / macOS)
  output/<rid>/BUILD-INFO.txt     toolchain, flags, libaom configuration (SIMD flavours,
                                  assembler, third_party), sizes, sha256, gate results,
                                  the smoke test's output
  output/<rid>/smoke-test*        the gate's smoke-test program (codebrix_av1_smoke, built
                                  for the TARGET by the same CMake run; on Windows also kept
                                  so a cross-built win-arm64 can finish its gate on a device)

  output/host/libcodebrix_av1.so  a plain build with this machine's own cmake + cc, for
                                  running the managed tests on a Linux dev box. NEVER shipped
                                  (its glibc floor is whatever the dev box has).
                                  Rebuild:  ./build-host.sh
  output/host-tools/bin/nasm      the vendored nasm built for this host (build-nasm.sh);
                                  used by build-host.sh and android/build.sh (x64). Never
                                  shipped.
  output/scratch-*/               host-only diagnostic builds and fixtures (e.g. an aomenc
                                  built from aom/ to cross-check libaom itself). Never
                                  shipped, never an input to any build.

See ../README.txt.
