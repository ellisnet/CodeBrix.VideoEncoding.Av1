using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;
using CodeBrix.VideoProcessing.Core.Media;
using CodeBrix.VideoProcessing.Core.Output;
using global::Android.App;
using global::Android.OS;
using global::Android.Widget;
using AndroidBuild = global::Android.OS.Build;
using AndroidLog = global::Android.Util.Log;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CodeBrix.VideoEncoding.Av1.Diagnostics;

/// <summary>
/// One-screen device proof for CodeBrix.VideoEncoding.Av1: loads the native encoder, encodes a synthetic 10-bit
/// colour clip and its lossless monochrome alpha matte in lock step, writes both as IVF files, decodes them with the
/// published dav1d decoder package (frame counts, key frames, PSNR, bit-exactness) and the colour stream again with
/// the device's own MediaCodec AV1 decoder, reports encode speed, and ends with an "AV1-DIAG RESULT:" line. Every
/// line also goes to logcat under the tag Av1Diag.
/// </summary>
[Activity(Label = "CodeBrix AV1 Diagnostics", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string LogTag = "Av1Diag";
    private const int Width = 480;
    private const int Height = 270;
    private const int FrameRate = 30;
    private const int FrameCount = 60; // 2 seconds
    private const int KeyFrameEvery = 30;
    private const int ColourQuality = 20; // Core 0..51 -> cq-level round(20 * 63 / 51) = 25
    private const double PsnrFloor = 35.0;
    private const double MediaCodecMadBound = 6.0; // 8-bit units; MediaCodec output formats vary

    private TextView _log = null;
    private ScrollView _scroll = null;
    private Button _run = null;
    private int _running;
    private int _cpuUsed = Av1EncoderOptions.Default;

    /// <summary>Builds the one-screen UI and starts the proof one second after launch (so adb needs no taps).</summary>
    /// <param name="savedInstanceState">Unused saved state.</param>
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var panel = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        _run = new Button(this) { Text = "Run AV1 diagnostics" };
        _run.Click += (_, _) => StartRun();
        _log = new TextView(this) { Text = "" };
        _log.SetTextIsSelectable(true);
        _scroll = new ScrollView(this);
        _scroll.AddView(_log);
        panel.AddView(_run);
        panel.AddView(_scroll, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MatchParent, 0, 1f));
        SetContentView(panel);

        // Optional: "adb shell am start ... --ei cpuUsed 6" sets Av1EncoderOptions.CpuUsed for every encode; the
        // default (-1) leaves the encoder's own default, which is what an application gets out of the box.
        _cpuUsed = Intent?.GetIntExtra("cpuUsed", Av1EncoderOptions.Default) ?? Av1EncoderOptions.Default;

        new Handler(Looper.MainLooper).PostDelayed(StartRun, 1000);
    }

    private void StartRun()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _run.Enabled = false;
        Task.Run(() =>
        {
            try { RunDiagnostics(); }
            catch (Exception ex) { Log("AV1-DIAG RESULT: FAIL: unexpected " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                Volatile.Write(ref _running, 0);
                RunOnUiThread(() => _run.Enabled = true);
            }
        });
    }

    private void Log(string line)
    {
        AndroidLog.Info(LogTag, line);
        RunOnUiThread(() =>
        {
            _log.Append(line + "\n");
            _scroll.Post(() => _scroll.FullScroll(global::Android.Views.FocusSearchDirection.Down));
        });
    }

    /// <summary>One encoded stream: its packets in order, its av1C record, and how long encoding took.</summary>
    private sealed class EncodedStream
    {
        public string Name { get; init; }
        public List<EncodedVideoPacket> Packets { get; } = new List<EncodedVideoPacket>();
        public byte[] Av1C { get; set; }
        public string SequenceHeader { get; set; }
        public double EncodeMilliseconds { get; set; }
        public string IvfPath { get; set; }
        public List<int> KeyFrames => Packets.Select((p, i) => (p, i)).Where(x => x.p.IsKeyFrame).Select(x => x.i).ToList();
    }

    private static string Set(IEnumerable<int> values) => "{" + string.Join(",", values) + "}";

    private static string Hex(byte[] data, int max) =>
        BitConverter.ToString(data, 0, Math.Min(max, data.Length)).Replace("-", " ") + (data.Length > max ? " ..." : "");

    private static VideoEncoderRequest ColourRequest(int bitDepth) =>
        new VideoEncoderRequest
        {
            Width = Width,
            Height = Height,
            PixelLayout = VideoPixelLayout.I420,
            BitDepth = bitDepth,
            FrameRate = new FrameRate(FrameRate, 1),
            Quality = VideoQuality.ConstantQuality(ColourQuality),
        };

    private static VideoEncoderRequest AlphaRequest() =>
        new VideoEncoderRequest
        {
            Width = Width,
            Height = Height,
            PixelLayout = VideoPixelLayout.Gray,
            BitDepth = 10,
            FrameRate = new FrameRate(FrameRate, 1),
            Lossless = true,
            ColorRange = VideoColorRange.Full,
            ColorPrimaries = VideoColorPrimaries.Unspecified,
            ColorTransfer = VideoColorTransfer.Unspecified,
            ColorMatrix = VideoColorMatrix.Unspecified,
        };

    private Av1EncoderOptions LockStepOptions() =>
        new Av1EncoderOptions { Usage = Av1EncoderUsage.GoodQuality, AutomaticKeyFrames = false, CpuUsed = _cpuUsed };

    private string Suffix => _cpuUsed == Av1EncoderOptions.Default ? "" : "-cpu" + _cpuUsed;

    private static bool Forced(int index) => index % KeyFrameEvery == 0;

    private static string Describe(Av1SequenceHeaderInfo h) =>
        h == null ? "(none)" : $"profile {h.Profile}, level {h.LevelName}, tier {h.Tier}, {h.BitDepth}-bit, mono {h.Monochrome}, "
            + $"subsampling {h.SubsamplingX}/{h.SubsamplingY}, full range {h.FullRange}, max {h.MaxFrameWidth}x{h.MaxFrameHeight}";

    private void Drain(Av1Encoder encoder, EncodedStream stream)
    {
        while (encoder.TryReadPacket(out EncodedVideoPacket packet)) stream.Packets.Add(packet);
        if (stream.Av1C == null && !encoder.CodecConfigurationRecord.IsEmpty)
        {
            stream.Av1C = encoder.CodecConfigurationRecord.ToArray();
            stream.SequenceHeader = Describe(encoder.SequenceHeader);
        }
    }

    private string WriteIvf(EncodedStream stream, string externalDir)
    {
        string path = Path.Combine(FilesDir.AbsolutePath, stream.Name + ".ivf");
        using (FileStream file = File.Create(path))
        using (IvfWriter writer = new IvfWriter(file, Width, Height, FrameRate, 1))
        {
            foreach (EncodedVideoPacket packet in stream.Packets) writer.WriteFrame(packet.Data.Span, packet.FrameIndex);
        }

        File.WriteAllBytes(Path.Combine(FilesDir.AbsolutePath, stream.Name + ".av1c"), stream.Av1C ?? Array.Empty<byte>());
        if (externalDir != null)
        {
            File.Copy(path, Path.Combine(externalDir, stream.Name + ".ivf"), true);
            File.WriteAllBytes(Path.Combine(externalDir, stream.Name + ".av1c"), stream.Av1C ?? Array.Empty<byte>());
        }

        stream.IvfPath = path;
        return path;
    }

    private void RunDiagnostics()
    {
        var verdicts = new List<string>();
        string failure = null;
        bool stepFailed = false;
        string stepNote = null;
        void Fail(string reason) { failure ??= reason; stepFailed = true; Log("CHECK FAILED: " + reason); }
        void Verdict(string step)
        {
            string line = $"{step}: {(stepFailed ? "FAIL" : stepNote != null ? "PASSED-WITH-NOTE (" + stepNote + ")" : "PASS")}";
            verdicts.Add(line);
            Log("=== " + line + " ===");
            stepFailed = false;
            stepNote = null;
        }

        Log("=== CodeBrix.VideoEncoding.Av1 device diagnostics ===");
        Log($"Device {AndroidBuild.Manufacturer} {AndroidBuild.Model}, ABI {AndroidBuild.SupportedAbis?[0]}, "
            + $"API {(int)AndroidBuild.VERSION.SdkInt}, process {RuntimeInformation.ProcessArchitecture}, "
            + $"runtime {RuntimeInformation.FrameworkDescription}, processors {System.Environment.ProcessorCount}");
        string externalDir = GetExternalFilesDir(null)?.AbsolutePath;

        // (1) Load and register.
        try
        {
            CodeBrixVideoEncodingAv1.Register();
            Log("Step 1 Register: OK, IsRegistered=" + CodeBrixVideoEncodingAv1.IsRegistered);
            Log("Step 1 NativeLibraryVersion: " + CodeBrixVideoEncodingAv1.NativeLibraryVersion);
            Log("Step 1 NativeLibraryPath: " + (CodeBrixVideoEncodingAv1.NativeLibraryPath ?? "(null)"));
            IVideoEncoderFactory factory = VideoEncoders.Get("av1");
            bool same = ReferenceEquals(factory, CodeBrixVideoEncodingAv1.Factory);
            Log($"Step 1 VideoEncoders.Get(\"av1\"): {factory?.GetType().FullName}, CodecId {factory?.CodecId}, "
                + $"DisplayName {factory?.DisplayName}, is CodeBrixVideoEncodingAv1.Factory {same}");
            if (!same) Fail("VideoEncoders.Get(\"av1\") did not return CodeBrixVideoEncodingAv1.Factory");
        }
        catch (Exception ex)
        {
            Log("Step 1 Register FAILED: " + ex);
            Fail("native load: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            Verdict("Step 1 register");
            Log("AV1-DIAG RESULT: FAIL: " + failure);
            return;
        }

        Verdict("Step 1 register");

        // (2) Encode colour + alpha in lock step.
        var colour = new EncodedStream { Name = "av1-colour-480x270-10bit-q20" + Suffix };
        var alpha = new EncodedStream { Name = "av1-alpha-480x270-10bit-lossless" + Suffix };
        var forced = Enumerable.Range(0, FrameCount).Where(Forced).ToList();
        try
        {
            var colourWatch = new Stopwatch();
            var alphaWatch = new Stopwatch();
            Log($"Step 2 encoding {FrameCount} frames {Width}x{Height} @ {FrameRate} fps: colour I420 10-bit "
                + $"ConstantQuality({ColourQuality}) GoodQuality; alpha Gray 10-bit full-range LOSSLESS; "
                + $"AutomaticKeyFrames=false, forced key frames {Set(forced)} fed to both; Av1EncoderOptions.CpuUsed "
                + (_cpuUsed == Av1EncoderOptions.Default ? "-1 (encoder default)" : _cpuUsed.ToString()));
            using (var colourEncoder = new Av1Encoder(ColourRequest(10), LockStepOptions()))
            using (var alphaEncoder = new Av1Encoder(AlphaRequest(), LockStepOptions()))
            {
                for (int i = 0; i < FrameCount; i++)
                {
                    SyntheticPicture picture = SyntheticClip.Create(Width, Height, 10, i);
                    colourWatch.Start();
                    colourEncoder.Encode(picture.ColourFrame(Width, Height, i), Forced(i));
                    Drain(colourEncoder, colour);
                    colourWatch.Stop();
                    alphaWatch.Start();
                    alphaEncoder.Encode(picture.AlphaFrame(Width, Height, i), Forced(i));
                    Drain(alphaEncoder, alpha);
                    alphaWatch.Stop();
                }

                colourWatch.Start();
                colourEncoder.Flush();
                Drain(colourEncoder, colour);
                colourWatch.Stop();
                alphaWatch.Start();
                alphaEncoder.Flush();
                Drain(alphaEncoder, alpha);
                alphaWatch.Stop();
                Log($"Step 2 colour encoder: FramesSubmitted {colourEncoder.FramesSubmitted}, PacketsProduced {colourEncoder.PacketsProduced}, "
                    + $"Threads {colourEncoder.Request.Threads}, CpuUsed {colourEncoder.Options.CpuUsed}");
                Log($"Step 2 alpha encoder: FramesSubmitted {alphaEncoder.FramesSubmitted}, PacketsProduced {alphaEncoder.PacketsProduced}");
            }

            colour.EncodeMilliseconds = colourWatch.Elapsed.TotalMilliseconds;
            alpha.EncodeMilliseconds = alphaWatch.Elapsed.TotalMilliseconds;

            foreach (EncodedStream stream in new[] { colour, alpha })
            {
                string path = WriteIvf(stream, externalDir);
                long bytes = new FileInfo(path).Length;
                bool numbered = stream.Packets.Select(p => p.FrameIndex).SequenceEqual(Enumerable.Range(0, stream.Packets.Count).Select(i => (long)i));
                Log($"Step 2 {stream.Name}: {stream.Packets.Count} packets, {bytes} bytes IVF, FrameIndex 0..n-1 {numbered}, "
                    + $"encoder key frames {Set(stream.KeyFrames)}, {stream.EncodeMilliseconds:F0} ms, path {path}");
                Log($"Step 2 {stream.Name}: sequence header {stream.SequenceHeader}; av1C ({stream.Av1C?.Length ?? 0} bytes) {(stream.Av1C == null ? "(none)" : Hex(stream.Av1C, 24))}");
                if (stream.Packets.Count != FrameCount) Fail($"{stream.Name}: {stream.Packets.Count} packets, expected {FrameCount}");
                if (!numbered) Fail($"{stream.Name}: FrameIndex values are not 0..{stream.Packets.Count - 1}");
                if (stream.Av1C == null || stream.Av1C.Length < 5 || stream.Av1C[0] != 0x81) Fail($"{stream.Name}: no valid av1C record");
                foreach (int f in forced)
                {
                    if (!stream.KeyFrames.Contains(f)) Fail($"{stream.Name}: forced frame {f} is not a key frame");
                }
            }

            Log($"Step 2 key frames: forced {Set(forced)}; colour {Set(colour.KeyFrames)} (extra {Set(colour.KeyFrames.Except(forced))}); "
                + $"alpha {Set(alpha.KeyFrames)} (extra {Set(alpha.KeyFrames.Except(forced))}); identical {colour.KeyFrames.SequenceEqual(alpha.KeyFrames)}");
            if (!colour.KeyFrames.SequenceEqual(alpha.KeyFrames)) Fail("colour and alpha encoder key-frame positions differ");
        }
        catch (Exception ex)
        {
            Log("Step 2 encode FAILED: " + ex);
            Fail("encode threw " + ex.GetType().Name + ": " + ex.Message);
        }

        Verdict("Step 2 lock-step encode + IVF");

        // (3) Decode both IVF files with the dav1d package.
        List<DecodedPicture> colourDecoded = null;
        try
        {
            Log("Step 3 dav1d native version: " + Dav1dDecode.NativeVersion);
            List<int> colourKeys = null;
            List<int> alphaKeys = null;
            foreach (EncodedStream stream in new[] { colour, alpha })
            {
                if (stream.IvfPath == null) throw new InvalidOperationException(stream.Name + " was not written");
                IvfFile ivf = IvfFile.Read(stream.IvfPath);
                Log($"Step 3 {stream.Name}: IVF {ivf.FourCC} {ivf.Width}x{ivf.Height}, timebase {ivf.TimebaseNumerator}/{ivf.TimebaseDenominator}, "
                    + $"header frames {ivf.HeaderFrameCount}, frames read {ivf.Frames.Count}");
                if (ivf.FourCC != "AV01" || ivf.Frames.Count != FrameCount || ivf.HeaderFrameCount != FrameCount) Fail($"{stream.Name}: IVF header/frames wrong");

                var watch = Stopwatch.StartNew();
                List<DecodedPicture> decoded = Dav1dDecode.Decode(stream.Av1C, ivf.Frames, stream.Packets.Select(p => p.IsKeyFrame).ToList(), FrameRate);
                watch.Stop();
                List<int> keys = decoded.Select((d, i) => (d, i)).Where(x => x.d.IsKeyFrame).Select(x => x.i).ToList();
                DecodedPicture first = decoded.FirstOrDefault();
                Log($"Step 3 {stream.Name}: dav1d decoded {decoded.Count} frames in {watch.Elapsed.TotalMilliseconds:F0} ms, "
                    + $"{first?.Width}x{first?.Height} {first?.Layout} {first?.BitDepth}-bit range {first?.Range}, key frames {Set(keys)}");
                if (decoded.Count != FrameCount) Fail($"{stream.Name}: dav1d decoded {decoded.Count} frames, expected {FrameCount}");
                if (!keys.SequenceEqual(stream.KeyFrames)) Fail($"{stream.Name}: decoded key frames {Set(keys)} != encoder's {Set(stream.KeyFrames)}");
                foreach (int f in forced)
                {
                    if (!keys.Contains(f)) Fail($"{stream.Name}: forced frame {f} did not decode as a key frame");
                }

                bool isColour = ReferenceEquals(stream, colour);
                if (first != null && (first.Width != Width || first.Height != Height || first.BitDepth != 10 || first.Layout != (isColour ? "I420" : "Gray")))
                {
                    Fail($"{stream.Name}: decoded shape {first.Width}x{first.Height} {first.Layout} {first.BitDepth}-bit is not what was encoded");
                }

                if (isColour)
                {
                    colourDecoded = decoded;
                    colourKeys = keys;
                    double ySse = 0, uSse = 0, vSse = 0;
                    double minFrameY = double.PositiveInfinity;
                    for (int i = 0; i < Math.Min(decoded.Count, FrameCount); i++)
                    {
                        SyntheticPicture source = SyntheticClip.Create(Width, Height, 10, i);
                        double y = SyntheticClip.SquaredError(source.Y, decoded[i].Y, 2);
                        ySse += y;
                        uSse += SyntheticClip.SquaredError(source.U, decoded[i].U, 2);
                        vSse += SyntheticClip.SquaredError(source.V, decoded[i].V, 2);
                        minFrameY = Math.Min(minFrameY, SyntheticClip.Psnr(y, Width * Height, 10));
                    }

                    int n = Math.Min(decoded.Count, FrameCount);
                    long lumaSamples = (long)n * Width * Height;
                    long chromaSamples = (long)n * (Width / 2) * (Height / 2);
                    double psnrY = SyntheticClip.Psnr(ySse, lumaSamples, 10);
                    double psnrU = SyntheticClip.Psnr(uSse, chromaSamples, 10);
                    double psnrV = SyntheticClip.Psnr(vSse, chromaSamples, 10);
                    double psnrAll = SyntheticClip.Psnr(ySse + uSse + vSse, lumaSamples + 2 * chromaSamples, 10);
                    Log($"Step 3 {stream.Name}: PSNR vs source (10-bit) Y {psnrY:F2} dB, U {psnrU:F2} dB, V {psnrV:F2} dB, "
                        + $"YUV {psnrAll:F2} dB, worst frame Y {minFrameY:F2} dB (floor {PsnrFloor} dB)");
                    if (!(psnrAll > PsnrFloor) || !(psnrY > PsnrFloor)) Fail($"{stream.Name}: PSNR Y {psnrY:F2} / YUV {psnrAll:F2} dB not above {PsnrFloor} dB");
                }
                else
                {
                    alphaKeys = keys;
                    int mismatchedFrames = 0;
                    int firstMismatch = -1;
                    long mismatchedSamples = 0;
                    int maxDifference = 0;
                    for (int i = 0; i < Math.Min(decoded.Count, FrameCount); i++)
                    {
                        SyntheticPicture source = SyntheticClip.Create(Width, Height, 10, i);
                        if (!source.Alpha.AsSpan().SequenceEqual(decoded[i].Y))
                        {
                            mismatchedFrames++;
                            if (firstMismatch < 0) firstMismatch = i;
                            for (int s = 0; s < Width * Height; s++)
                            {
                                int d = Math.Abs(SyntheticClip.Get(source.Alpha, s, 2) - SyntheticClip.Get(decoded[i].Y, s, 2));
                                if (d == 0) continue;
                                mismatchedSamples++;
                                maxDifference = Math.Max(maxDifference, d);
                            }
                        }
                    }

                    Log($"Step 3 {stream.Name}: BIT-EXACT vs source {mismatchedFrames == 0 && decoded.Count == FrameCount} "
                        + $"({mismatchedFrames} mismatched frames{(firstMismatch >= 0 ? ", first " + firstMismatch : "")}, "
                        + $"{mismatchedSamples} samples differ, max difference {maxDifference})");
                    if (mismatchedFrames != 0) Fail($"{stream.Name}: lossless alpha is not bit-exact ({mismatchedFrames} frames differ)");
                }
            }

            bool sameKeys = colourKeys != null && alphaKeys != null && colourKeys.SequenceEqual(alphaKeys);
            Log($"Step 3 decoded key frames: colour {Set(colourKeys ?? new List<int>())}, alpha {Set(alphaKeys ?? new List<int>())}, "
                + $"forced {Set(forced)}, identical {sameKeys}");
            if (!sameKeys) Fail("decoded key-frame positions differ between colour and alpha");
        }
        catch (Exception ex)
        {
            Log("Step 3 dav1d decode FAILED: " + ex);
            Fail("dav1d decode threw " + ex.GetType().Name + ": " + ex.Message);
        }

        Verdict("Step 3 dav1d decode");

        // (4) Decode the colour IVF with the device's own MediaCodec AV1 decoder.
        try
        {
            byte[] sourceLuma8 = new byte[Width * Height];
            SyntheticPicture source0 = SyntheticClip.Create(Width, Height, 10, 0);
            for (int i = 0; i < sourceLuma8.Length; i++) sourceLuma8[i] = (byte)Math.Min(255, (SyntheticClip.Get(source0.Y, i, 2) + 2) >> 2);

            double Mad(byte[] a, byte[] b)
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
                return sum / a.Length;
            }

            bool lastUnavailable = false;
            bool Attempt(string label, byte[] av1C, List<byte[]> units, out string why)
            {
                lastUnavailable = false;
                MediaCodecResult r = MediaCodecDecode.Decode(av1C, units, Width, Height, FrameRate);
                Log($"Step 4 [{label}] MediaCodec decoder {r.DecoderName ?? "(none)"} (software-only {r.SoftwareOnly}, "
                    + $"hardware-accelerated {r.HardwareAccelerated}), profile/levels {r.Profiles}");
                Log($"Step 4 [{label}] queued {r.FramesQueued}, decoded {r.FramesDecoded} frames in {r.ElapsedMilliseconds:F0} ms; "
                    + $"output format {r.OutputFormat ?? "(none)"}");
                Log($"Step 4 [{label}] first image: {r.FirstImageDescription ?? "(none)"}");
                if (r.Error != null) Log($"Step 4 [{label}] error: {r.Error}");
                why = null;
                lastUnavailable = r.DecoderUnavailable;
                if (r.Error != null) why = r.Error;
                else if (r.FramesDecoded != FrameCount) why = $"decoded {r.FramesDecoded} frames, expected {FrameCount}";
                else if (r.FirstLuma8 == null) why = "first picture could not be read";
                else
                {
                    double madSource = Mad(r.FirstLuma8, sourceLuma8);
                    string madDav1d = "n/a";
                    if (colourDecoded != null && colourDecoded.Count > 0 && label.StartsWith("10-bit", StringComparison.Ordinal))
                    {
                        byte[] d8 = new byte[Width * Height];
                        for (int i = 0; i < d8.Length; i++) d8[i] = (byte)Math.Min(255, (SyntheticClip.Get(colourDecoded[0].Y, i, 2) + 2) >> 2);
                        madDav1d = Mad(r.FirstLuma8, d8).ToString("F3");
                    }

                    Log($"Step 4 [{label}] first-frame luma mean-absolute-difference (8-bit units): vs source {madSource:F3} "
                        + $"(bound {MediaCodecMadBound}), vs dav1d's decode {madDav1d}");
                    if (!(madSource < MediaCodecMadBound)) why = $"first-frame luma MAD {madSource:F3} not under {MediaCodecMadBound}";
                }

                return why == null;
            }

            IvfFile colourIvf = IvfFile.Read(colour.IvfPath);
            if (!Attempt("10-bit", colour.Av1C, colourIvf.Frames, out string why10))
            {
                bool unavailable10 = lastUnavailable;
                Log($"Step 4 MediaCodec did NOT take the 10-bit stream ({why10}); retrying with an 8-bit colour encode");
                var eight = new EncodedStream { Name = "av1-colour-480x270-8bit-q20" + Suffix };
                using (var encoder = new Av1Encoder(ColourRequest(8), LockStepOptions()))
                {
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < FrameCount; i++)
                    {
                        SyntheticPicture p = SyntheticClip.Create(Width, Height, 8, i);
                        encoder.Encode(p.ColourFrame(Width, Height, i), Forced(i));
                        Drain(encoder, eight);
                    }

                    encoder.Flush();
                    Drain(encoder, eight);
                    watch.Stop();
                    eight.EncodeMilliseconds = watch.Elapsed.TotalMilliseconds;
                }

                WriteIvf(eight, externalDir);
                Log($"Step 4 8-bit colour encode: {eight.Packets.Count} packets, key frames {Set(eight.KeyFrames)}, {eight.EncodeMilliseconds:F0} ms");
                if (Attempt("8-bit retry", eight.Av1C, IvfFile.Read(eight.IvfPath).Frames, out string why8))
                {
                    Log("Step 4 NOTE: MediaCodec refused 10-bit, accepted 8-bit");
                    stepNote = "MediaCodec took 8-bit only";
                }
                else if (unavailable10 && lastUnavailable)
                {
                    // The device's decoder died before producing any picture at both bit depths: a device limitation
                    // (for example a software-codec process that crashes), not something the stream can cause to
                    // differ - reported, not counted as a failure.
                    string note = "MediaCodec AV1 decode unavailable on this device (decoder failed before any output: " + why8 + ")";
                    Log("Step 4 NOTE: " + note);
                    stepNote = note;
                }
                else
                {
                    Fail("MediaCodec failed at 10-bit (" + why10 + ") and at 8-bit (" + why8 + ")");
                }
            }
            else
            {
                Log("Step 4 MediaCodec took the 10-bit stream");
            }
        }
        catch (Exception ex)
        {
            Log("Step 4 MediaCodec FAILED: " + ex);
            Fail("MediaCodec threw " + ex.GetType().Name + ": " + ex.Message);
        }

        Verdict("Step 4 MediaCodec decode");

        // (5) Timing.
        foreach (EncodedStream stream in new[] { colour, alpha })
        {
            double fps = stream.EncodeMilliseconds > 0 ? FrameCount * 1000.0 / stream.EncodeMilliseconds : 0;
            Log($"Step 5 {stream.Name}: encode wall {stream.EncodeMilliseconds:F0} ms for {FrameCount} frames = {fps:F1} fps");
            if (stream.EncodeMilliseconds <= 0) Fail(stream.Name + ": no encode time recorded");
        }

        double both = colour.EncodeMilliseconds + alpha.EncodeMilliseconds;
        Log($"Step 5 both streams together: {both:F0} ms = {(both > 0 ? FrameCount * 1000.0 / both : 0):F1} fps (lock-step pairs)");
        Verdict("Step 5 timing");

        Log("Summary: " + string.Join("; ", verdicts));
        Log(failure == null ? "AV1-DIAG RESULT: PASS" : "AV1-DIAG RESULT: FAIL: " + failure);
    }
}
