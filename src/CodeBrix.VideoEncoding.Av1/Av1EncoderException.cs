using System;
using CodeBrix.VideoProcessing.Core.Encoding;

namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// The native AV1 encoder failed, or the native library could not be loaded. A
/// <see cref="VideoEncodingException" />, so code written against the CodeBrix.VideoProcessing.Core encoder
/// contract catches it without naming this package.
/// </summary>
/// <remarks>
/// <see cref="NativeCode" /> is the native contract's result code (0 when the failure did not come from a
/// native call - for example a library that could not be loaded) and <see cref="NativeMessage" /> the native
/// encoder's own explanation of the failure, when it gave one.
/// </remarks>
public class Av1EncoderException : VideoEncodingException
{
    /// <summary>Creates the exception with a default message.</summary>
    public Av1EncoderException()
        : base("The AV1 encoder failed.")
    {
        NativeMessage = string.Empty;
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public Av1EncoderException(string message)
        : base(message)
    {
        NativeMessage = string.Empty;
    }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public Av1EncoderException(string message, Exception innerException)
        : base(message, innerException)
    {
        NativeMessage = string.Empty;
    }

    /// <summary>Creates the exception for a failed native call.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="nativeCode">The native result code (a negative <c>CBA_E_*</c> value).</param>
    /// <param name="nativeMessage">The native encoder's explanation; null is stored as an empty string.</param>
    public Av1EncoderException(string message, int nativeCode, string nativeMessage)
        : base(message)
    {
        NativeCode = nativeCode;
        NativeMessage = nativeMessage ?? string.Empty;
    }

    /// <summary>
    /// The native result code: -1 invalid argument, -2 out of memory, -3 invalid state, -4 the encoder refused
    /// the settings or a codec option, -5 buffer too small, -6 any other encoder failure. 0 when the failure did
    /// not come from a native call.
    /// </summary>
    public int NativeCode { get; }

    /// <summary>
    /// The native encoder's own explanation of the failure (for example why it refused a codec option), or an
    /// empty string when it gave none. Never null.
    /// </summary>
    public string NativeMessage { get; }
}
