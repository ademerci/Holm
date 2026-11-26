using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using WinNotch.Sharing;
using Xunit;

namespace Holm.Tests;

/// <summary>The phone sends to this computer: the page, the script and the uploads, over a real local socket.</summary>
public class PhoneReceiveTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "holm-phone-" + Guid.NewGuid().ToString("N"));
    readonly PhoneShare _share;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    const string Phone = "a1b2c3d4e5f60718", Stranger = "ffffeeeeddddcccc";
    readonly System.Collections.Generic.List<PhoneRequest> _asked = new();
    bool _yes = true;

    public PhoneReceiveTests()
    {
        _share = PhoneShare.StartReceiving(IPAddress.Loopback, _folder);
        _share.Ask = request => { lock (_asked) _asked.Add(request); return Task.FromResult(_yes); };
    }

    public void Dispose()
    {
        _share.Dispose();
        _http.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    async Task<HttpStatusCode> Ask(string session = Phone, int count = 1, long size = 1, string name = "photo.jpg")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _share.Url + "ask");
        request.Headers.Add("X-Session", session);
        request.Headers.Add("X-Name", Uri.EscapeDataString(name));
        request.Headers.Add("X-Count", count.ToString());
        request.Headers.Add("X-Size", size.ToString());
        return (await _http.SendAsync(request)).StatusCode;
    }

    Task<HttpResponseMessage> Upload(string name, byte[] body, string? url = null, string session = Phone)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url ?? _share.Url + "u") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Name", Uri.EscapeDataString(name));
        request.Headers.Add("X-Session", session);
        return _http.SendAsync(request);
    }

    bool NothingSaved => !Directory.Exists(_folder) || !Directory.EnumerateFiles(_folder).Any();

    [Fact]
    public async Task Nothing_is_saved_before_the_computer_says_yes()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload("sneaky.jpg", "x"u8.ToArray())).StatusCode);
        Assert.True(NothingSaved);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task The_computer_is_asked_once_with_what_comes_and_a_no_keeps_everything_out()
    {
        _yes = false;
        Assert.Equal(HttpStatusCode.Forbidden, await Ask(count: 5, size: 12_000_000, name: "IMG_0001.HEIC"));
        Assert.Equal(HttpStatusCode.Forbidden, await Ask(count: 5, size: 12_000_000, name: "IMG_0001.HEIC")); // no second question
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload("IMG_0001.HEIC", "x"u8.ToArray())).StatusCode);
        var asked = Assert.Single(_asked);
        Assert.Equal(("IMG_0001.HEIC", 5, 12_000_000L), (asked.FirstName, asked.Count, asked.Bytes));
        Assert.True(NothingSaved);
    }

    [Fact]
    public async Task A_yes_is_for_that_phone_only_and_someone_else_with_the_code_is_asked_again()
    {
        Assert.Equal(HttpStatusCode.OK, await Ask(Phone));
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload("x.jpg", "x"u8.ToArray(), session: Stranger)).StatusCode);
        _yes = false;
        Assert.Equal(HttpStatusCode.Forbidden, await Ask(Stranger));
        Assert.Equal(2, _asked.Count);
        Assert.Equal(HttpStatusCode.OK, (await Upload("mine.jpg", "x"u8.ToArray())).StatusCode);
        Assert.True(File.Exists(Path.Combine(_folder, "mine.jpg")));
        Assert.Single(Directory.EnumerateFiles(_folder));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1", "iPhone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15", "iPad")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0 Mobile Safari/537.36", "Android phone (SM-S911B)")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36", "Android phone")]
    [InlineData("Mozilla/5.0 (Android 14; Mobile; rv:132.0) Gecko/132.0 Firefox/132.0", "Android phone")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X710) AppleWebKit/537.36 Chrome/120.0 Safari/537.36", "Android tablet (SM-X710)")]
    [InlineData(null, "A phone")]
    public void The_question_says_what_kind_of_device_asks(string? userAgent, string device)
        => Assert.Equal(device, PhoneShare.DeviceName(userAgent));

    [Fact]
    public async Task The_question_names_the_device()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _share.Url + "ask");
        request.Headers.Add("X-Session", Phone);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) Mobile/15E148 Safari/604.1");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(request)).StatusCode);
        Assert.Equal("iPhone", Assert.Single(_asked).Device);
    }

    [Fact]
    public async Task Without_an_answer_to_give_nothing_is_accepted()
    {
        _share.Ask = null;
        Assert.Equal(HttpStatusCode.Forbidden, await Ask());
        Assert.Equal(HttpStatusCode.Forbidden, await Ask("short"));
    }

    [Fact]
    public async Task The_page_offers_a_file_picker_and_its_script()
    {
        Assert.True(_share.IsReceiving);
        string page = await _http.GetStringAsync(_share.Url);
        Assert.Contains("type=\"file\" multiple", page);
        Assert.Contains("/app.js", page);
        Assert.Contains("XMLHttpRequest", await _http.GetStringAsync(_share.Url + "app.js"));
    }

    [Fact]
    public async Task A_photo_arrives_whole_under_its_own_name_and_never_overwrites()
    {
        var bytes = new byte[700_000];
        new Random(7).NextBytes(bytes);
        var second = "second"u8.ToArray();
        string? received = null;
        _share.Received += path => received = path;
        Assert.Equal(HttpStatusCode.OK, await Ask(count: 2, size: bytes.Length + second.Length, name: "Ferien äöü.jpg"));

        Assert.Equal(HttpStatusCode.OK, (await Upload("Ferien äöü.jpg", bytes)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload("Ferien äöü.jpg", second)).StatusCode);

        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_folder, "Ferien äöü.jpg")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_folder, "Ferien äöü (1).jpg")));
        Assert.Equal(2, _share.ReceivedFiles.Count);
        Assert.Equal(Path.Combine(_folder, "Ferien äöü (1).jpg"), received);
    }

    [Fact]
    public async Task A_name_cannot_leave_the_folder()
    {
        Assert.Equal(HttpStatusCode.OK, await Ask());
        Assert.Equal(HttpStatusCode.OK, (await Upload("../../evil.txt", "x"u8.ToArray())).StatusCode);
        Assert.True(File.Exists(Path.Combine(_folder, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_folder, "..", "evil.txt")));
    }

    [Fact]
    public async Task Without_the_secret_part_of_the_address_nothing_is_saved()
    {
        var url = new Uri(_share.Url);
        var response = await Upload("x.txt", "x"u8.ToArray(), $"http://{url.Authority}/wrongtoken/u");
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(_share.Url + "u")).StatusCode);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public async Task A_file_cut_off_halfway_is_not_kept()
    {
        Assert.Equal(HttpStatusCode.OK, await Ask(count: 1, size: 100_000, name: "half.bin"));
        var url = new Uri(_share.Url);
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, url.Port);
            var stream = client.GetStream();
            string head = $"POST {url.AbsolutePath}u HTTP/1.1\r\nHost: {url.Authority}\r\nX-Name: half.bin\r\nX-Session: {Phone}\r\nContent-Length: 100000\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(new byte[5000]);
            await stream.FlushAsync();
            // The file is being written while the bytes come.
            for (int i = 0; i < 50 && NothingSaved; i++) await Task.Delay(50);
            Assert.False(NothingSaved);
        } // the phone went away
        for (int i = 0; i < 50 && Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any(); i++) await Task.Delay(100);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
        Assert.Empty(_share.ReceivedFiles);
    }

    [Fact]
    public void A_link_that_offers_files_takes_none()
    {
        Assert.Equal(PhoneShare.RouteKind.NotFound, PhoneShare.Route("/tok/u", "tok", 0).Kind);
        Assert.Equal(PhoneShare.RouteKind.Upload, PhoneShare.Route("/tok/u", "tok", 0, receiving: true).Kind);
        Assert.Equal(PhoneShare.RouteKind.Script, PhoneShare.Route("/tok/app.js", "tok", 0, receiving: true).Kind);
        Assert.Equal(PhoneShare.RouteKind.Ask, PhoneShare.Route("/tok/ask", "tok", 0, receiving: true).Kind);
    }
}
