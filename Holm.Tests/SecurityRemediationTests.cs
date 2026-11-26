using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform.Linux;
using WinNotch;
using WinNotch.Sharing;
using Xunit;

namespace WinNotch.Tests;

/// <summary>
/// Targeted unit and integration tests for the 2026-11-25 security audit remediations
/// (HOLM-SEC-001 – HOLM-SEC-034) and legacy Android-removal upgrade safety.
/// </summary>
public class SecurityRemediationTests
{
    // ───────────── SecurityGuards.IsPublicIp (HOLM-SEC-008, 011, 026) ─────────────

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("172.15.255.255", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.63.255.255", true)]
    [InlineData("100.128.0.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("::ffff:1.1.1.1", true)]
    // Loopback
    [InlineData("127.0.0.1", false)]
    [InlineData("127.255.255.254", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    // RFC1918 private
    [InlineData("10.0.0.1", false)]
    [InlineData("10.255.255.255", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.254", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("192.168.255.254", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:172.16.0.1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    // Link-local / cloud metadata
    [InlineData("169.254.169.254", false)]
    [InlineData("169.254.0.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    // CGNAT (100.64.0.0/10)
    [InlineData("100.64.0.1", false)]
    [InlineData("100.100.100.100", false)]
    [InlineData("100.127.255.254", false)]
    // IPv6 ULA (fc00::/7)
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456:789a::1", false)]
    // Unspecified, multicast, documentation, benchmark, 6to4, Teredo
    [InlineData("0.0.0.0", false)]
    [InlineData("::", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("ff02::1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001::1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    public void IsPublicIp_classifies_addresses_strictly(string ipText, bool expected)
    {
        Assert.Equal(expected, SecurityGuards.IsPublicIp(IPAddress.Parse(ipText)));
    }

    [Fact]
    public void IsPublicIp_rejects_null() => Assert.False(SecurityGuards.IsPublicIp(null));

    // ───────────── SecurityGuards.IsDangerousOpenExtension (HOLM-SEC-025) ─────────────

    [Theory]
    [InlineData("payload.exe", true)]
    [InlineData("PAYLOAD.EXE", true)]
    [InlineData("library.dll", true)]
    [InlineData("run.bat", true)]
    [InlineData("run.cmd", true)]
    [InlineData("script.ps1", true)]
    [InlineData("script.vbs", true)]
    [InlineData("script.js", true)]
    [InlineData("script.jse", true)]
    [InlineData("script.wsf", true)]
    [InlineData("script.wsh", true)]
    [InlineData("app.hta", true)]
    [InlineData("shortcut.lnk", true)]
    [InlineData("site.url", true)]
    [InlineData("launcher.desktop", true)]
    [InlineData("install.sh", true)]
    [InlineData("Holm.AppImage", true)]
    [InlineData("setup.msi", true)]
    [InlineData("screen.scr", true)]
    [InlineData("archive.jar", true)]
    [InlineData("tool.py", true)]
    [InlineData("disk.iso", true)]
    [InlineData("evil.exe. ", true)] // trailing dot/space trick
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    [InlineData("photo.png", false)]
    [InlineData("vacation.jpg", false)]
    [InlineData("document.pdf", false)]
    [InlineData("notes.txt", false)]
    [InlineData("clip.mp4", false)]
    [InlineData("bundle.zip", false)]
    [InlineData("README", false)]
    public void IsDangerousOpenExtension_blocks_executables_and_allows_safe_files(string? fileName, bool dangerous)
    {
        Assert.Equal(dangerous, SecurityGuards.IsDangerousOpenExtension(fileName));
    }

    // ───────────── Strict NIST P-256 SPKI validation (HOLM-SEC-032) ─────────────

    // Valid DER-encoded secp256k1 SubjectPublicKeyInfo (OID 1.3.132.0.10, generator point G)
    static readonly byte[] Secp256k1Spki = Convert.FromHexString(
        "3056301006072A8648CE3D020106052B8104000A03420004" +
        "79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798" +
        "483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8");

    [Fact]
    public void P256_spki_validation_accepts_nist_p256_and_rejects_other_curves_and_garbage()
    {
        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] valid = p256.ExportSubjectPublicKeyInfo();
        Assert.True(SecurityGuards.IsP256Spki(valid));
        using (var imported = SecurityGuards.ImportP256PublicKey(valid))
            Assert.Equal(256, imported.KeySize);

        // Trailing bytes after valid SPKI must be rejected
        byte[] trailing = valid.Concat(new byte[] { 0x00 }).ToArray();
        Assert.False(SecurityGuards.IsP256Spki(trailing));
        Assert.Throws<CryptographicException>(() => SecurityGuards.ImportP256PublicKey(trailing));

        // P-384 and P-521 must be rejected
        using var p384 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        byte[] p384Spki = p384.ExportSubjectPublicKeyInfo();
        Assert.False(SecurityGuards.IsP256Spki(p384Spki));
        Assert.Throws<CryptographicException>(() => SecurityGuards.ImportP256PublicKey(p384Spki));

        using var p521 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
        byte[] p521Spki = p521.ExportSubjectPublicKeyInfo();
        Assert.False(SecurityGuards.IsP256Spki(p521Spki));
        Assert.Throws<CryptographicException>(() => SecurityGuards.ImportP256PublicKey(p521Spki));

        // secp256k1 (256-bit non-NIST curve) must be rejected cleanly
        Assert.False(SecurityGuards.IsP256Spki(Secp256k1Spki));
        Assert.ThrowsAny<Exception>(() => SecurityGuards.ImportP256PublicKey(Secp256k1Spki));

        // RSA SPKI and arbitrary garbage
        using var rsa = RSA.Create(2048);
        Assert.False(SecurityGuards.IsP256Spki(rsa.ExportSubjectPublicKeyInfo()));
        Assert.False(SecurityGuards.IsP256Spki(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.False(SecurityGuards.IsP256Spki(ReadOnlySpan<byte>.Empty));

        // FriendNotes.Open must return null (not crash with ArgumentException) when sender presents P-384 / secp256k1 / garbage
        using var lena = FriendIdentity.Create("Lena", null);
        string fakeCipher = FriendIdentity.Base64Url(RandomNumberGenerator.GetBytes(64));
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Prefix + FriendIdentity.Base64Url(p384Spki) + " " + fakeCipher, out _));
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Prefix + FriendIdentity.Base64Url(Secp256k1Spki) + " " + fakeCipher, out _));
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Prefix + FriendIdentity.Base64Url(new byte[] { 1, 2, 3 }) + " " + fakeCipher, out _));
    }

    // ───────────── ChatGif.Clean & FromTyped (HOLM-SEC-008) ─────────────

    [Theory]
    [InlineData("https://media.giphy.com/media/abc12345/giphy.gif", true)]
    [InlineData("https://media0.giphy.com/media/abc12345/giphy.gif", true)]
    [InlineData("https://media4.giphy.com/media/abc12345/giphy.gif", true)]
    [InlineData("https://i.giphy.com/abc12345.gif", true)]
    [InlineData("https://media.tenor.com/xyz/cat.gif", true)]
    [InlineData("https://media1.tenor.com/m/xyz/cat.gif", true)]
    [InlineData("https://c.tenor.com/xyz/cat.gif", true)]
    // Rejected: non-allowlisted domains, HTTP, non-default port, userinfo, IP literals, localhost
    [InlineData("http://media.giphy.com/media/abc12345/giphy.gif", false)]
    [InlineData("https://evil.example.com/track.gif", false)]
    [InlineData("https://media.giphy.com.evil.example/a.gif", false)]
    [InlineData("https://media.giphy.com:8443/media/abc12345/giphy.gif", false)]
    [InlineData("https://user:pass@media.giphy.com/media/abc12345/giphy.gif", false)]
    [InlineData("https://127.0.0.1/a.gif", false)]
    [InlineData("https://1.1.1.1/a.gif", false)]
    [InlineData("https://[::1]/a.gif", false)]
    [InlineData("https://localhost/a.gif", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ChatGif_Clean_enforces_https_and_trusted_cdn_hosts(string? url, bool allowed)
    {
        if (allowed) Assert.NotNull(ChatGif.Clean(url));
        else Assert.Null(ChatGif.Clean(url));
    }

    [Fact]
    public void ChatGif_FromTyped_rewrites_giphy_pages_and_rejects_arbitrary_hosts()
    {
        Assert.Equal("https://media.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif",
            ChatGif.FromTyped("https://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S"));
        Assert.Null(ChatGif.FromTyped("https://attacker.example/pixel.gif"));
        Assert.Null(ChatGif.FromTyped("http://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S"));
    }

    // ───────────── FriendIdentity.FromCard / ParseFriendCode (HOLM-SEC-011, 032) ─────────────

    static string BuildCard(string name, string topic, string keyBase64Url, string? server)
    {
        var payload = new Dictionary<string, string?> { ["n"] = name, ["t"] = topic, ["k"] = keyBase64Url };
        if (server != null) payload["s"] = server;
        return "WNF1." + FriendIdentity.Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    [Fact]
    public void FriendIdentity_ParseFriendCode_rejects_unsafe_urls_and_non_p256_keys()
    {
        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        string validKey = FriendIdentity.Base64Url(p256.ExportSubjectPublicKeyInfo());

        // Valid public HTTPS server succeeds
        var ok = FriendIdentity.ParseFriendCode(BuildCard("Lena", "wnf-abcdef123456", validKey, "https://relay.holmnotch.ch"));
        Assert.NotNull(ok);
        Assert.Equal("https://relay.holmnotch.ch", ok!.Server);

        // Reject HTTP (non-loopback), private IPs, link-local, userinfo, query, fragment, .local/.internal
        string[] badServers =
        {
            "http://relay.holmnotch.ch",
            "http://192.168.1.5:8080",
            "https://192.168.1.5",
            "https://10.0.0.1",
            "https://172.16.0.2",
            "https://169.254.169.254",
            "https://100.64.0.1",
            "https://[fc00::1]",
            "https://user:pass@relay.holmnotch.ch",
            "https://relay.holmnotch.ch?foo=bar",
            "https://relay.holmnotch.ch#frag",
            "https://mybox.local",
            "https://service.internal",
            "https://intranet",
            "ftp://relay.holmnotch.ch",
        };
        foreach (string bad in badServers)
            Assert.Null(FriendIdentity.ParseFriendCode(BuildCard("Lena", "wnf-abcdef123456", validKey, bad)));

        // Non-production loopback check: IsSafePublicHttpsUri without test override rejects loopback HTTP & HTTPS
        Assert.False(SecurityGuards.IsSafePublicHttpsUri("http://127.0.0.1:8080", allowLoopbackWhenTesting: false));
        Assert.False(SecurityGuards.IsSafePublicHttpsUri("http://localhost:8080", allowLoopbackWhenTesting: false));
        Assert.False(SecurityGuards.IsSafePublicHttpsUri("https://127.0.0.1", allowLoopbackWhenTesting: false));
        Assert.False(SecurityGuards.IsSafePublicHttpsUri("https://localhost", allowLoopbackWhenTesting: false));

        // Reject non-P-256 keys in card
        using var p384 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(FriendIdentity.ParseFriendCode(BuildCard("Lena", "wnf-abcdef123456", FriendIdentity.Base64Url(p384.ExportSubjectPublicKeyInfo()), null)));
        Assert.Null(FriendIdentity.ParseFriendCode(BuildCard("Lena", "wnf-abcdef123456", FriendIdentity.Base64Url(Secp256k1Spki), null)));
    }

    // ───────────── FileTransfer.ParseOffer int64 overflow check (HOLM-SEC-019) ─────────────

    [Fact]
    public async Task FileTransfer_ParseOffer_rejects_int64_overflow_and_negative_sizes()
    {
        using var target = new TempFolder();
        var (evil, receiver) = await Net.ChannelPairAsync();
        await using (evil)
        await using (receiver)
        {
            var ct = Net.Timeout();
            bool decideCalled = false;
            var receive = FileTransfer.ReceiveAsync(receiver, _ => { decideCalled = true; return Task.FromResult(true); }, target.Path, null, ct);

            // Two positive sizes whose sum overflows signed Int64 to a negative/small number
            var json = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Sender = "Mallory",
                Files = new[]
                {
                    new { Name = "a.bin", Size = long.MaxValue - 10 },
                    new { Name = "b.bin", Size = 100L },
                },
            });
            byte[] frame = new byte[] { 1 }.Concat(json).ToArray();
            await evil.SendAsync(frame, ct);

            await Assert.ThrowsAsync<InvalidDataException>(() => receive);
            Assert.False(decideCalled);
        }

        // Also test near-MaxTotalBytes boundary overflow without overflowing Int64
        var (evil2, receiver2) = await Net.ChannelPairAsync();
        await using (evil2)
        await using (receiver2)
        {
            var ct = Net.Timeout();
            var receive = FileTransfer.ReceiveAsync(receiver2, _ => Task.FromResult(true), target.Path, null, ct);
            var json = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Sender = "Mallory",
                Files = new[]
                {
                    new { Name = "a.bin", Size = FileTransfer.MaxTotalBytes },
                    new { Name = "b.bin", Size = 1L },
                },
            });
            await evil2.SendAsync(new byte[] { 1 }.Concat(json).ToArray(), ct);
            await Assert.ThrowsAsync<InvalidDataException>(() => receive);
        }
    }

    // ───────────── PhoneShare quota, IP pinning & per-IP cap (HOLM-SEC-009, 029) ─────────────

    [Fact]
    public async Task PhoneShare_enforces_session_file_count_and_byte_quotas()
    {
        using var inbox = new TempFolder();
        using var share = PhoneShare.StartReceiving(IPAddress.Loopback, inbox.Path);
        share.Ask = _ => Task.FromResult(true);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        static HttpRequestMessage MakeAsk(string url, string session, int count, long size)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, url + "ask");
            r.Headers.Add("X-Session", session);
            r.Headers.Add("X-Name", "a.txt");
            r.Headers.Add("X-Count", count.ToString());
            r.Headers.Add("X-Size", size.ToString());
            return r;
        }
        static HttpRequestMessage MakeUpload(string url, string session, string name, byte[] body)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, url + "u") { Content = new ByteArrayContent(body) };
            r.Headers.Add("X-Session", session);
            r.Headers.Add("X-Name", name);
            return r;
        }

        // Session 1: approved for 1 file of 10 bytes — 11 bytes rejected, 10 bytes accepted, then session retired
        string s1 = "1111222233334444";
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(MakeAsk(share.Url, s1, 1, 10))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(MakeUpload(share.Url, s1, "big.txt", new byte[11]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(MakeUpload(share.Url, s1, "ok.txt", new byte[10]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(MakeUpload(share.Url, s1, "extra.txt", new byte[1]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(MakeAsk(share.Url, s1, 1, 10))).StatusCode); // retired

        // Session 2: approved for 2 files totaling 10 bytes — 7 + 4 rejected (exceeds remaining 3), 7 + 3 accepted, 3rd rejected
        string s2 = "5555666677778888";
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(MakeAsk(share.Url, s2, 2, 10))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(MakeUpload(share.Url, s2, "part1.txt", new byte[7]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(MakeUpload(share.Url, s2, "too-big.txt", new byte[4]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(MakeUpload(share.Url, s2, "part2.txt", new byte[3]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(MakeUpload(share.Url, s2, "part3.txt", new byte[0]))).StatusCode);
    }

    [Fact]
    public async Task PhoneShare_pins_session_to_client_ip_and_caps_connections_per_ip()
    {
        using var inbox = new TempFolder();
        using var share = PhoneShare.StartReceiving(IPAddress.Loopback, inbox.Path);
        share.Ask = _ => Task.FromResult(true);
        var uri = new Uri(share.Url);

        static async Task<string> RawPostAsync(IPAddress bindIp, int port, string requestText)
        {
            using var client = new TcpClient(new IPEndPoint(bindIp, 0));
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(requestText));
            await stream.FlushAsync();
            var buf = new byte[512];
            int n = await stream.ReadAsync(buf);
            return Encoding.ASCII.GetString(buf, 0, n);
        }

        // Approve session from 127.0.0.1
        string session = "aaaabbbbccccdddd";
        string askResp = await RawPostAsync(IPAddress.Loopback, uri.Port,
            $"POST {uri.AbsolutePath}ask HTTP/1.1\r\nHost: {uri.Authority}\r\nX-Session: {session}\r\nX-Name: a.txt\r\nX-Count: 1\r\nX-Size: 4\r\nContent-Length: 0\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 200", askResp);

        // Different source IP (127.0.0.2) trying to hijack the approved session must get 403 Forbidden
        var otherLoopback = IPAddress.Parse("127.0.0.2");
        string hijackResp = await RawPostAsync(otherLoopback, uri.Port,
            $"POST {uri.AbsolutePath}u HTTP/1.1\r\nHost: {uri.Authority}\r\nX-Session: {session}\r\nX-Name: hijacked.txt\r\nContent-Length: 4\r\n\r\nevil");
        Assert.StartsWith("HTTP/1.1 403", hijackResp);
        Assert.Empty(share.ReceivedFiles);

        // Per-IP cap (MaxClientsPerIp = 4): hold 4 idle sockets from 127.0.0.1; 5th from 127.0.0.1 is rejected while 127.0.0.2 still gets 200
        var held = new List<TcpClient>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var c = new TcpClient(new IPEndPoint(IPAddress.Loopback, 0));
                await c.ConnectAsync(IPAddress.Loopback, uri.Port);
                held.Add(c);
            }
            await Task.Delay(150);

            using (var fifth = new TcpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                await fifth.ConnectAsync(IPAddress.Loopback, uri.Port);
                var s = fifth.GetStream();
                await s.WriteAsync(Encoding.ASCII.GetBytes($"GET {uri.AbsolutePath} HTTP/1.1\r\nHost: {uri.Authority}\r\n\r\n"));
                var buf = new byte[64];
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                int read = 0;
                try { read = await s.ReadAsync(buf, readCts.Token); }
                catch (IOException) { read = 0; }
                Assert.Equal(0, read); // closed without HTTP response
            }

            string otherResp = await RawPostAsync(otherLoopback, uri.Port,
                $"GET {uri.AbsolutePath} HTTP/1.1\r\nHost: {uri.Authority}\r\n\r\n");
            Assert.StartsWith("HTTP/1.1 200", otherResp);
        }
        finally
        {
            foreach (var c in held) c.Dispose();
        }
    }

    // ───────────── SecureChannel concurrent SendAsync serialization (HOLM-SEC-031) ─────────────

    [Fact]
    public async Task SecureChannel_serializes_concurrent_sends_without_nonce_reuse()
    {
        var (sender, receiver) = await Net.ChannelPairAsync();
        await using (sender)
        await using (receiver)
        {
            const int count = 64;
            var ct = Net.Timeout(15);
            var received = new List<string>(count);
            var recvTask = Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                    received.Add(Encoding.UTF8.GetString(await receiver.ReceiveAsync(ct)));
            }, ct);

            var expected = Enumerable.Range(0, count).Select(i => $"msg-{i:D3}-" + new string('x', 128)).ToArray();
            await Task.WhenAll(expected.Select(m => Task.Run(() => sender.SendAsync(Encoding.UTF8.GetBytes(m), ct), ct)));
            await recvTask;

            Assert.Equal(expected.OrderBy(s => s), received.OrderBy(s => s));
        }
    }

    // ───────────── Legacy upgrade safety & Linux security helpers ─────────────

    [Fact]
    public void Legacy_settings_with_unknown_fields_and_phone_tool_deserialize_cleanly()
    {
        const string legacyJson = """
            {
                "Beam": true,
                "UnknownLegacyPhoneSetting": true,
                "PhoneDeviceId": "old-android-123",
                "HomeTools": ["phone", "notes", "beam"],
                "HomeHiddenTools": ["phone"]
            }
            """;
        var settings = JsonSerializer.Deserialize<AppSettings>(legacyJson)!;
        Assert.True(settings.Beam);
        var layout = HomeLayout.Read(settings);
        Assert.DoesNotContain("phone", layout.ToolOrder);
        Assert.DoesNotContain("phone", layout.HiddenTools);
        Assert.Equal("notes", layout.ToolOrder[0]);
    }

    [Theory]
    [InlineData("discord", true)]
    [InlineData("org.telegram.desktop", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("bad\0entry", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LinuxNotifications_IsSafeDesktopEntry_blocks_path_traversal(string? entry, bool expected)
    {
        Assert.Equal(expected, LinuxNotificationsPlatform.IsSafeDesktopEntry(entry));
    }

    [Theory]
    [InlineData("http://example.com/cover.jpg")]
    [InlineData("https://127.0.0.1/cover.jpg")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://user:pass@example.com/cover.jpg")]
    public async Task LinuxMedia_LoadCoverAsync_rejects_unsafe_remote_urls(string url)
    {
        Assert.Null(await LinuxMediaPlatform.LoadCoverAsync(url));
    }

    [Fact]
    public async Task LinuxMedia_LoadCoverAsync_rejects_oversized_data_uri()
    {
        string huge = "data:image/png;base64," + new string('A', 12 * 1024 * 1024);
        Assert.Null(await LinuxMediaPlatform.LoadCoverAsync(huge));
    }
}

/// <summary>
/// FriendsService invite redemption, backdated expiry, revocation, and replay protection tests
/// using the shared FakeInboxServer collection (HOLM-SEC-006, 028).
/// </summary>
[Collection("Inbox servers")]
public sealed class InviteAndReplaySecurityTests : IDisposable
{
    readonly FakeInboxServer _relay = new(), _ntfy = new();
    readonly string _wasDefault = FriendIdentity.DefaultServer, _wasRelay = FriendIdentity.RelayServer;

    public InviteAndReplaySecurityTests()
    {
        FriendIdentity.DefaultServer = _ntfy.Url;
        FriendIdentity.RelayServer = _relay.Url;
        FriendsService.ProtectKey = (data, _) => data.ToArray();
        FriendsService.UnprotectKey = (data, _) => data.ToArray();
    }

    public void Dispose()
    {
        FriendIdentity.DefaultServer = _wasDefault;
        FriendIdentity.RelayServer = _wasRelay;
        _relay.Dispose();
        _ntfy.Dispose();
    }

    static Task Ui(Action a)
    {
        lock (typeof(InviteAndReplaySecurityTests)) a();
        return Task.CompletedTask;
    }

    static async Task Until(Func<bool> done, string what)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(50);
        Assert.True(done(), what);
    }

    [Fact]
    public async Task Invite_is_single_use_and_revocation_prevents_auto_accept()
    {
        var aliceSettings = new AppSettings { Beam = true, ProfileName = "Alice", FriendsServer = _relay.Url };
        var alice = new FriendsService(aliceSettings, Ui);
        var autoAccepted = new ConcurrentQueue<Friend>();
        var manualRequested = new ConcurrentQueue<Friend>();
        alice.InviteAccepted += f => autoAccepted.Enqueue(f);
        alice.FriendRequested += (f, _) => manualRequested.Enqueue(f);

        using var bob = FriendIdentity.Create("Bob", _relay.Url);
        using var carol = FriendIdentity.Create("Carol", _relay.Url);
        using var dave = FriendIdentity.Create("Dave", _relay.Url);
        try
        {
            var aliceCard = alice.EnsureMe().AsFriend();
            await Until(() => _relay.Followers >= 1, "Alice connected");

            var invite = await alice.InviteAsync(fresh: true, Net.Timeout());
            Assert.Equal(16, FriendInvite.Length);
            Assert.Matches("^HOLM-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", invite.Code);

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Bob redeems the invite -> auto-accepted & consumed
            string bobReq = FriendNotes.Seal(bob, aliceCard,
                new FriendNote(FriendNoteKind.FriendRequest, "Bob", now, Code: bob.FriendCode,
                               RequestId: Guid.NewGuid().ToString("N"), Invite: invite.Proof));
            await FriendInbox.PostAsync(aliceCard, bobReq, Net.Timeout());
            await Until(() => autoAccepted.Count == 1, "Bob auto-accepted via invite");
            Assert.Empty(aliceSettings.FriendInvites); // consumed (single-use)

            // Carol tries to reuse the same proof -> NOT auto-accepted, falls back to manual FriendRequested
            string carolReq = FriendNotes.Seal(carol, aliceCard,
                new FriendNote(FriendNoteKind.FriendRequest, "Carol", now, Code: carol.FriendCode,
                               RequestId: Guid.NewGuid().ToString("N"), Invite: invite.Proof));
            await FriendInbox.PostAsync(aliceCard, carolReq, Net.Timeout());
            await Until(() => manualRequested.Count == 1, "Carol requires manual approval");
            Assert.Single(autoAccepted);
            Assert.Equal(carol.PublicKey, manualRequested.Single().Key);

            // Second invite explicitly revoked before Dave uses it -> manual approval required
            var invite2 = await alice.InviteAsync(fresh: true, Net.Timeout());
            Assert.True(alice.RevokeInvite(invite2.Code));
            Assert.Empty(alice.Invites);
            string daveReq = FriendNotes.Seal(dave, aliceCard,
                new FriendNote(FriendNoteKind.FriendRequest, "Dave", now, Code: dave.FriendCode,
                               RequestId: Guid.NewGuid().ToString("N"), Invite: invite2.Proof));
            await FriendInbox.PostAsync(aliceCard, daveReq, Net.Timeout());
            await Until(() => manualRequested.Count == 2, "Dave requires manual approval after revocation");
            Assert.Single(autoAccepted);
        }
        finally { alice.Stop(); }
    }

    [Fact]
    public async Task Backdated_dto_T_cannot_bypass_expired_invite_and_replayed_notes_are_dropped()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string expiredCode = FriendInvite.New();
        long expiredAt = now - 3600; // expired 1 hour ago, still within 24h InviteGrace

        var aliceSettings = new AppSettings { Beam = true, ProfileName = "Alice", FriendsServer = _relay.Url };
        aliceSettings.FriendInvites.Add(new OwnInvite
        {
            Code = expiredCode,
            Proof = FriendInvite.Proof(expiredCode),
            Expires = expiredAt,
        });

        using var mallory = FriendIdentity.Create("Mallory", _relay.Url);
        using var friend = FriendIdentity.Create("Noah", _relay.Url);
        aliceSettings.Friends.Add(friend.AsFriend());

        var alice = new FriendsService(aliceSettings, Ui);
        var autoAccepted = new ConcurrentQueue<Friend>();
        var manualRequested = new ConcurrentQueue<Friend>();
        var chats = new ConcurrentQueue<string>();
        alice.InviteAccepted += f => autoAccepted.Enqueue(f);
        alice.FriendRequested += (f, _) => manualRequested.Enqueue(f);
        alice.ChatReceived += (_, _, text, _) => chats.Enqueue(text);
        try
        {
            var aliceCard = alice.EnsureMe().AsFriend();
            await Until(() => _relay.Followers >= 1, "Alice connected");

            // Mallory backdates dto.T to before expiredAt — must NOT auto-accept because arrival > Expires + 300
            string forgedReq = FriendNotes.Seal(mallory, aliceCard,
                new FriendNote(FriendNoteKind.FriendRequest, "Mallory", expiredAt - 60, Code: mallory.FriendCode,
                               RequestId: Guid.NewGuid().ToString("N"), Invite: FriendInvite.Proof(expiredCode)));
            await FriendInbox.PostAsync(aliceCard, forgedReq, Net.Timeout());
            await Until(() => manualRequested.Count == 1, "Expired invite request falls back to manual prompt");
            Assert.Empty(autoAccepted);

            // Replay protection (HOLM-SEC-006): posting the exact same sealed note a second time (new server event id) is ignored
            string sealedChat = FriendNotes.Seal(friend, aliceCard,
                new FriendNote(FriendNoteKind.Chat, "Noah", now, Text: "hello once", MessageId: Guid.NewGuid().ToString("N")));
            await FriendInbox.PostAsync(aliceCard, sealedChat, Net.Timeout());
            await Until(() => chats.Count == 1, "First chat delivery received");

            await FriendInbox.PostAsync(aliceCard, sealedChat, Net.Timeout()); // replayed with fresh server id
            await Task.Delay(400);
            Assert.Single(chats);
        }
        finally { alice.Stop(); }
    }
}
