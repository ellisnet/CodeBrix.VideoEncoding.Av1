using System;
using CodeBrix.VideoEncoding.Av1.Interop;
using CodeBrix.VideoProcessing.Core.Encoding;

namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// The one call that makes AV1 encoding available: <c>CodeBrixVideoEncodingAv1.Register();</c>
/// </summary>
/// <remarks>
/// <para>
/// Call it once, early. It loads and checks the native encoder for the current platform - so a packaging problem
/// surfaces at start-up rather than at the first encode - and registers <see cref="Factory" /> with the
/// CodeBrix.VideoProcessing.Core encoder registry, so <c>VideoEncoders.Get(VideoCodecIds.Av1)</c> finds it.
/// </para>
/// <para>
/// <see cref="Av1Encoder" /> works without this call; it loads the native on first use.
/// </para>
/// <para>
/// There is deliberately NO module initializer doing this for you: a module initializer only runs once something
/// in the assembly is touched, which trimming and lazy assembly loading make unreliable. An explicit call is the
/// contract.
/// </para>
/// </remarks>
public static class CodeBrixVideoEncodingAv1
{
    private static readonly object Gate = new object();
    private static readonly Av1EncoderFactory SharedFactory = new Av1EncoderFactory();

    private static bool registered;

    /// <summary>
    /// Loads the native encoder and registers <see cref="Factory" /> under "av1" with <see cref="VideoEncoders" />.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe to call from any thread; calling it more than once does nothing. To encode with other
    /// default AV1 options, register an <see cref="Av1EncoderFactory" /> of your own AFTERWARDS - the registry
    /// returns the most recently registered factory for a codec.
    /// </remarks>
    /// <exception cref="Av1EncoderException">
    /// The native encoder for this platform could not be loaded; the message names the runtime identifier and every
    /// path that was tried.
    /// </exception>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) return;

            Av1Library.EnsureLoaded();
            VideoEncoders.Register(SharedFactory);

            registered = true;
        }
    }

    /// <summary>Whether <see cref="Register" /> has run.</summary>
    public static bool IsRegistered
    {
        get { lock (Gate) { return registered; } }
    }

    /// <summary>The single factory instance <see cref="Register" /> registers.</summary>
    public static Av1EncoderFactory Factory => SharedFactory;

    /// <summary>
    /// Loads the native encoder for the current platform if it is not loaded already, and checks that it speaks the
    /// contract this package was built against.
    /// </summary>
    /// <exception cref="Av1EncoderException">
    /// The native library could not be loaded - the message names the runtime identifier and lists every path that
    /// was tried - or it reports a different contract version.
    /// </exception>
    public static void EnsureLoaded() => Av1Library.EnsureLoaded();

    /// <summary>
    /// True when the native encoder can be loaded on this platform (loading it if necessary). Never throws; when it
    /// is false, <see cref="NativeLoadError" /> says why.
    /// </summary>
    public static bool IsNativeAvailable => TryLoad(out string _);

    /// <summary>
    /// Why the native encoder cannot be loaded, or null when it can (loading it if necessary). Never throws.
    /// </summary>
    public static string NativeLoadError => TryLoad(out string error) ? null : error;

    /// <summary>
    /// The version string of the AV1 encoder library inside the loaded native, for diagnostics. Loads the native
    /// library if necessary.
    /// </summary>
    /// <exception cref="Av1EncoderException">The native library could not be loaded.</exception>
    public static string NativeLibraryVersion => Av1Library.Version;

    /// <summary>Where the native library was loaded from, for diagnostics, or null when it has not been loaded yet.</summary>
    public static string NativeLibraryPath => Av1Library.LoadedPath;

    private static bool TryLoad(out string error)
    {
        try
        {
            Av1Library.EnsureLoaded();
            error = null;
            return true;
        }
        catch (Av1EncoderException failure)
        {
            error = failure.Message;
            return false;
        }
        catch (DllNotFoundException failure)
        {
            error = failure.Message;
            return false;
        }
        catch (EntryPointNotFoundException failure)
        {
            error = failure.Message;
            return false;
        }
    }
}
