using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using CodeBrix.VideoEncoding.Av1.Internal;
using CodeBrix.VideoEncoding.Av1.Interop;
using CodeBrix.VideoProcessing.Core.Encoding;
using CodeBrix.VideoProcessing.Core.Frames;

namespace CodeBrix.VideoEncoding.Av1;

/// <summary>
/// An AV1 encoder for ONE elementary stream: planar frames in, AV1 temporal units out, through the pull model
/// of the CodeBrix.VideoProcessing.Core <see cref="IVideoEncoder" /> contract.
/// </summary>
/// <remarks>
/// <para>
/// Get one from the registry (<c>VideoEncoders.Get(VideoCodecIds.Av1).Create(request)</c>, after
/// <see cref="CodeBrixVideoEncodingAv1.Register" />), or construct one directly when AV1-specific
/// <see cref="Av1EncoderOptions" /> are wanted. Both check the request completely before any native call.
/// </para>
/// <para>
/// LIFECYCLE. The native encoder starts on the first <see cref="Encode" />, or earlier through
/// <see cref="Start" /> (the registry's factory starts it at once, so configuration problems surface there).
/// <see cref="CodecConfigurationRecord" />, <see cref="SequenceHeaderObu" /> and <see cref="SequenceHeader" /> are
/// taken from the first key frame the encoder produces, so they are empty until the first packet is ready - and
/// always set before <see cref="TryReadPacket" /> returns that packet. Drain <see cref="TryReadPacket" /> after every
/// <see cref="Encode" /> and after <see cref="Flush" />.
/// </para>
/// <para>
/// FRAMES. Each frame must have the request's width, height, layout and bit depth. Its planes are pinned only
/// for the duration of the native call and read in place (strides in bytes, 10/12-bit samples as little-endian
/// 16-bit words); the caller may reuse them as soon as <see cref="Encode" /> returns. An alpha plane is ignored.
/// The encoder numbers frames itself: the Nth call is frame N, at <c>Request.FrameRate.TimestampOf(N)</c>.
/// </para>
/// <para>
/// PACKETS. One per frame, in presentation order, each a complete temporal unit starting with a temporal
/// delimiter OBU, in a fresh array the caller owns. With the default look-ahead the first packets arrive a number
/// of frames late; <see cref="Flush" /> brings out the rest.
/// </para>
/// <para>
/// ERRORS AND STATE. <see cref="Encode" /> after <see cref="Flush" /> throws <see cref="InvalidOperationException" />;
/// a second <see cref="Flush" /> does nothing; <see cref="Dispose" /> may be called any number of times. After
/// <see cref="Dispose" />, <see cref="Start" />, <see cref="Encode" />, <see cref="Flush" /> and
/// <see cref="TryReadPacket" /> throw <see cref="ObjectDisposedException" />; the read-only properties keep their
/// last values. Native failures throw <see cref="Av1EncoderException" /> (a <see cref="VideoEncodingException" />).
/// </para>
/// <para>THREADING: one thread at a time per encoder; separate encoders are independent.</para>
/// </remarks>
public sealed class Av1Encoder : IVideoEncoder
{
    private readonly Av1NativeSettings settings;
    private readonly Queue<EncodedVideoPacket> packets = new Queue<EncodedVideoPacket>();
    private Av1EncoderHandle handle;
    private bool started;
    private bool flushed;
    private bool disposed;
    private long framesSubmitted;
    private long packetsProduced;
    private ReadOnlyMemory<byte> configurationRecord;
    private ReadOnlyMemory<byte> sequenceHeaderObu;
    private Av1SequenceHeaderInfo sequenceHeader;

