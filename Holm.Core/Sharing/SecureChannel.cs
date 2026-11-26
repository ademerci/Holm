using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

/// <summary>
/// Length-prefixed AES-256-GCM frames over any stream. Each direction has its own nonce prefix and
/// counter, so a nonce is never reused; a frame that was changed, dropped or replayed fails to decrypt.
/// One reader and one writer may use it at the same time.
/// </summary>
public sealed class SecureChannel : IAsyncDisposable, IDisposable
{
    public const int MaxPlaintext = 1 << 20;
    const int TagSize = 16;

    readonly Stream _stream;
    readonly AesGcm _aes;
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly uint _sendDirection, _receiveDirection;
    ulong _sent, _received;
    int _disposed;

    /// <summary>
    /// Optional 6-digit Short Authentication String (SAS) derived from the handshake transcript for
    /// unauthenticated LAN connections so both sides can verify the channel out-of-band.
    /// </summary>
    public string? VerificationCode { get; }

    /// <summary>Whether the peer's identity was cryptographically authenticated during channel setup.</summary>
    public bool IsAuthenticated { get; }

    /// <param name="initiator">The side that starts the conversation (the sender); the other side passes false.</param>
    public SecureChannel(Stream stream, byte[] key, bool initiator, string? verificationCode = null, bool authenticated = true)
    {
        if (key.Length != 32) throw new ArgumentException("Keys are 32 bytes.", nameof(key));
        _stream = stream;
        _aes = new AesGcm(key, TagSize);
        (_sendDirection, _receiveDirection) = initiator ? (1u, 2u) : (2u, 1u);
        VerificationCode = verificationCode;
        IsAuthenticated = authenticated;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> plaintext, CancellationToken ct)
    {
        if (plaintext.Length > MaxPlaintext) throw new ArgumentException("Frame too large.", nameof(plaintext));
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var frame = new byte[4 + plaintext.Length + TagSize];
            BinaryPrimitives.WriteInt32BigEndian(frame, plaintext.Length + TagSize);
            ulong counter = _sent++;
            _aes.Encrypt(Nonce(_sendDirection, counter), plaintext.Span,
                         frame.AsSpan(4, plaintext.Length), frame.AsSpan(4 + plaintext.Length, TagSize));
            await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            try { _sendLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <exception cref="EndOfStreamException">The other side hung up.</exception>
    /// <exception cref="AuthenticationTagMismatchException">Wrong key, or the data was tampered with.</exception>
    public async Task<byte[]> ReceiveAsync(CancellationToken ct)
    {
        var header = new byte[4];
        await _stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < TagSize || length > MaxPlaintext + TagSize) throw new InvalidDataException("Bad frame length.");
        var data = new byte[length];
        await _stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        var plaintext = new byte[length - TagSize];
        _aes.Decrypt(Nonce(_receiveDirection, _received++), data.AsSpan(0, length - TagSize),
                     data.AsSpan(length - TagSize), plaintext);
        return plaintext;
    }

    static byte[] Nonce(uint direction, ulong counter)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(nonce, direction);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _aes.Dispose();
        _stream.Dispose();
        _sendLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
