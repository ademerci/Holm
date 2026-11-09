using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

// Streaming your screen to friends. The streamer leaves each friend a sealed "stream" note with a fresh secret, as for
// files; both notches meet on the transit relay with it and talk over a SecureChannel. The viewer says Watch (or No),
// then the streamer sends JPEG frames as they come, a sign of life while the screen doesn't change, and End when it
// stops. Each viewer has a channel of their own; a viewer that is still busy with one frame skips the next, so a slow
// connection gets fewer frames instead of falling behind.

public static class StreamProtocol
{
    public const byte Watch = (byte)'W', No = (byte)'N', Frame = (byte)'F', Alive = (byte)'K', End = (byte)'E';

    /// <summary>Without a frame or a sign of life for this long, the stream is gone.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(20);
    /// <summary>A sign of life goes out when the screen hasn't changed for this long.</summary>
    public static readonly TimeSpan AliveEvery = TimeSpan.FromSeconds(3);

    public static byte[] FrameMessage(byte[] jpeg)
    {
        if (jpeg.Length + 1 > SecureChannel.MaxPlaintext) throw new ArgumentException("Frame too large.", nameof(jpeg));
        var message = new byte[jpeg.Length + 1];
        message[0] = Frame;
        jpeg.CopyTo(message, 1);
        return message;
    }

    /// <summary>
    /// The viewer's side: says it watches, then hands each frame to <paramref name="frame"/> until the streamer ends,
    /// goes silent or <paramref name="ct"/> fires. True if the streamer said End.
    /// </summary>
    public static async Task<bool> WatchAsync(SecureChannel channel, Action<byte[]> frame, CancellationToken ct)
    {
        await channel.SendAsync(new[] { Watch }, ct).ConfigureAwait(false);
        while (true)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
            quiet.CancelAfter(Silence);
            byte[] message;
            try { message = await channel.ReceiveAsync(quiet.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; } // silence
            if (message.Length == 0) continue;
            switch (message[0])
            {
                case Frame when message.Length > 1: frame(message[1..]); break;
                case End: return true;
                // Alive, and anything a newer Holm may send: keep watching.
            }
        }
    }

    /// <summary>The viewer's "not now", so the streamer stops waiting.</summary>
    public static Task DeclineAsync(SecureChannel channel, CancellationToken ct) => channel.SendAsync(new[] { No }, ct);

    /// <summary>The streamer's side: waits for the viewer's answer. True for Watch.</summary>
    public static async Task<bool> AnswerAsync(SecureChannel channel, CancellationToken ct)
    {
        byte[] answer = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        return answer.Length > 0 && answer[0] == Watch;
    }
}

/// <summary>The streamer's viewers, each with their channel; frames go to every viewer that is ready for one.</summary>
public sealed class StreamHost : IAsyncDisposable
{
    static readonly TimeSpan SendPatience = TimeSpan.FromSeconds(15);

    sealed class Viewer
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required SecureChannel Channel { get; init; }
        public int Busy;
    }

    readonly List<Viewer> _viewers = new();
    readonly object _lock = new();
    readonly CancellationTokenSource _stop = new();
    DateTime _lastSent = DateTime.MinValue;
    int _lastHash;
    bool _ended;

    /// <summary>A viewer joined or left (any thread).</summary>
    public event Action? ViewersChanged;

    public DateTime Started { get; } = DateTime.UtcNow;

    public IReadOnlyList<string> Names { get { lock (_lock) return _viewers.Select(v => v.Name).ToList(); } }

    public bool Has(string key) { lock (_lock) return _viewers.Any(v => v.Key == key); }

    /// <summary>A viewer who said Watch. The host owns the channel from now on.</summary>
    public void Add(string key, string name, SecureChannel channel)
    {
        lock (_lock)
        {
            if (_ended) { channel.Dispose(); return; }
            _viewers.Add(new Viewer { Key = key, Name = name, Channel = channel });
        }
        _lastHash = 0; // the newcomer needs a picture right away
        ViewersChanged?.Invoke();
    }

    /// <summary>
    /// A new picture of the screen (from the capture thread). The same picture again only goes as a sign of life,
    /// every few seconds.
    /// </summary>
    public void Publish(byte[] jpeg)
    {
        int hash = Hash(jpeg);
        bool same = hash == _lastHash;
        if (same && DateTime.UtcNow - _lastSent < StreamProtocol.AliveEvery) return;
        byte[] message = same ? new[] { StreamProtocol.Alive } : StreamProtocol.FrameMessage(jpeg);
        Viewer[] viewers;
        lock (_lock) viewers = _viewers.ToArray();
        bool sentFrame = false;
        foreach (var viewer in viewers)
        {
            if (Interlocked.CompareExchange(ref viewer.Busy, 1, 0) != 0) continue; // still on the last one
            sentFrame = true;
            _ = SendAsync(viewer, message);
        }
        if (sentFrame || viewers.Length == 0)
        {
            _lastHash = hash;
            _lastSent = DateTime.UtcNow;
        }
    }

    async Task SendAsync(Viewer viewer, byte[] message)
    {
        try
        {
            using var patience = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            patience.CancelAfter(SendPatience);
            await viewer.Channel.SendAsync(message, patience.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref viewer.Busy, 0);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException)
        {
            Remove(viewer); // they closed the window or the connection died
        }
    }

    void Remove(Viewer viewer)
    {
        bool removed;
        lock (_lock) removed = _viewers.Remove(viewer);
        if (!removed) return;
        try { viewer.Channel.Dispose(); } catch (Exception) { }
        ViewersChanged?.Invoke();
    }

    /// <summary>Tells every viewer the stream ended and hangs up.</summary>
    public async ValueTask DisposeAsync()
    {
        Viewer[] viewers;
        lock (_lock)
        {
            if (_ended) return;
            _ended = true;
            viewers = _viewers.ToArray();
            _viewers.Clear();
        }
        _stop.Cancel();
        foreach (var viewer in viewers)
        {
            try
            {
                using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await viewer.Channel.SendAsync(new[] { StreamProtocol.End }, bye.Token).ConfigureAwait(false);
            }
            catch (Exception) { } // gone already
            try { viewer.Channel.Dispose(); } catch (Exception) { }
        }
        _stop.Dispose();
        if (viewers.Length > 0) ViewersChanged?.Invoke();
    }

    /// <summary>A quick fingerprint: the same JPEG bytes mean the same screen.</summary>
    static int Hash(byte[] data)
    {
        var hash = new HashCode();
        hash.AddBytes(data);
        return hash.ToHashCode();
    }
}