    /// <summary>Creates an encoder for one stream. Nothing native happens until <see cref="Start" /> or the first <see cref="Encode" />.</summary>
    /// <param name="request">The stream's settings; the encoder keeps its own copy.</param>
    /// <param name="options">AV1-specific settings, or null for the defaults; the encoder keeps its own copy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The request or options fail validation, a value is outside what AV1 allows (Speed above 9, more than 64
    /// threads, a size above 65536, the RGB matrix with subsampled chroma, a codec option that is not an ASCII
    /// libaom option name), or the request and options ask for contradictory rate control; the message names it.
    /// </exception>
    /// <exception cref="NotSupportedException">The request's layout is NV12 or RGBA, or it is I420 with an odd width or height.</exception>
    public Av1Encoder(VideoEncoderRequest request, Av1EncoderOptions options = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        VideoEncoderRequest copy = request.Clone();
        Av1EncoderOptions optionsCopy = (options ?? new Av1EncoderOptions()).Clone();
        settings = Av1RequestMapper.Map(copy, optionsCopy, Environment.ProcessorCount);

        Request = copy;
        Options = optionsCopy;
    }

    /// <inheritdoc />
    public VideoEncoderRequest Request { get; }

    /// <summary>The encoder's own copy of the AV1-specific options.</summary>
    public Av1EncoderOptions Options { get; }

    /// <summary>
    /// The AV1CodecConfigurationRecord (av1C): four bytes describing the stream followed by the sequence header
    /// OBU exactly as the stream carries it. Empty until the first packet is ready (with the default look-ahead,
    /// that is some frames after the first <see cref="Encode" />, or at <see cref="Flush" />); always set before
    /// <see cref="TryReadPacket" /> returns the first packet, and never changes afterwards.
    /// </summary>
    public ReadOnlyMemory<byte> CodecConfigurationRecord => configurationRecord;

    /// <summary>The sequence header OBU exactly as it appears in the bitstream; empty until the first packet is ready.</summary>
    public ReadOnlyMemory<byte> SequenceHeaderObu => sequenceHeaderObu;

    /// <summary>What the stream's sequence header says (profile, level, tier, bit depth, colour); null until the first packet is ready.</summary>
    public Av1SequenceHeaderInfo SequenceHeader => sequenceHeader;

    /// <inheritdoc />
    public long FramesSubmitted => framesSubmitted;

    /// <inheritdoc />
    public long PacketsProduced => packetsProduced;

    /// <summary>True once the native encoder has been started.</summary>
    public bool IsStarted => started;

    /// <summary>True once <see cref="Flush" /> has been called.</summary>
    public bool IsFlushed => flushed;

    /// <summary>True once <see cref="Dispose" /> has been called.</summary>
    public bool IsDisposed => disposed;

