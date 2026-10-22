using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

/// <summary>
/// Pairs two Holm apps over the internet through a "transit relay" (the public one run by the
/// magic-wormhole project). Both sides connect, name the same channel, and the relay pipes bytes between
/// them. The channel name is derived from the share code and everything sent is encrypted with a key
/// derived from it too, so the relay only ever sees encrypted data.
/// </summary>
public static class RelayLink
{
    public const string DefaultHost = "transit.magic-wormhole.io";
    public const int DefaultPort = 4001;

    /// <summary>Connects and waits until the other side has connected to the same channel too.</summary>
    /// <exception cref="IOException">The relay refused or dropped the connection.</exception>
    public static async Task<Stream> ConnectAsync(string host, int port, byte[] token, CancellationToken ct)
    {
        if (token.Length != 32) throw new ArgumentException("Tokens are 32 bytes.", nameof(token));
        var client = new TcpClient { NoDelay = true };
        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                await client.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
            }
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            var stream = new NetworkStream(client.Client, ownsSocket: true);

            string side = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            string handshake = $"please relay {Convert.ToHexString(token).ToLowerInvariant()} for side {side}\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), ct).ConfigureAwait(false);

            // The relay answers only once both sides are there, which can take minutes.
            string reply = await ReadLineAsync(stream, ct).ConfigureAwait(false);
            if (reply != "ok") throw new IOException("The relay said: " + reply);
            return stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (line.Length < 200)
        {
            int read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0) throw new IOException("The relay closed the connection.");
            if (one[0] == (byte)'\n') return line.ToString();
            line.Append((char)one[0]);
        }
        throw new IOException("The relay sent something unexpected.");
    }

    /// <summary>
    /// Keeps trying until the other side shows up, the time runs out or it is cancelled
    /// (the relay may drop a connection that waits for a long time).
    /// </summary>
    public static async Task<Stream> ConnectWithRetryAsync(string host, int port, byte[] token, TimeSpan patience, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(patience);
        while (true)
        {
            try { return await ConnectAsync(host, port, token, deadline.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or SocketException && !deadline.IsCancellationRequested)
            {
                Diag.Log("Relay connection lost, retrying: " + ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(false);
            }
        }
    }
}
