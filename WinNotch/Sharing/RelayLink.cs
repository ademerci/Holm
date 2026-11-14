using System;
using System.IO;
using System.Linq;
using System.Net;
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
        var client = await ReachAsync(host, port, ct).ConfigureAwait(false);
        try
        {
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

    /// <summary>How long one address of the relay gets to answer before the next one is tried.</summary>
    internal static TimeSpan AddressPatience = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A TCP connection to the relay, IPv4 addresses first, each given <see cref="AddressPatience"/>. Trying the host name
    /// as a whole went through its addresses one by one, IPv6 first: a network that silently drops IPv6 spent the whole
    /// wait for a friend on that one address, and the send failed every time.
    /// </summary>
    /// <exception cref="IOException">No address of the relay could be reached.</exception>
    static async Task<TcpClient> ReachAsync(string host, int port, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host, out var ip) ? new[] { ip } : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false); }
        catch (SocketException ex) { throw new IOException("The relay's address couldn't be found.", ex); }
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var client = new TcpClient(address.AddressFamily) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(AddressPatience);
            try
            {
                await client.ConnectAsync(address, port, attempt.Token).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                client.Dispose();
                last = ex;
            }
        }
        throw new IOException("The relay couldn't be reached.", last);
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
