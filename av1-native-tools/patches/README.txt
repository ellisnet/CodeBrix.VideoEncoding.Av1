av1-native-tools/patches/
================================================================================
EMPTY ON PURPOSE. As of 2026-10-10 libaom 3.15.2 is built exactly as released.

If a change to the libaom source is ever unavoidable, it goes here as a unified
diff named NNNN-aom-<what>.patch, made relative to av1-native-tools/ (so paths
start with aom/). Every build script copies aom/ and shim/ into a scratch
folder and applies every *.patch here with `patch -p1 --forward --batch` (in
name order; `git apply -p1` on Windows) to THAT copy; a patch that does not
apply fails the build. aom/ itself is never edited.
Record any patch in UPSTREAM.txt and BUILD-PROVENANCE.txt.

(A candidate exists but is NOT applied - see BUILD-PROVENANCE.txt, "Known
libaom behaviours": the extra key frame after a forced key frame in
GOOD_QUALITY usage. Applying it is a decision for the maintainer.)
