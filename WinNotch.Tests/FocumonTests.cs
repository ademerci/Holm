using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WinNotch.Tests;

public class FocumonTests
{
    static readonly DateTime Now = new(2026, 10, 29, 16, 0, 0, DateTimeKind.Utc);

    static FocumonStatus? Parse(string json) => Focumon.TryParse(json, Now, out _);

    [Fact]
    public void A_countdown_ends_when_its_time_is_up()
    {
        var status = Parse("""{"v":1,"phase":"focus","down":true,"seconds":1500,"monster":"Embrix"}""")!;
        Assert.Equal(FocumonPhase.Focus, status.Phase);
        Assert.Equal("Embrix", status.Monster);
        Assert.Equal(TimeSpan.FromSeconds(1500), status.Clock(Now));
        Assert.Equal(TimeSpan.FromSeconds(1200), status.Clock(Now.AddSeconds(300)));
        Assert.Equal(TimeSpan.Zero, status.Clock(Now.AddHours(1))); // never below zero
    }

    [Fact]
    public void A_count_up_runs_on_by_itself()
    {
        var status = Parse("""{"v":1,"phase":"focus","down":false,"seconds":90}""")!;
        Assert.False(status.CountsDown);
        Assert.Equal(TimeSpan.FromSeconds(90), status.Clock(Now));
        Assert.Equal(TimeSpan.FromSeconds(150), status.Clock(Now.AddSeconds(60)));
    }

    [Fact]
    public void A_break_is_a_break_and_idle_has_no_clock()
    {
        Assert.Equal(FocumonPhase.Break, Parse("""{"v":1,"phase":"break","down":true,"seconds":300}""")!.Phase);
        var idle = Parse("""{"v":1,"phase":"idle","monster":"Embrix"}""")!;
        Assert.Equal(FocumonPhase.Idle, idle.Phase);
        Assert.Null(idle.Clock(Now));
    }

