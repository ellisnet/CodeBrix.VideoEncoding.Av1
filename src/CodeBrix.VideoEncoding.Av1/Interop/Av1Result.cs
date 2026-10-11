using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoEncoding.Av1.Interop;

/// <summary>
/// The result codes of the native contract (<c>CBA_*</c> in <c>codebrix_av1.h</c>), and the text the managed
/// side reports for each.
/// </summary>
internal static class Av1Result
{
    /// <summary><c>CBA_OK</c>.</summary>
    public const int Ok = 0;

    /// <summary><c>CBA_E_INVALID_ARGUMENT</c>.</summary>
    public const int InvalidArgument = -1;

    /// <summary><c>CBA_E_OUT_OF_MEMORY</c>.</summary>
    public const int OutOfMemory = -2;

    /// <summary><c>CBA_E_INVALID_STATE</c>.</summary>
    public const int InvalidState = -3;

    /// <summary><c>CBA_E_UNSUPPORTED</c>.</summary>
    public const int Unsupported = -4;

    /// <summary><c>CBA_E_BUFFER_TOO_SMALL</c>.</summary>
    public const int BufferTooSmall = -5;

    /// <summary><c>CBA_E_AOM_FAULT</c>.</summary>
    public const int AomFault = -6;

    /// <summary>Describes a result code in words.</summary>
    /// <param name="code">The code.</param>
    /// <returns>A short description.</returns>
    public static string Describe(int code) =>
        code switch
        {
            Ok => "success",
            InvalidArgument => "an argument was out of range (CBA_E_INVALID_ARGUMENT)",
            OutOfMemory => "the native encoder ran out of memory (CBA_E_OUT_OF_MEMORY)",
            InvalidState => "the call is not valid in the encoder's current state (CBA_E_INVALID_STATE)",
            Unsupported => "the AV1 encoder refused this combination of settings or codec options (CBA_E_UNSUPPORTED)",
            BufferTooSmall => "the destination buffer was too small (CBA_E_BUFFER_TOO_SMALL)",
            AomFault => "the AV1 encoder failed (CBA_E_AOM_FAULT)",
            _ => $"unknown native result code {code}",
        };

    /// <summary>Reads the native explanation of the last failing call on <paramref name="encoder" />.</summary>
    /// <param name="encoder">The native handle, or <see cref="IntPtr.Zero" />.</param>
    /// <returns>The text, or an empty string.</returns>
    public static string LastError(IntPtr encoder)
    {
        if (encoder == IntPtr.Zero) return string.Empty;
        IntPtr text = Av1Native.EncoderLastError(encoder);
        return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(text) ?? string.Empty;
    }

    /// <summary>Builds the exception for a failed native call.</summary>
    /// <param name="code">The code the call returned.</param>
    /// <param name="operation">What was being done, for the message.</param>
    /// <param name="encoder">The native handle the call was made on, for the native explanation.</param>
    /// <returns>The exception.</returns>
    public static Av1EncoderException Failure(int code, string operation, IntPtr encoder)
    {
        string detail = LastError(encoder);
        string message = $"AV1 encoding failed while {operation}: {Describe(code)}.";
        if (detail.Length > 0) message += $" Encoder said: {detail}";
        return new Av1EncoderException(message, code, detail);
    }

    /// <summary>Throws an <see cref="Av1EncoderException" /> when <paramref name="code" /> is negative.</summary>
    /// <param name="code">The code a native call returned.</param>
    /// <param name="operation">What was being done, for the message.</param>
    /// <param name="encoder">The native handle the call was made on.</param>
    public static void ThrowIfFailed(int code, string operation, IntPtr encoder)
    {
        if (code >= Ok) return;
        throw Failure(code, operation, encoder);
    }
}
