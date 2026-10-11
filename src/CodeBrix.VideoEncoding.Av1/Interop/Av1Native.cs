using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoEncoding.Av1.Interop;

/// <summary>
/// The codebrix_av1 entry points, as source-generated <c>LibraryImport</c> declarations mirroring
/// <c>av1-native-tools/shim/codebrix_av1.h</c> one for one.
/// </summary>
/// <remarks>
/// Every parameter is an opaque handle, a 32- or 64-bit integer or a byte / integer pointer - the header keeps
/// C <c>long</c>, callbacks and every libaom structure out of the contract - so one declaration set is right on
/// every platform. The static constructor installs the library resolver, so the first call through any of these
/// finds the native in the package's <c>runtimes/</c> layout.
/// </remarks>
internal static unsafe partial class Av1Native
{
    static Av1Native()
    {
        Av1Library.EnsureResolverInstalled();
    }

    /// <summary>Returns the contract version of the loaded library.</summary>
    /// <returns><c>CODEBRIX_AV1_API_VERSION</c>.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_api_version")]
    internal static partial int ApiVersion();

    /// <summary>Returns libaom's own version string. The pointer is static and owned by the library.</summary>
    /// <returns>A pointer to a NUL-terminated ASCII string.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_library_version")]
    internal static partial IntPtr LibraryVersion();

    /// <summary>Creates an encoder; libaom is not initialised until <see cref="EncoderStart" />.</summary>
    /// <param name="width">Width in pixels, 1 to 65536.</param>
    /// <param name="height">Height in pixels, 1 to 65536.</param>
    /// <param name="bitDepth">8, 10 or 12.</param>
    /// <param name="layout">A <c>CBA_LAYOUT_*</c> value.</param>
    /// <param name="timebaseNumerator">The pts unit's numerator, in seconds.</param>
    /// <param name="timebaseDenominator">The pts unit's denominator.</param>
    /// <param name="usage">A <c>CBA_USAGE_*</c> value.</param>
    /// <param name="encoder">Receives the handle.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_create")]
    internal static partial int EncoderCreate(
        int width,
        int height,
        int bitDepth,
        int layout,
        int timebaseNumerator,
        int timebaseDenominator,
        int usage,
        IntPtr* encoder);

    /// <summary>Sets one <c>CBA_OPT_*</c> integer option. Before start only.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="option">The option id.</param>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_set_int32")]
    internal static partial int EncoderSetInt32(IntPtr encoder, int option, int value);

    /// <summary>Records a libaom option by name and value, applied at start. Before start only.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="nameAscii">The NUL-terminated ASCII option name.</param>
    /// <param name="valueAscii">The NUL-terminated ASCII value.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_set_string_option")]
    internal static partial int EncoderSetStringOption(IntPtr encoder, byte* nameAscii, byte* valueAscii);

    /// <summary>Initialises libaom with everything set so far.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_start")]
    internal static partial int EncoderStart(IntPtr encoder);

    /// <summary>Encodes one frame.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="y">The luma plane.</param>
    /// <param name="yStrideBytes">The luma stride in bytes.</param>
    /// <param name="u">The Cb plane, or null for monochrome.</param>
    /// <param name="uStrideBytes">The Cb stride in bytes.</param>
    /// <param name="v">The Cr plane, or null for monochrome.</param>
    /// <param name="vStrideBytes">The Cr stride in bytes.</param>
    /// <param name="pts">The presentation time in timebase units (the frame index).</param>
    /// <param name="forceKeyFrame">1 to force a key frame.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_encode")]
    internal static partial int EncoderEncode(
        IntPtr encoder,
        byte* y,
        int yStrideBytes,
        byte* u,
        int uStrideBytes,
        byte* v,
        int vStrideBytes,
        long pts,
        int forceKeyFrame);

    /// <summary>Ends the stream, queueing every remaining temporal unit.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_flush")]
    internal static partial int EncoderFlush(IntPtr encoder);

    /// <summary>The number of temporal units waiting in the packet queue.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The count, or negative for a null handle.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_pending_packets")]
    internal static partial int EncoderPendingPackets(IntPtr encoder);

    /// <summary>Describes the oldest pending packet without removing it.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="length">Receives its length in bytes.</param>
    /// <param name="pts">Receives its pts.</param>
    /// <param name="isKeyFrame">Receives 1 for a key frame.</param>
    /// <returns>A <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_packet_info")]
    internal static partial int EncoderPacketInfo(IntPtr encoder, int* length, long* pts, int* isKeyFrame);

    /// <summary>Copies the oldest pending packet out and removes it from the queue.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="destination">Where to copy to.</param>
    /// <param name="capacity">How many bytes fit.</param>
    /// <returns>The bytes copied, or a negative <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_read_packet")]
    internal static partial int EncoderReadPacket(IntPtr encoder, byte* destination, int capacity);

    /// <summary>The length of the sequence header OBU. Valid after start.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The length, or a negative <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_sequence_header_length")]
    internal static partial int EncoderSequenceHeaderLength(IntPtr encoder);

    /// <summary>Copies the sequence header OBU.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="destination">Where to copy to.</param>
    /// <param name="capacity">How many bytes fit.</param>
    /// <returns>The bytes copied, or a negative <see cref="Av1Result" /> code.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_read_sequence_header")]
    internal static partial int EncoderReadSequenceHeader(IntPtr encoder, byte* destination, int capacity);

    /// <summary>Frames accepted by encode so far.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The count, or negative for a null handle.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_frames_submitted")]
    internal static partial long EncoderFramesSubmitted(IntPtr encoder);

    /// <summary>Temporal units produced so far, read or not.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The count, or negative for a null handle.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_packets_produced")]
    internal static partial long EncoderPacketsProduced(IntPtr encoder);

    /// <summary>libaom's explanation of the last failing call, or "" after a successful one.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>A pointer to a NUL-terminated string owned by the handle.</returns>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_last_error")]
    internal static partial IntPtr EncoderLastError(IntPtr encoder);

    /// <summary>Releases an encoder, including libaom's threads. A null handle is ignored.</summary>
    /// <param name="encoder">The encoder.</param>
    [LibraryImport(Av1Library.LibraryName, EntryPoint = "codebrix_av1_encoder_destroy")]
    internal static partial void EncoderDestroy(IntPtr encoder);
}