    [Fact]
    public void Closing_the_page_says_gone()
    {
        Assert.Null(Focumon.TryParse("""{"v":1,"phase":"gone"}""", Now, out bool gone));
        Assert.True(gone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"phase":"focus","down":true,"seconds":10}""")]                    // no version
    [InlineData("""{"v":2,"phase":"focus","down":true,"seconds":10}""")]               // a newer extension than Holm
    [InlineData("""{"v":1,"phase":"sleeping"}""")]
    [InlineData("""{"v":1,"phase":"focus","down":true}""")]                           // a clock without seconds
    [InlineData("""{"v":1,"phase":"focus","down":true,"seconds":-5}""")]
    [InlineData("""{"v":1,"phase":"focus","down":true,"seconds":999999999}""")]
    [InlineData("""{"v":1,"phase":"focus","down":true,"seconds":"10"}""")]
    [InlineData("""{"v":1,"phase":"focus","down":true,"seconds":10,"x":[[[[[[1]]]]]]}""")] // nested too deep
    public void Rubbish_is_not_a_status(string json)
    {
        Assert.Null(Focumon.TryParse(json, Now, out bool gone));
        Assert.False(gone);
    }

    [Fact]
    public void A_name_is_made_safe_to_show()
    {
        Assert.Equal("Two Lines Long", Focumon.Clean("  Two\r\nLines\tLong "));
        Assert.Equal("", Focumon.Clean(null));
        Assert.Equal("", Focumon.Clean("  \n "));
        Assert.Equal(40, Focumon.Clean(new string('A', 200)).Length);
    }

    [Theory]
    [InlineData("https://www.focumon.com/assets/monsters/embrix.png", true)]
    [InlineData("https://focumon.com/a.png", true)]
    [InlineData("https://cdn.focumon.com/a.png", true)]
    [InlineData("http://www.focumon.com/a.png", false)]                 // not encrypted
    [InlineData("https://focumon.com.evil.example/a.png", false)]
    [InlineData("https://evilfocumon.com/a.png", false)]
    [InlineData("https://user:pass@www.focumon.com/a.png", false)]
    [InlineData("https://www.focumon.com:8443/a.png", false)]
    [InlineData("file:///C:/secret.png", false)]
    [InlineData("//www.focumon.com/a.png", false)]
    [InlineData("", false)]
    public void Pictures_only_come_from_Focumon_itself(string url, bool allowed)
        => Assert.Equal(allowed, Focumon.SafeImage(url) != null);

    [Fact]
    public void A_picture_from_somewhere_else_is_dropped_but_the_rest_stays()
    {
        var status = Parse("""{"v":1,"phase":"idle","monster":"Embrix","image":"https://elsewhere.example/a.png"}""")!;
        Assert.Null(status.ImageUrl);
        Assert.Equal("Embrix", status.Monster);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(1453, "24:13")]
    [InlineData(1500, "25:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    public void The_clock_reads_like_the_page(int seconds, string expected)
        => Assert.Equal(expected, Focumon.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_page_that_went_quiet_is_stale()
    {
        var status = Parse("""{"v":1,"phase":"focus","down":false,"seconds":10}""")!;
        Assert.False(status.IsStale(Now.AddSeconds(100)));
        Assert.True(status.IsStale(Now.AddSeconds(151)));
    }

    // ───────────── who may talk to the link ─────────────

    const int Port = 47631;

    static FocumonLink.Request Request(string method = "POST", string target = "/focumon", string host = "127.0.0.1:47631",
                                       string? origin = "chrome-extension://abc", bool mark = true, int length = 50)
        => new(method, target, host, origin, mark, length, true);

    [Fact]
    public void The_extension_is_let_in()
        => Assert.Equal(FocumonLink.Verdict.Allowed, FocumonLink.Judge(Request(), Port));

    [Fact]
    public void Localhost_by_name_is_fine_too()
        => Assert.Equal(FocumonLink.Verdict.Allowed, FocumonLink.Judge(Request(host: "localhost:47631"), Port));

    [Theory]
    [InlineData("https://www.focumon.com")]       // a page, even Focumon's own
    [InlineData("https://evil.example")]
    [InlineData("null")]
    [InlineData(null)]                             // no origin: not a browser's fetch from an extension
    public void A_web_page_is_not_let_in(string? origin)
        => Assert.Equal(FocumonLink.Verdict.Forbidden, FocumonLink.Judge(Request(origin: origin), Port));

    [Fact]
    public void Without_our_header_nobody_is_let_in()
        => Assert.Equal(FocumonLink.Verdict.Forbidden, FocumonLink.Judge(Request(mark: false), Port));

    [Theory]
    [InlineData("evil.example")]                   // a name pointed at 127.0.0.1
    [InlineData("127.0.0.1:1234")]
    [InlineData(null)]
    public void Only_our_own_address_counts(string? host)
        => Assert.Equal(FocumonLink.Verdict.Forbidden, FocumonLink.Judge(Request(host: host!), Port));

    [Fact]
    public void Only_one_thing_can_be_done_here()
    {
        Assert.Equal(FocumonLink.Verdict.WrongMethod, FocumonLink.Judge(Request(method: "GET"), Port));
        Assert.Equal(FocumonLink.Verdict.WrongMethod, FocumonLink.Judge(Request(method: "OPTIONS"), Port));
        Assert.Equal(FocumonLink.Verdict.Forbidden, FocumonLink.Judge(Request(target: "/other"), Port));
    }

    [Fact]
    public void Messages_are_small()
    {
        Assert.Equal(FocumonLink.Verdict.TooBig, FocumonLink.Judge(Request(length: 4097), Port));
        Assert.Equal(FocumonLink.Verdict.Malformed, FocumonLink.Judge(Request(length: -1), Port));
        Assert.Equal(FocumonLink.Verdict.Malformed, FocumonLink.Judge(Request(length: -2), Port));
    }

    [Fact]
    public void A_request_is_read_the_way_a_browser_writes_it()
    {
        var request = FocumonLink.Request.Parse(
            "POST /focumon HTTP/1.1\r\nHost: 127.0.0.1:47631\r\norigin: chrome-extension://abc\r\nX-Holm-Focumon: 1\r\ncontent-length: 12\r\nAccept: */*");
        Assert.True(request.Valid);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/focumon", request.Target);
        Assert.Equal("127.0.0.1:47631", request.Host);
        Assert.Equal("chrome-extension://abc", request.Origin);
        Assert.True(request.HasMark);
        Assert.Equal(12, request.ContentLength);
        Assert.False(FocumonLink.Request.Parse("nonsense").Valid);
    }

    // ───────────── the real thing, over a socket ─────────────

    static async Task<string> Send(int port, string headers, string body)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        byte[] bytes = Encoding.UTF8.GetBytes($"POST /focumon HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n{headers}Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
        await stream.WriteAsync(bytes);
        var answer = new byte[512];
        int read = await stream.ReadAsync(answer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        return Encoding.Latin1.GetString(answer, 0, read).Split("\r\n")[0];
    }

    [Fact]
    public async Task The_extension_tells_and_Holm_hears()
    {
        using var link = FocumonLink.TryStart(0)!;
        Assert.NotNull(link);
        var heard = new TaskCompletionSource<FocumonStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Updated += s => heard.TrySetResult(s);

        string reply = await Send(link.Port, "Origin: chrome-extension://abc\r\nX-Holm-Focumon: 1\r\n",
            """{"v":1,"phase":"focus","down":true,"seconds":1500,"monster":"Embrix"}""");

        Assert.Equal("HTTP/1.1 204 No Content", reply);
        var status = await heard.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Embrix", status.Monster);
        Assert.Equal(FocumonPhase.Focus, status.Phase);
    }

    [Fact]
    public async Task The_page_closing_is_heard()
    {
        using var link = FocumonLink.TryStart(0)!;
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Gone += () => gone.TrySetResult();

        await Send(link.Port, "Origin: chrome-extension://abc\r\nX-Holm-Focumon: 1\r\n", """{"v":1,"phase":"gone"}""");
        await gone.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_web_page_gets_nothing_through()
    {
        using var link = FocumonLink.TryStart(0)!;
        bool heard = false;
        link.Updated += _ => heard = true;

        string reply = await Send(link.Port, "Origin: https://evil.example\r\nX-Holm-Focumon: 1\r\n",
            """{"v":1,"phase":"focus","down":true,"seconds":1500,"monster":"Fake"}""");

        Assert.Equal("HTTP/1.1 403 Forbidden", reply);
        await Task.Delay(100);
        Assert.False(heard);
    }

    [Fact]
    public async Task Rubbish_from_the_extension_is_turned_down()
    {
        using var link = FocumonLink.TryStart(0)!;
        string reply = await Send(link.Port, "Origin: chrome-extension://abc\r\nX-Holm-Focumon: 1\r\n", "not json");
        Assert.Equal("HTTP/1.1 400 Bad Request", reply);
    }

    [Fact]
    public void A_second_Holm_does_not_crash_on_a_busy_port()
    {
        using var first = FocumonLink.TryStart(0)!;
        Assert.Null(FocumonLink.TryStart(first.Port));
    }
}