    /// <summary>
    /// Creates and configures the native encoder now, so configuration problems (a refused setting or codec option)
    /// surface here rather than at the first <see cref="Encode" />. Calling it again does nothing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="ArgumentException">The encoder refused a codec option (the name or its value); the inner exception carries its explanation.</exception>
    /// <exception cref="Av1EncoderException">The native library could not be loaded, or the encoder refused the settings.</exception>
    public void Start()
    {
        ThrowIfDisposed();
        if (started) return;

        Av1Library.EnsureLoaded();
        Av1EncoderHandle created = Create();
        try
        {
            Configure(created);
            TryReadSequenceHeader(created);
        }
        catch
        {
            created.Dispose();
            throw;
        }

        handle = created;
        started = true;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The frame is empty, or its size, layout or bit depth differs from the request.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Flush" /> has been called.</exception>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="Av1EncoderException">The native encoder failed.</exception>
    public void Encode(in VideoFrameData frame, bool forceKeyFrame)
    {
        ThrowIfDisposed();
        if (flushed) throw new InvalidOperationException("The encoder has been flushed; no more frames can be encoded.");
        CheckFrame(frame);
        Start();

        VideoPlane y = frame.Y;
        VideoPlane u = frame.U;
        VideoPlane v = frame.V;
        long pts = framesSubmitted;

        using (MemoryHandle yPin = y.Data.Pin())
        using (MemoryHandle uPin = u.IsEmpty ? default : u.Data.Pin())
        using (MemoryHandle vPin = v.IsEmpty ? default : v.Data.Pin())
        {
            int code;
            unsafe
            {
                code = Av1Native.EncoderEncode(
                    handle.DangerousGetHandle(),
                    (byte*)yPin.Pointer,
                    y.Stride,
                    (byte*)uPin.Pointer,
                    u.IsEmpty ? 0 : u.Stride,
                    (byte*)vPin.Pointer,
                    v.IsEmpty ? 0 : v.Stride,
                    pts,
                    forceKeyFrame ? 1 : 0);
            }

            Av1Result.ThrowIfFailed(code, $"encoding frame {pts}", handle.DangerousGetHandle());
        }

        framesSubmitted++;
        Drain();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Flushing an encoder that never started marks it flushed without starting it, so it produces no packets and
    /// no configuration record.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="Av1EncoderException">The native encoder failed.</exception>
    public void Flush()
    {
        ThrowIfDisposed();
        if (flushed) return;

        if (started)
        {
            int code = Av1Native.EncoderFlush(handle.DangerousGetHandle());
            Av1Result.ThrowIfFailed(code, "flushing", handle.DangerousGetHandle());
            flushed = true;
            Drain();
            return;
        }

        flushed = true;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    public bool TryReadPacket(out EncodedVideoPacket packet)
    {
        ThrowIfDisposed();
        if (packets.Count == 0)
        {
            packet = default;
            return false;
        }

        packet = packets.Dequeue();
        packetsProduced++;
        return true;
    }

    /// <summary>Releases the native encoder and its threads. Packets not yet read are discarded. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        packets.Clear();
        handle?.Dispose();
        handle = null;
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(Av1Encoder));
    }

    private void CheckFrame(in VideoFrameData frame)
    {
        if (frame.IsEmpty) throw new ArgumentException("The frame is empty.", nameof(frame));
        if (frame.Width != Request.Width || frame.Height != Request.Height)
        {
            throw new ArgumentException(
                $"The frame is {frame.Width}x{frame.Height}; this encoder was created for {Request.Width}x{Request.Height}.",
                nameof(frame));
        }

        if (frame.Layout != Request.PixelLayout)
            throw new ArgumentException($"The frame is {frame.Layout}; this encoder was created for {Request.PixelLayout}.", nameof(frame));
        if (frame.BitDepth != Request.BitDepth)
            throw new ArgumentException($"The frame is {frame.BitDepth}-bit; this encoder was created for {Request.BitDepth}-bit.", nameof(frame));
    }

    private Av1EncoderHandle Create()
    {
        IntPtr created = IntPtr.Zero;
        int code;
        unsafe
        {
            code = Av1Native.EncoderCreate(
                settings.Width,
                settings.Height,
                settings.BitDepth,
                settings.Layout,
                settings.TimebaseNumerator,
                settings.TimebaseDenominator,
                settings.Usage,
                &created);
        }

        Av1Result.ThrowIfFailed(code, "creating the encoder", IntPtr.Zero);
        if (created == IntPtr.Zero) throw new Av1EncoderException("The native encoder returned no handle.", Av1Result.AomFault, string.Empty);
        return new Av1EncoderHandle(created);
    }

    private void Configure(Av1EncoderHandle target)
    {
        IntPtr native = target.DangerousGetHandle();

        foreach (KeyValuePair<int, int> option in settings.IntOptions)
        {
            int code = Av1Native.EncoderSetInt32(native, option.Key, option.Value);
            Av1Result.ThrowIfFailed(code, $"setting option {option.Key} to {option.Value}", native);
        }

        foreach (KeyValuePair<string, string> option in settings.StringOptions)
        {
            int code = SetStringOption(native, option.Key, option.Value);
            Av1Result.ThrowIfFailed(code, $"recording codec option '{option.Key}'", native);
        }

        int startCode = Av1Native.EncoderStart(native);
        if (startCode >= Av1Result.Ok) return;

        Av1EncoderException failure = Av1Result.Failure(startCode, "starting the encoder", native);
        string rejected = FindRejectedCodecOption(failure.NativeMessage);
        if (rejected != null)
        {
            throw new ArgumentException(
                $"The AV1 encoder refused codec option '{rejected}': {failure.NativeMessage}",
                nameof(VideoEncoderRequest.CodecOptions),
                failure);
        }

        throw failure;
    }

    // The native side reports a refused pass-through option as 'option "<name>": <libaom's reason>'; map that
    // case onto the contract's ArgumentException for codec options.
    private string FindRejectedCodecOption(string nativeMessage)
    {
        if (string.IsNullOrEmpty(nativeMessage)) return null;
        foreach (KeyValuePair<string, string> option in settings.StringOptions)
        {
            if (nativeMessage.StartsWith("option \"" + option.Key + "\"", StringComparison.Ordinal)) return option.Key;
        }

        return null;
    }

    private static unsafe int SetStringOption(IntPtr native, string name, string value)
    {
        byte[] nameBytes = ToAsciiZ(name);
        byte[] valueBytes = ToAsciiZ(value);
        fixed (byte* namePointer = nameBytes)
        fixed (byte* valuePointer = valueBytes)
        {
            return Av1Native.EncoderSetStringOption(native, namePointer, valuePointer);
        }
    }

    private static byte[] ToAsciiZ(string text)
    {
        byte[] bytes = new byte[text.Length + 1];
        Encoding.ASCII.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    // The native side takes the sequence header from the first key frame it produces and answers "invalid state"
    // until a packet exists; the record is therefore built while draining, before the first packet is queued.
    private void TryReadSequenceHeader(Av1EncoderHandle target)
    {
        if (!sequenceHeaderObu.IsEmpty) return;
        IntPtr native = target.DangerousGetHandle();
        int length = Av1Native.EncoderSequenceHeaderLength(native);
        if (length == Av1Result.InvalidState) return;
        ReadOnlyMemory<byte> obu = ReadSequenceHeader(native, length);
        Av1SequenceHeaderInfo info = Av1SequenceHeaderReader.ReadObu(obu.Span);
        configurationRecord = Av1ConfigurationRecord.Build(info, obu.Span);
        sequenceHeader = info;
        sequenceHeaderObu = obu;
    }

    private static ReadOnlyMemory<byte> ReadSequenceHeader(IntPtr native, int length)
    {
        Av1Result.ThrowIfFailed(length, "reading the sequence header length", native);
        if (length == 0) throw new Av1EncoderException("The native encoder produced an empty sequence header.", Av1Result.AomFault, string.Empty);

        byte[] obu = new byte[length];
        int copied;
        unsafe
        {
            fixed (byte* destination = obu)
            {
                copied = Av1Native.EncoderReadSequenceHeader(native, destination, obu.Length);
            }
        }

        Av1Result.ThrowIfFailed(copied, "reading the sequence header", native);
        return copied == obu.Length ? obu : obu.AsMemory(0, copied);
    }

    private void Drain()
    {
        IntPtr native = handle.DangerousGetHandle();
        while (true)
        {
            int pending = Av1Native.EncoderPendingPackets(native);
            Av1Result.ThrowIfFailed(pending, "counting packets", native);
            if (pending == 0) return;

            int length;
            long pts;
            int key;
            int code;
            unsafe
            {
                code = Av1Native.EncoderPacketInfo(native, &length, &pts, &key);
            }

            Av1Result.ThrowIfFailed(code, "describing a packet", native);

            byte[] data = new byte[length];
            int copied;
            unsafe
            {
                fixed (byte* destination = data)
                {
                    copied = Av1Native.EncoderReadPacket(native, destination, data.Length);
                }
            }

            Av1Result.ThrowIfFailed(copied, "reading a packet", native);
            if (copied != length)
            {
                throw new Av1EncoderException(
                    $"The native encoder described a {length}-byte packet but copied {copied} bytes.",
                    Av1Result.AomFault,
                    string.Empty);
            }

            if (sequenceHeaderObu.IsEmpty) TryReadSequenceHeader(handle);
            packets.Enqueue(new EncodedVideoPacket(data, Request.FrameRate.TimestampOf(pts), pts, key != 0));
        }
    }
}
