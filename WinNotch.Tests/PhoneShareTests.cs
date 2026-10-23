using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class PhoneShareTests
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task The_phone_gets_a_page_and_the_file()
    {
        using var folder = new TempFolder();
        byte[] content = Enumerable.Range(0, 700_000).Select(i => (byte)(i * 7)).ToArray();
        string path = folder.File("Holiday photos.zip", content);
        using var share = PhoneShare.Start(new[] { path }, IPAddress.Loopback);
        var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool visited = false;
        share.Visited += () => visited = true;
        share.Delivered += i => delivered.TrySetResult(i);

        Assert.StartsWith("http://127.0.0.1:", share.Url);
        string page = await Http.GetStringAsync(share.Url);
        Assert.Contains("Holiday photos.zip", page);
        Assert.True(visited);

        using var response = await Http.GetAsync(share.Url + "f/0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(share.AllDelivered);
    }

    [Fact]
    public async Task A_video_streams_in_parts()
    {
        using var folder = new TempFolder();
        string path = folder.File("clip.mp4", Encoding.ASCII.GetBytes("0123456789"));
        using var share = PhoneShare.Start(new[] { path }, IPAddress.Loopback);

        var request = new HttpRequestMessage(HttpMethod.Get, share.Url + "v/0");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);
        using var response = await Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
        Assert.False(share.AllDelivered); // a part isn't the whole file
    }

    [Fact]
    public async Task Without_the_token_there_is_nothing()
    {
        using var folder = new TempFolder();
        string path = folder.File("secret.txt", "x"u8.ToArray());
        using var share = PhoneShare.Start(new[] { path }, IPAddress.Loopback);
        var root = new Uri(share.Url).GetLeftPart(UriPartial.Authority);

        foreach (string target in new[] { "/", "/f/0", "/wrongtoken/f/0", "/favicon.ico", share.Url[root.Length..] + "f/1", share.Url[root.Length..] + "f/-1" })
        {
            using var response = await Http.GetAsync(root + target);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var post = await Http.PostAsync(share.Url + "f/0", new StringContent(""));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task Paths_cannot_climb_out()
    {
        using var folder = new TempFolder();
        string path = folder.File("a.txt", "a"u8.ToArray());
        using var share = PhoneShare.Start(new[] { path }, IPAddress.Loopback);
        var uri = new Uri(share.Url);
        // Raw request, so nothing tidies the path first.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, uri.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {uri.AbsolutePath}f/../../a.txt HTTP/1.1\r\nHost: x\r\n\r\n"));
        using var reader = new StreamReader(stream);
        Assert.StartsWith("HTTP/1.1 404", await reader.ReadLineAsync());
    }

    [Fact]
    public async Task It_stops_when_told()
    {
        using var folder = new TempFolder();
        string path = folder.File("a.txt", "a"u8.ToArray());
        var share = PhoneShare.Start(new[] { path }, IPAddress.Loopback);
        string url = share.Url;
        share.Dispose();
        share.Dispose();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Http.GetStringAsync(url));
    }

    [Fact]
    public void Names_are_escaped_in_the_page()
    {
        string page = PhoneShare.Page(new[] { new PhoneFile(@"C:\x", "<script>alert(1)</script>.txt", 5) }, "tok");
        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("href=\"/tok/v/0\"", page); // a text file can be opened
        Assert.Contains("href=\"/tok/f/0\" download", page);
    }

    [Theory]
    [InlineData("/tok/", PhoneShare.RouteKind.Page, -1)]
    [InlineData("/tok", PhoneShare.RouteKind.Page, -1)]
    [InlineData("/tok/?x=1", PhoneShare.RouteKind.Page, -1)]
    [InlineData("/tok/f/1", PhoneShare.RouteKind.Download, 1)]
    [InlineData("/tok/v/0", PhoneShare.RouteKind.View, 0)]
    [InlineData("/tok/f/2", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("/tok/f/+1", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("/tok/f/1/x", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("/TOK/", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("/to/", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("tok/", PhoneShare.RouteKind.NotFound, -1)]
    [InlineData("//tok/", PhoneShare.RouteKind.NotFound, -1)]
    public void Requests_are_routed(string target, PhoneShare.RouteKind kind, int file)
        => Assert.Equal(new PhoneShare.RouteResult(kind, file), PhoneShare.Route(target, "tok", 2));

    [Theory]
    [InlineData("bytes=0-3", 10, true, 0, 3)]
    [InlineData("bytes=4-", 10, true, 4, 9)]
    [InlineData("bytes=-3", 10, true, 7, 9)]
    [InlineData("bytes=5-100", 10, true, 5, 9)]
    [InlineData("bytes=10-", 10, false, 0, 0)]
    [InlineData("bytes=5-2", 10, false, 0, 0)]
    [InlineData("bytes=0-1,4-5", 10, false, 0, 0)]
    [InlineData("items=0-1", 10, false, 0, 0)]
    [InlineData("bytes=-0", 10, false, 0, 0)]
    public void Ranges_are_read(string header, long length, bool ok, long from, long to)
    {
        Assert.Equal(ok, PhoneShare.TryParseRange(header, length, out long f, out long t));
        if (ok) Assert.Equal((from, to), (f, t));
    }

    [Fact]
    public void File_names_survive_the_download_header()
    {
        string header = PhoneShare.Disposition("Fotos \"Sommer\" ä.zip", inline: false);
        Assert.StartsWith("attachment; filename=\"Fotos _Sommer_ _.zip\"", header);
        Assert.EndsWith("filename*=UTF-8''Fotos%20%22Sommer%22%20%C3%A4.zip", header);
    }

    static LanAddress.Candidate Nic(string name, NetworkInterfaceType type, string address, bool gateway = true, bool up = true)
        => new(name, name, type, up, IPAddress.Parse(address), gateway);

    [Fact]
    public void The_wifi_address_is_picked_for_the_phone()
    {
        var pick = LanAddress.Pick(new[]
        {
            Nic("vEthernet (Default Switch)", NetworkInterfaceType.Ethernet, "172.20.0.1"),
            Nic("Ethernet", NetworkInterfaceType.Ethernet, "192.168.1.20"),
            Nic("WLAN", NetworkInterfaceType.Wireless80211, "192.168.1.23"),
            Nic("Loopback", NetworkInterfaceType.Loopback, "127.0.0.1"),
        });
        Assert.Equal(IPAddress.Parse("192.168.1.23"), pick);
    }

    [Fact]
    public void No_address_without_a_real_network()
        => Assert.Null(LanAddress.Pick(new[]
        {
            Nic("WLAN", NetworkInterfaceType.Wireless80211, "169.254.10.4"),
            Nic("Ethernet", NetworkInterfaceType.Ethernet, "192.168.1.20", gateway: false),
            Nic("NordLynx VPN", NetworkInterfaceType.Ethernet, "10.5.0.2"),
            Nic("WLAN 2", NetworkInterfaceType.Wireless80211, "192.168.1.30", up: false),
        }));

    [Fact]
    public void The_qr_code_has_its_quiet_zone_and_finder_patterns()
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode("http://192.168.1.23:52345/kQ7vXrT2mB9sLwE4yHn3Ga/", QRCoder.QRCodeGenerator.ECCLevel.M);
        Assert.False(data.ModuleMatrix[0][0]); // quiet zone
        Assert.True(data.ModuleMatrix[4][4]);  // the finder pattern's corner
        System.Windows.Media.Geometry geometry = null!;
        int modules = 0;
        var thread = new Thread(() => geometry = QrCode.For("http://192.168.1.23:52345/kQ7vXrT2mB9sLwE4yHn3Ga/", out modules));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Equal(data.ModuleMatrix.Count - 8, modules);
        Assert.Equal(0, (modules - 17) % 4);
        Assert.Equal(new System.Windows.Rect(0, 0, modules, modules), geometry.Bounds);
    }

    /// <summary>Draws the code at one pixel per module and compares every module with the generator's matrix.</summary>
    [Fact]
    public void The_drawn_code_matches_the_generated_one_module_for_module()
    {
        const string text = "http://192.168.1.23:52345/kQ7vXrT2mB9sLwE4yHn3Ga/";
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var mismatches = new List<string>();
        var thread = new Thread(() =>
        {
            var geometry = QrCode.For(text, out int modules);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, modules, modules));
                dc.DrawGeometry(System.Windows.Media.Brushes.Black, null, geometry);
            }
            System.Windows.Media.RenderOptions.SetEdgeMode(visual, System.Windows.Media.EdgeMode.Aliased);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(modules, modules, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[modules * modules * 4];
            bitmap.CopyPixels(pixels, modules * 4, 0);
            for (int y = 0; y < modules; y++)
                for (int x = 0; x < modules; x++)
                {
                    bool dark = pixels[(y * modules + x) * 4] < 128;
                    if (dark != data.ModuleMatrix[y + 4][x + 4]) mismatches.Add($"({x},{y})");
                }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Empty(mismatches);
    }
}
