================================================================================
Av1Diagnostics - physical-device proof for CodeBrix.VideoEncoding.Av1 on Android
================================================================================

A one-screen .NET for Android app (Android 13 / API 33 and later, arm64-v8a and
x86_64). One second after launch - or when "Run AV1 diagnostics" is tapped -
it:

  1. calls CodeBrixVideoEncodingAv1.Register(), logs NativeLibraryVersion and
     NativeLibraryPath, and checks that VideoEncoders.Get("av1") returns
     CodeBrixVideoEncodingAv1.Factory;
  2. generates a 2-second clip in managed code (no assets): 60 frames of
     480x270 at 30 fps - a colour gradient with a soft-edged moving disc
     (I420, 10-bit, limited range) and the disc's alpha matte (Gray, 10-bit,
     full range) - and encodes both IN LOCK STEP with two Av1Encoders:
     colour at ConstantQuality(20) (cq-level 25), alpha LOSSLESS, both
     GoodQuality usage, AutomaticKeyFrames = false, the same forced key
     frames (every 30th frame) fed to both. It writes each stream as an IVF
     file plus its av1C record (.av1c) to the app's FilesDir and copies them
     to the app's external files folder so adb can pull them;
  3. reads both IVF files back and decodes them with the
     CodeBrix.VideoPlayback.Dav1d package: frame counts, decoded key-frame
     positions (identical between the two streams, every forced frame a key
     frame, the full sets reported), layout / bit depth / range, the colour
     stream's PSNR against the source (must be above 35 dB) and the alpha
     stream's bit-exactness against the source;
  4. decodes the colour IVF again with the device's own MediaCodec
     "video/av01" decoder (the av1C record as csd-0, ByteBuffer output),
     reporting the decoder's name, the frame count and the first picture's
     luma mean-absolute-difference against the source (and against dav1d's
     decode); if the device does not take the 10-bit stream it encodes the
     colour clip again at 8 bits and retries, and says so. A decoder that
     fails before producing any picture at both bit depths (an exception or
     a timeout - for example a device whose software-codec process crashes)
     is reported as "MediaCodec AV1 decode unavailable on this device" and
     the step is marked PASSED-WITH-NOTE rather than FAIL; a decoder that
     runs and produces the wrong frame count or picture is a FAIL;
  5. reports the encode wall time and frames per second of each stream;
  6. shows PASS / FAIL per step and ends with "AV1-DIAG RESULT: PASS" or
     "AV1-DIAG RESULT: FAIL: <reason>".

Every line is shown on screen and written to logcat with the tag Av1Diag.

By default every encode uses the encoder's own speed default
(Av1EncoderOptions.CpuUsed = -1). Launching with the integer extra cpuUsed
(see below) sets Av1EncoderOptions.CpuUsed for every encode instead; the
written files then carry a "-cpu<N>" suffix.


HOW THE NATIVES REACH THE APK
=============================
The sample references the library as SOURCE (a ProjectReference to
src/CodeBrix.VideoEncoding.Av1). A ProjectReference does NOT carry the
library's runtimes/<rid>/native/ files into an Android build: they are
pack-only items in the library csproj, so they reach the .nupkg but not a
referencing project's output. The sample csproj therefore names the two
Android natives itself as AndroidNativeLibrary items with Abi metadata
(arm64-v8a and x86_64), which packs libcodebrix_av1.so into lib/<abi>/ of the
APK. An application that consumes the PACKAGE needs none of this.

CodeBrix.VideoPlayback.Dav1d.BsdLicenseForever (from nuget.org) is the AV1
decoder of step 3; as a PackageReference it delivers its own Android natives
(libdav1d.so) automatically.


BUILD, INSTALL, RUN
===================
Needs the .NET 10 Android workload, an Android SDK with the API 36 platform and
a JDK the workload accepts (the csproj defaults AndroidSdkDirectory to
$HOME/Android/Sdk and JavaSdkDirectory to the Debian JDK 21 path when those
exist).

    dotnet build samples/Av1Diagnostics/Av1Diagnostics.csproj -c Release

One APK carries both ABIs:
    samples/Av1Diagnostics/bin/Release/net10.0-android36.1/
        com.codebrix.videoencoding.av1.diagnostics-Signed.apk

    adb -s <serial> install -r <that apk>
    adb -s <serial> logcat -c
    adb -s <serial> shell am start -n \
        com.codebrix.videoencoding.av1.diagnostics/crc641e6ec0c4b7d7831d.MainActivity
    (optionally add:  --ei cpuUsed 6)
    (wait until the RESULT line appears - from a few seconds to a few minutes,
     depending on the device and the speed setting)
    adb -s <serial> logcat -d -s Av1Diag

The encoded files (the Release build is not debuggable, so run-as cannot read
FilesDir; the app copies them out):

    adb -s <serial> pull \
        /sdcard/Android/data/com.codebrix.videoencoding.av1.diagnostics/files/ .

When finished:

    adb -s <serial> shell am force-stop com.codebrix.videoencoding.av1.diagnostics
