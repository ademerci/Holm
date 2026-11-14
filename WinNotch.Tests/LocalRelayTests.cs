using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>
/// Sending to a friend through a relay on this machine that behaves like the public one: it pairs the two sides by
/// token and pipes bytes. No internet needed, so it runs in CI, unlike <see cref="RelayTests"/>.
/// </summary>
public class LocalRelayTests
{
    [Fact]
    public async Task A_friend_who_joins_late_after_a_dropped_connection_still_gets_the_file()
    {
        using var relay = new LocalRelay(dropFirst: true);
        using var source = new TempFolder();
        using var downloads = new TempFolder();
        var content = RandomNumberGenerator.GetBytes(300_000);
        string path = source.File("Holiday.jpg", content);
        var secret = ShareCode.NewSecret();
        var ct = Net.Timeout(60);

        // "localhost" has an IPv6 and an IPv4 address; the relay listens on IPv4 only, like a network without IPv6.
        var sending = Task.Run(async () =>
        {
            await using var stream = await RelayLink.ConnectWithRetryAsync("localhost", relay.Port, ShareCode.RelayToken(secret), FriendsService.SendPatience, ct);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
            return await FileTransfer.SendAsync(channel, "Noah", new[] { path }, null, ct);
        });
        await Task.Delay(TimeSpan.FromSeconds(4)); // the friend's notch gets the note a while later
        var receiving = Task.Run(async () =>
        {
            await using var stream = await RelayLink.ConnectWithRetryAsync("localhost", relay.Port, ShareCode.RelayToken(secret), TimeSpan.FromMinutes(2), ct);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            return await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(true), downloads.Path, null, ct);
        });

        Assert.True(await sending);
        Assert.Equal(content, File.ReadAllBytes((await receiving)!.Single()));
        Assert.True(relay.Dropped);
    }

    [Fact]
    public async Task A_relay_nobody_answers_on_is_an_io_error_not_a_hang()
    {
        // A port nothing listens on: refused at once, on every address.
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        await Assert.ThrowsAsync<IOException>(() => RelayLink.ConnectAsync("localhost", port, new byte[32], Net.Timeout(10)));
    }

    [Fact]
    public void A_sender_waits_longer_than_the_age_a_note_may_have()
        => Assert.True(FriendsService.SendPatience >= FriendsService.Patience * 2);

    /// <summary>A transit relay in miniature: "please relay TOKEN for side SIDE\n", then "ok\n" to both once both are there.</summary>
    sealed class LocalRelay : IDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly Dictionary<string, NetworkStream> _waiting = new();
        bool _dropFirst;

        public LocalRelay(bool dropFirst)
        {
            _dropFirst = dropFirst;
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public bool Dropped { get; private set; }

        async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }
                _ = ServeAsync(client);
            }
        }

        async Task ServeAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var line = new StringBuilder();
            var one = new byte[1];
            while (await stream.ReadAsync(one) == 1 && one[0] != '\n') line.Append((char)one[0]);
            string token = line.ToString().Split(' ')[2];
            NetworkStream? other;
            lock (_waiting)
            {
                if (_dropFirst)
                {
                    // The public relay drops connections that wait too long; the side must come back.
                    _dropFirst = false;
                    Dropped = true;
                    client.Dispose();
                    return;
                }
                if (!_waiting.Remove(token, out other))
                {
                    _waiting[token] = stream;
                    return;
                }
            }
            var ok = Encoding.ASCII.GetBytes("ok\n");
            await other.WriteAsync(ok);
            await stream.WriteAsync(ok);
            _ = Pipe(other, stream);
            _ = Pipe(stream, other);
        }

        static async Task Pipe(Stream from, Stream to)
        {
            try { await from.CopyToAsync(to); }
            catch (Exception) { }
            finally { to.Dispose(); }
        }

        public void Dispose() => _listener.Stop();
    }
}
