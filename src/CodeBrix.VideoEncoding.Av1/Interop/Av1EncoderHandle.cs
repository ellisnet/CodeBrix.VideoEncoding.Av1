using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoEncoding.Av1.Interop;

/// <summary>
/// Owns one native <c>codebrix_av1_encoder</c>, so an encoder that is never disposed still releases its native
/// memory and libaom's worker threads when it is collected.
/// </summary>
internal sealed class Av1EncoderHandle : SafeHandle
{
    /// <summary>Wraps a handle returned by <c>codebrix_av1_encoder_create</c>.</summary>
    /// <param name="existing">The native handle.</param>
    public Av1EncoderHandle(IntPtr existing) : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(existing);
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        Av1Native.EncoderDestroy(handle);
        return true;
    }
}
