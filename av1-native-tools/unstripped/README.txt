================================================================================
av1-native-tools/unstripped - the durable home for pre-strip binaries
================================================================================

UNLIKE output/, THIS FOLDER IS COMMITTED. It keeps the unstripped mate of every
codebrix_av1 binary that is shipped in the package, so crash dumps of a
stripped release binary can be symbolised after the build machine is gone.
Nothing here is shipped, and nothing here is an input to any build or pack step.

One folder per runtime identifier, mirroring
../../src/CodeBrix.VideoEncoding.Av1/runtimes/<rid>/native/:

  <rid>/libcodebrix_av1.so      pre-strip ELF with DWARF (Linux, Android). The
                                DWARF sections are zlib-COMPRESSED (-gz): libaom's
                                full debug info is ~10x its code (90 MB uncompressed
                                for linux-x64). gdb, lldb, addr2line, eu-stack and
                                llvm-symbolizer read compressed sections directly.
  <rid>/libcodebrix_av1.dylib   pre-strip Mach-O (macOS; the .dSYM stays in output/)
  <rid>/codebrix_av1.pdb        the PDB of the shipped DLL (Windows)

The build scripts copy the twin here automatically when they adopt a binary
(ADOPT=1 / no -NoAdopt). Refresh SHA256SUMS after adding one:

    sha256sum */libcodebrix_av1.so */*.dylib */*.pdb > SHA256SUMS   # (whichever exist)

Verify a twin: its sha256 is the "SHA256 unstripped" (or "PDB") line of that
RID in ../BUILD-PROVENANCE.txt, and its build-id equals the shipped file's:

    readelf -n <rid>/libcodebrix_av1.so | grep 'Build ID'
    readelf -n ../../src/CodeBrix.VideoEncoding.Av1/runtimes/<rid>/native/libcodebrix_av1.so | grep 'Build ID'

For Windows the link is the PDB's GUID + age: the DLL's CodeView (RSDS) record
names "codebrix_av1.pdb" with a GUID, and the .pdb carries the same GUID.
From a developer prompt:

    dumpbin /headers ../../src/CodeBrix.VideoEncoding.Av1/runtimes/<rid>/native/codebrix_av1.dll | findstr RSDS

A debugger (WinDbg, Visual Studio) given this folder as a symbol path matches
the two by that GUID; nothing needs renaming.

STORED SO FAR: see ../BUILD-PROVENANCE.txt (one block per RID, build-id and
sha256 of each twin).
