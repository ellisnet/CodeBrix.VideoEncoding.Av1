================================================================================
EXTRAS-README: CodeBrix.VideoEncoding.Av1
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

Exactly one project is packable - src/CodeBrix.VideoEncoding.Av1 - and
everything else listed below exists only to build, test or document it. None of
it is included in the CodeBrix.VideoEncoding.Av1.BsdLicenseForever package (the
package carries only the native BINARIES that av1-native-tools produces, under
runtimes/<rid>/native/).

For runnable, compilable usage of the library, read the test project: the
"WORKING EXAMPLES ON GITHUB" section of AGENT-README.txt maps each feature area
to the test file that exercises it.


AV1-NATIVE-TOOLS
================
    av1-native-tools/

Everything needed to build the native codebrix_av1 library for every runtime
identifier, from this repository alone:
  - aom/: the vendored, unmodified libaom release source tree
    (upstream-downloads/ holds the release tarball; UPSTREAM.txt records its
    identity and hash). It is built encoder-only; its tests, tools, examples
    and bundled googletest are never compiled;
  - nasm/: the vendored assembler source, built and used at BUILD TIME only
    for the x86-64 assembly; nothing from it is shipped;
  - shim/: codebrix_av1.h (the fixed-ABI contract with the managed assembly)
    and the C shim + CMake build that links a static libaom into one shared
    library;
  - licenses/: the generator that composes LICENSE-Aom.txt (identical for
    every RID);
  - the per-platform build scripts (Linux manylinux containers, Android NDK,
    Windows, macOS) and the smoke test;
  - BUILD-PROVENANCE.txt: one record per RID actually built.

Start with av1-native-tools/README.txt. MAINTAINER-README.txt (NATIVE
LIBRARIES) has the build matrix: which machine builds which RID.


TEST PROJECT
============
    tests/CodeBrix.VideoEncoding.Av1.Tests/

xUnit v3; run it as described in MAINTAINER-README.txt (TESTING). Every round
trip is decoded with the published CodeBrix.VideoPlayback.Dav1d package - an
AV1 decoder that shares no code with the encoder - and checked for frame count,
key-frame positions, layout, bit depth, colour tags, PSNR (lossy) or bit
exactness (lossless). IvfWriter.cs is a tiny IVF writer for dumping test
output to inspect with other tools: set CODEBRIX_AV1_TEST_DUMP_DIR to a folder
and the IVF test writes its stream there. It lives in the tests only - the
library knows nothing about containers.
