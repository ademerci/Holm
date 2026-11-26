using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Sharing;

/// <summary>Another Holm on the same network (unauthenticated LAN announcement unless verified via SAS).</summary>
public sealed record NearbyPeer(Guid Id, string Name, IPEndPoint Endpoint, DateTime LastSeen, bool Authenticated = false);

/// <summary>
/// Finds other Holm apps on the local network (a small UDP broadcast) and accepts their transfers
/// (TCP). Each connection agrees on a fresh key (ECDH P-256), so nothing crosses the network in clear text.
/// Nothing polls: there is one announcement a minute, and others answer when someone asks.
/// </summary>
public sealed class NearbyService : IDisposable
{
    public const int DefaultDiscoveryPort = 47811;
    const string App = "WinNotch"; // the app's former name, kept so Holm and older versions still find each other
    static readonly TimeSpan PeerLifetime = TimeSpan.FromMinutes(3);

    readonly Guid _id = Guid.NewGuid();
    readonly string _name;
    readonly int _discoveryPort;
    readonly IReadOnlyList<IPEndPoint> _extraTargets;
    readonly ConcurrentDictionary<Guid, NearbyPeer> _peers = new();
    readonly SemaphoreSlim _incomingSlot = new(1, 1);
    UdpClient? _udp;
    TcpListener? _tcp;
    CancellationTokenSource? _cts;
    int _tcpPort;
    IPEndPoint[]? _targets; // where to announce; looked up again when an address changes

    /// <summary>The peer list changed. Raised on a worker thread.</summary>
    public event Action? PeersChanged;

    /// <summary>
    /// Handles an incoming transfer (normally <see cref="FileTransfer.ReceiveAsync"/> with the user's answer).
    /// Only one runs at a time; others are turned away while it does.
    /// </summary>
    public Func<SecureChannel, string, Task>? Incoming { get; set; }

    /// <param name="extraTargets">Also announce to these endpoints (used by the tests, which run two services on one machine).</param>
    public NearbyService(string name, int discoveryPort = DefaultDiscoveryPort, IReadOnlyList<IPEndPoint>? extraTargets = null)
    {
        _name = name;
        _discoveryPort = discoveryPort;
        _extraTargets = extraTargets ?? Array.Empty<IPEndPoint>();
    }

    public bool IsRunning => _cts != null;
    public Guid Id => _id;

    public IReadOnlyList<NearbyPeer> Peers
        => _peers.Values.Where(p => DateTime.UtcNow - p.LastSeen < PeerLifetime).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public void Start()
    {
        if (_cts != null) return;
        var cts = new CancellationTokenSource();
        try
        {
            _tcp = new TcpListener(IPAddress.Any, 0);
            _tcp.Start(4);
            _tcpPort = ((IPEndPoint)_tcp.LocalEndpoint).Port;

            _udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false, EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
        }
        catch
        {
            _tcp?.Stop(); _tcp = null;
            _udp?.Dispose(); _udp = null;
            cts.Dispose();
            throw;
        }
        _cts = cts;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _ = ListenAsync(cts.Token);
        _ = AcceptAsync(cts.Token);
        _ = AnnounceAsync(cts.Token);
    }

    public void Stop()
    {
        if (_cts == null) return;
        try { Broadcast("bye"); } catch { }
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _targets = null;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        _tcp?.Stop(); _tcp = null;
        _udp?.Dispose(); _udp = null;
        if (!_peers.IsEmpty) { _peers.Clear(); PeersChanged?.Invoke(); }
    }

    public void Dispose() => Stop();

    /// <summary>Asks everyone to answer now, and waits a moment for the answers.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_cts == null) return;
        Broadcast("query");
        await Task.Delay(700, ct).ConfigureAwait(false);
    }

    /// <summary>Connects to a peer for sending; the returned channel is encrypted with an out-of-band SAS code.</summary>
    public static async Task<SecureChannel> ConnectAsync(NearbyPeer peer, CancellationToken ct)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(peer.Endpoint, timeout.Token).ConfigureAwait(false);
            var stream = new NetworkStream(client.Client, ownsSocket: true);
            var (key, sas) = await AgreeKeyWithSasAsync(stream, initiator: true, timeout.Token).ConfigureAwait(false);
            return new SecureChannel(stream, key, initiator: true, verificationCode: sas, authenticated: false);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // ───────────── discovery ─────────────

    sealed record Packet(string App, int V, string T, Guid Id, string Name, int Port);

    async Task AnnounceAsync(CancellationToken ct)
    {
        try
        {
            Broadcast("query"); // introduces us and asks the others to say hello
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                Broadcast("hello");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diag.Log("Nearby announce stopped: " + ex.Message); }
    }

    void Broadcast(string type)
    {
        var udp = _udp;
        if (udp == null) return;
        var bytes = Serialize(type);
        foreach (var target in Targets())
        {
            try { udp.Send(bytes, bytes.Length, target); }
            catch (SocketException) { } // an interface that went away
        }
    }

    byte[] Serialize(string type)
        => JsonSerializer.SerializeToUtf8Bytes(new Packet(App, 1, type, _id, _name, _tcpPort));

    void OnAddressChanged(object? sender, EventArgs e) => _targets = null;

    /// <summary>
    /// The broadcast address of every IPv4 network we're on, plus the extra targets. Listing the network adapters
    /// is the expensive part of the announcement, so the list is kept until Windows reports a changed address.
    /// </summary>
    IPEndPoint[] Targets() => _targets ??= FindTargets().ToArray();

    IEnumerable<IPEndPoint> FindTargets()
    {
        var seen = new HashSet<IPAddress> { IPAddress.Broadcast };
        yield return new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); } catch { interfaces = Array.Empty<NetworkInterface>(); }
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            UnicastIPAddressInformationCollection addresses;
            try { addresses = nic.GetIPProperties().UnicastAddresses; } catch { continue; }
            foreach (var address in addresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null) continue;
                var ip = address.Address.GetAddressBytes();
                var mask = address.IPv4Mask.GetAddressBytes();
                if (mask.All(b => b == 0)) continue;
                var broadcast = new byte[4];
                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                var target = new IPAddress(broadcast);
                if (seen.Add(target)) yield return new IPEndPoint(target, _discoveryPort);
            }
        }
        foreach (var extra in _extraTargets) yield return extra;
    }

    async Task ListenAsync(CancellationToken ct)
    {
        var udp = _udp!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udp.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; } // e.g. ICMP "port unreachable" from an earlier send

            Packet? packet;
            try { packet = result.Buffer.Length <= 2048 ? JsonSerializer.Deserialize<Packet>(result.Buffer) : null; }
            catch (JsonException) { continue; }
            if (packet == null || packet.App != App || packet.V != 1 || packet.Id == _id) continue;

            var now = DateTime.UtcNow;
            var senderIp = result.RemoteEndPoint.Address;
            if (packet.T == "bye")
            {
                if (_peers.TryGetValue(packet.Id, out var existingBye)
                    && existingBye.Endpoint.Address.Equals(senderIp)
                    && _peers.TryRemove(packet.Id, out _))
                    PeersChanged?.Invoke();
                continue;
            }
            if (packet.Port is <= 0 or > 65535) continue;

            // Refuse to overwrite an active, non-expired peer entry from a different source IP (HOLM-SEC-002).
            if (_peers.TryGetValue(packet.Id, out var existing)
                && now - existing.LastSeen < PeerLifetime
                && !existing.Endpoint.Address.Equals(senderIp))
            {
                continue;
            }

            string name = new string((packet.Name ?? "").Where(c => !char.IsControl(c)).Take(40).ToArray()).Trim();
            var peer = new NearbyPeer(packet.Id, name.Length > 0 ? name : "Holm", new IPEndPoint(senderIp, packet.Port), now, Authenticated: false);
            bool isNew = !_peers.ContainsKey(packet.Id);
            _peers[packet.Id] = peer;
            if (isNew) PeersChanged?.Invoke();

            if (packet.T == "query")
            {
                var reply = Serialize("hello");
                try { await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }

    // ───────────── incoming transfers ─────────────

    async Task AcceptAsync(CancellationToken ct)
    {
        var listener = _tcp!;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            _ = HandleAsync(client, ct);
        }
    }

    async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            if (!await _incomingSlot.WaitAsync(0, ct).ConfigureAwait(false)) return; // already busy with someone
            try
            {
                client.NoDelay = true;
                var stream = new NetworkStream(client.Client, ownsSocket: false);
                byte[] key;
                string sas;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    (key, sas) = await AgreeKeyWithSasAsync(stream, initiator: false, timeout.Token).ConfigureAwait(false);
                }
                await using var channel = new SecureChannel(stream, key, initiator: false, verificationCode: sas, authenticated: false);
                var handler = Incoming;
                if (handler != null)
                    await handler(channel, ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString()).ConfigureAwait(false);
            }
            catch (Exception ex) { Diag.Log("Incoming transfer failed: " + ex.Message); }
            finally { _incomingSlot.Release(); }
        }
    }

    /// <summary>
    /// Ephemeral ECDH: both sides send a fresh NIST P-256 public key; the shared key and a 6-digit
    /// Short Authentication String (SAS) both cover the transcript of the two public keys.
    /// </summary>
    internal static async Task<byte[]> AgreeKeyAsync(Stream stream, bool initiator, CancellationToken ct)
        => (await AgreeKeyWithSasAsync(stream, initiator, ct).ConfigureAwait(false)).Key;

    internal static async Task<(byte[] Key, string Sas)> AgreeKeyWithSasAsync(Stream stream, bool initiator, CancellationToken ct)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] mine = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var lengthPrefix = new byte[] { (byte)(mine.Length >> 8), (byte)mine.Length };
        await stream.WriteAsync(lengthPrefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(mine, ct).ConfigureAwait(false);

        var header = new byte[2];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int length = (header[0] << 8) | header[1];
        if (length is < 32 or > 512) throw new InvalidDataException("Bad key.");
        var theirs = new byte[length];
        await stream.ReadExactlyAsync(theirs, ct).ConfigureAwait(false);

        ECDiffieHellman other;
        try { other = SecurityGuards.ImportP256PublicKey(theirs); }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("Bad key.", ex);
        }
        using (other)
        {
            byte[] transcript = SHA256.HashData(initiator ? Concat(mine, theirs) : Concat(theirs, mine));
            byte[] key = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256, Encoding.ASCII.GetBytes("WinNotch LAN v1"), transcript);
            string sas = ComputeSas(transcript);
            return (key, sas);
        }
    }

    internal static string ComputeSas(byte[] transcript)
    {
        byte[] label = "Holm LAN SAS v1"u8.ToArray();
        byte[] input = new byte[transcript.Length + label.Length];
        transcript.CopyTo(input, 0);
        label.CopyTo(input, transcript.Length);
        byte[] hash = SHA256.HashData(input);
        uint code = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    static byte[] Concat(byte[] a, byte[] b)
    {
        var all = new byte[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }
}
