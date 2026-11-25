using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WinNotch.Sharing;

namespace WinNotch.Tests;

// Friend notes on Holm's relay (relay/) with ntfy beside it: where notes go, what happens when one server refuses them,
// and how an inbox moves to the relay without losing friends on older Holm versions. Both servers are played by
// FakeInboxServer here; RelayWorkerTests runs the same against the real Worker (wrangler dev).

[CollectionDefinition("Inbox servers", DisableParallelization = true)]
public class InboxServersCollection { }

[Collection("Inbox servers")]
public sealed class RelayInboxTests : IDisposable
{
    readonly FakeInboxServer _relay = new(), _ntfy = new();
    readonly string _wasDefault = FriendIdentity.DefaultServer, _wasRelay = FriendIdentity.RelayServer;

    public RelayInboxTests()
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

    static Friend OnRelay(FriendIdentity who) => new() { Name = who.Name, Topic = who.Topic, Key = who.PublicKey, Server = FriendIdentity.RelayServer };

    [Fact]
    public async Task A_note_the_relay_refuses_goes_to_ntfy()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        _relay.Status = 503;
        await FriendInbox.PostAsync(OnRelay(lena), "wnf1 x", Net.Timeout());
        Assert.Equal(lena.Topic, Assert.Single(_ntfy.Posts).Topic);
        Assert.Empty(_relay.Posts);
    }

    [Fact]
    public async Task A_note_for_ntfy_doesnt_go_to_the_relay()
    {
        // Older Holm only follows ntfy: a note it would never read is worse than a clear error.
        using var lena = FriendIdentity.Create("Lena", null);
        _ntfy.Status = 429;
        await Assert.ThrowsAsync<IOException>(() => FriendInbox.PostAsync(lena.AsFriend(), "wnf1 x", Net.Timeout()));
        Assert.Empty(_relay.Posts);
    }

    [Fact]
    public async Task Presence_is_passed_on_but_not_kept()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        await FriendInbox.PostAsync(OnRelay(lena), "wnf1 ping", Net.Timeout(), keep: false);
        await FriendInbox.PostAsync(OnRelay(lena), "wnf1 chat", Net.Timeout());
        Assert.Equal([false, true], _relay.Posts.Select(p => p.Keep));
    }

    [Fact]
    public async Task An_invite_code_works_while_ntfy_is_over_its_daily_quota()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        string code = FriendInvite.New();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _ntfy.Status = 429; // "daily message quota reached": what broke short friend codes
        await FriendInbox.PublishInviteAsync(FriendInvite.Topic(code), FriendInvite.Seal(code, lena.FriendCode, now + 600), Net.Timeout());
        _ntfy.Status = 200;
        var cards = await FriendInbox.FetchInviteAsync(FriendInvite.Topic(code), Net.Timeout());
        Assert.Equal(lena.PublicKey, cards.Select(c => FriendInvite.Open(code, c, now)).Single(f => f != null)!.Key);
    }

    [Fact]
    public async Task An_invite_goes_to_both_servers_and_one_left_by_an_older_Holm_is_found_on_ntfy()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        string code = FriendInvite.New(), old = FriendInvite.New();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await FriendInbox.PublishInviteAsync(FriendInvite.Topic(code), FriendInvite.Seal(code, lena.FriendCode, now + 600), Net.Timeout());
        Assert.Single(_relay.Posts);
        Assert.Single(_ntfy.Posts); // for friends whose Holm doesn't know the relay

        _ntfy.Keep(FriendInvite.Topic(old), FriendInvite.Seal(old, lena.FriendCode, now + 600)); // as Holm 1.17 leaves it
        _relay.Status = 503;
        var cards = await FriendInbox.FetchInviteAsync(FriendInvite.Topic(old), Net.Timeout());
        Assert.NotNull(FriendInvite.Open(old, Assert.Single(cards), now));
    }

    [Fact]
    public async Task An_invite_needs_one_server()
    {
        _relay.Status = _ntfy.Status = 503;
        var ex = await Assert.ThrowsAsync<IOException>(() => FriendInbox.PublishInviteAsync(FriendInvite.Topic(FriendInvite.New()), "hinv1 x", Net.Timeout()));
        Assert.Contains("503", ex.Message);
    }

    static Task Ui(Action a)
    {
        lock (typeof(RelayInboxTests)) a();
        return Task.CompletedTask;
    }

    static async Task Until(Func<bool> done, string what)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(100);
        Assert.True(done(), what);
    }

    [Fact]
    public async Task An_inbox_moves_to_the_relay_and_friends_on_older_Holm_keep_reaching_it()
    {
        // Noah is a friend on Holm 1.17: he only knows ntfy, and Lena's old card says ntfy.
        using var noah = FriendIdentity.Create("Noah", null);
        var settings = new AppSettings { Beam = true, ProfileName = "Lena" };
        settings.Friends.Add(noah.AsFriend());
        var lena = new FriendsService(settings, Ui);
        var chats = new ConcurrentQueue<string>();
        lena.ChatReceived += (_, _, text, _) => chats.Enqueue(text);
        try
        {
            var me = lena.EnsureMe(); // follows ntfy and the relay
            var oldCard = me.AsFriend();
            Assert.Null(oldCard.Server);

            // The relay let her in: her code names it now, and Noah was told.
            await Until(() => settings.FriendsRelay, "the inbox moved to the relay");
            Assert.Equal(_relay.Url, FriendIdentity.ParseFriendCode(lena.Me!.FriendCode)!.Server);
            // Her profile goes to him as well; the move is the "added you" note with her new code.
            List<FriendNote> Told() => _ntfy.Posts.Where(p => p.Topic == noah.Topic).Select(p => FriendNotes.Open(noah, p.Text, out _))
                                                  .Where(n => n?.Kind == FriendNoteKind.AddedYou).ToList()!;
            await Until(() => Told().Count > 0, "Noah heard of the move");
            var told = Assert.Single(Told());
            var newCard = FriendIdentity.ParseFriendCode(told.Code)!;
            Assert.Equal(_relay.Url, newCard.Server); // Holm 1.17 replaces a friend's card with this one (FriendsService.Handle)

            // Noah's notes reach her through both: the old card on ntfy, the new one on the relay.
            var lenaAsFriend = lena.Me!.AsFriend();
            await FriendInbox.PostAsync(oldCard, Chat(noah, lenaAsFriend, "via ntfy"), Net.Timeout());
            await FriendInbox.PostAsync(newCard, Chat(noah, lenaAsFriend, "via the relay"), Net.Timeout());
            await Until(() => chats.Count == 2, "both chats arrived");
            Assert.Equal(["via ntfy", "via the relay"], chats.Order());

            // Holm closed; a note waits on the relay; the next start catches up and handles it once.
            lena.Stop();
            await FriendInbox.PostAsync(newCard, Chat(noah, lenaAsFriend, "while closed"), Net.Timeout());
            lena.Restart();
            await Until(() => chats.Count == 3, "the note left while Holm was closed arrived");
            await Task.Delay(500);
            Assert.Equal(3, chats.Count);

            // Moving happens once.
            lena.Restart();
            await Task.Delay(1000);
            Assert.Single(Told());
        }
        finally { lena.Stop(); }
    }

    [Fact]
    public async Task An_inbox_on_a_server_of_ones_own_stays_there()
    {
        using var custom = new FakeInboxServer();
        var settings = new AppSettings { Beam = true, FriendsServer = custom.Url };
        var lena = new FriendsService(settings, Ui);
        try
        {
            lena.EnsureMe();
            await Task.Delay(1000);
            Assert.False(settings.FriendsRelay);
            Assert.Equal(custom.Url, FriendIdentity.ParseFriendCode(lena.Me!.FriendCode)!.Server);
            Assert.Equal(0, _relay.Followers);
        }
        finally { lena.Stop(); }
    }

    static string Chat(FriendIdentity from, Friend to, string text)
        => FriendNotes.Seal(from, to, new FriendNote(FriendNoteKind.Chat, from.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                                     Text: text, MessageId: Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Resending_a_request_keeps_the_original_accept_button_working()
    {
        var sender = new FriendsService(new AppSettings { Beam = true, FriendsServer = _ntfy.Url }, Ui);
        var receiver = new FriendsService(new AppSettings { Beam = true, FriendsServer = _ntfy.Url }, Ui);
        try
        {
            var target = receiver.EnsureMe().AsFriend();
            sender.EnsureMe();
            await Until(() => _ntfy.Followers == 2, "both inboxes connected");
            Assert.Equal(FriendRequestResult.NoResponse,
                await sender.RequestAddAsync(target, TimeSpan.FromMilliseconds(100), Net.Timeout()));
            await Until(() => receiver.Incoming.Count == 1, "the request arrived");
            var original = Assert.Single(receiver.Incoming);

            Assert.Equal(FriendRequestResult.NoResponse,
                await sender.RequestAddAsync(target, TimeSpan.FromMilliseconds(100), Net.Timeout()));
            Assert.Equal(original.RequestId, Assert.Single(sender.Pending).RequestId);
            Assert.Equal(original.RequestId, Assert.Single(receiver.Incoming).RequestId);

            await receiver.RespondToFriendRequestAsync(original.Card, original.RequestId, true, Net.Timeout());
            await Until(() => sender.Find(target.Key) != null, "the sender received the acceptance");
            Assert.NotNull(receiver.Find(sender.Me!.PublicKey));
            Assert.Empty(receiver.Incoming);
        }
        finally { sender.Stop(); receiver.Stop(); }
    }

    [Fact]
    public async Task An_unanswered_request_survives_a_settings_reload_and_can_be_withdrawn()
    {
        var settings = new AppSettings { Beam = true, FriendsServer = _ntfy.Url };
        var sender = new FriendsService(new AppSettings { Beam = true, FriendsServer = _ntfy.Url }, Ui);
        var receiver = new FriendsService(settings, Ui);
        FriendsService? restarted = null;
        try
        {
            var target = receiver.EnsureMe().AsFriend();
            sender.EnsureMe();
            await Until(() => _ntfy.Subscriptions == 2, "both inboxes connected");
            await sender.RequestAddAsync(target, TimeSpan.FromMilliseconds(100), Net.Timeout());
            await Until(() => receiver.Incoming.Count == 1, "the request arrived");
            receiver.Stop();
            var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            restarted = new FriendsService(reloaded, Ui);
            Assert.Equal(Assert.Single(sender.Pending).RequestId, Assert.Single(restarted.Incoming).RequestId);
            Assert.Equal(target.Key, restarted.EnsureMe().PublicKey);
            restarted.Restart();
            await Until(() => _ntfy.Subscriptions == 3, "the receiver reconnected");
            await sender.CancelRequestAsync(Assert.Single(sender.Pending));
            await Until(() => restarted.Incoming.Count == 0, "the withdrawal removed the saved request");
        }
        finally { sender.Stop(); receiver.Stop(); restarted?.Stop(); }
    }
}

/// <summary>
/// The relay (relay/src/index.js) run locally with `npm run dev` in relay/: set HOLM_RELAY_URL=http://127.0.0.1:8787
/// and run the Local tests. Checks Holm's real client against the real Worker.
/// </summary>
[Collection("Inbox servers")]
[Trait("Category", "Local")]
public sealed class RelayWorkerTests : IDisposable
{
    readonly string? _url = Environment.GetEnvironmentVariable("HOLM_RELAY_URL");
    readonly string _wasDefault = FriendIdentity.DefaultServer, _wasRelay = FriendIdentity.RelayServer;

    public RelayWorkerTests()
    {
        if (_url == null) return;
        FriendIdentity.RelayServer = _url.TrimEnd('/');
        FriendIdentity.DefaultServer = "http://127.0.0.1:9"; // no ntfy: everything must work on the relay alone
    }

    public void Dispose()
    {
        FriendIdentity.DefaultServer = _wasDefault;
        FriendIdentity.RelayServer = _wasRelay;
    }

    [Fact]
    public async Task Notes_invites_presence_and_catching_up_work_on_the_Worker()
    {
        if (_url == null) return; // needs `npm run dev` in relay/
        using var lena = FriendIdentity.Create("Lena", FriendIdentity.RelayServer);
        using var noah = FriendIdentity.Create("Noah", null);
        var toLena = lena.AsFriend();
        Assert.Equal(FriendIdentity.RelayServer, toLena.Server);

        string Note(FriendNoteKind kind, string? text = null)
            => FriendNotes.Seal(noah, toLena, new FriendNote(kind, "Noah", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text: text,
                                                             MessageId: text == null ? null : Guid.NewGuid().ToString("N")));

        // Left before Lena follows: kept, and found when she catches up.
        long since = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5;
        await FriendInbox.PostAsync(toLena, Note(FriendNoteKind.Chat, "kept"), Net.Timeout());
        await FriendInbox.PostAsync(toLena, Note(FriendNoteKind.Ping), Net.Timeout(), keep: false); // gone: nobody listened

        var got = new ConcurrentQueue<FriendNote>();
        long last = 0;
        bool connected = false;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var follow = FriendInbox.FollowRelayAsync(lena.Topic, since, (_, _, text) =>
        {
            if (FriendNotes.Open(lena, text, out _) is { } n) got.Enqueue(n);
            return Task.CompletedTask;
        }, t => last = t, () => connected = true, null, stop.Token);

        for (int i = 0; i < 100 && got.IsEmpty; i++) await Task.Delay(100);
        Assert.True(connected);
        Assert.Equal("kept", Assert.Single(got).Text);

        await FriendInbox.PostAsync(toLena, Note(FriendNoteKind.Ping), Net.Timeout(), keep: false);
        await FriendInbox.PostAsync(toLena, Note(FriendNoteKind.Chat, "live"), Net.Timeout());
        for (int i = 0; i < 100 && got.Count < 3; i++) await Task.Delay(100);
        Assert.Equal([FriendNoteKind.Chat, FriendNoteKind.Ping, FriendNoteKind.Chat], got.Select(n => n.Kind));
        Assert.True(last >= since);
        stop.Cancel();
        await follow;

        // An invite code, on the relay alone (ntfy is unreachable here).
        string code = FriendInvite.New();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await FriendInbox.PublishInviteAsync(FriendInvite.Topic(code), FriendInvite.Seal(code, lena.FriendCode, now + 600), Net.Timeout());
        var cards = await FriendInbox.FetchInviteAsync(FriendInvite.Topic(code), Net.Timeout());
        Assert.Equal(lena.PublicKey, FriendInvite.Open(code, Assert.Single(cards), now)!.Key);

        // A quiet line stays open: WebSocket pings (every 30 s, 30 s for the pong) keep it, without reconnecting.
        got.Clear();
        int connects = 0;
        using var quiet = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var quietFollow = FriendInbox.FollowRelayAsync(lena.Topic, null, (_, _, text) =>
        {
            if (FriendNotes.Open(lena, text, out _) is { } n) got.Enqueue(n);
            return Task.CompletedTask;
        }, _ => { }, () => Interlocked.Increment(ref connects), null, quiet.Token);
        await Task.Delay(TimeSpan.FromSeconds(75));
        await FriendInbox.PostAsync(toLena, Note(FriendNoteKind.Chat, "after a quiet minute"), Net.Timeout());
        for (int i = 0; i < 50 && got.IsEmpty; i++) await Task.Delay(100);
        Assert.Equal("after a quiet minute", Assert.Single(got).Text);
        Assert.Equal(1, connects);
        quiet.Cancel();
        await quietFollow;

        // What isn't a Holm note is refused.
        await Assert.ThrowsAsync<IOException>(() => FriendInbox.PostAsync(new Friend { Topic = lena.Topic, Server = FriendIdentity.RelayServer }, "hello", Net.Timeout()));
    }
}

/// <summary>
/// An inbox server for tests, speaking what Holm uses of ntfy (POST, polling, the JSON stream) and the relay's WebSocket.
/// <see cref="Status"/> other than 200 refuses every note and poll, as a server over its quota or down.
/// </summary>
sealed class FakeInboxServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly List<(string Id, long Time, string Topic, string Text)> _kept = new();
    readonly List<(string Topic, Func<string, Task> Send)> _followers = new();
    readonly CancellationTokenSource _stop = new();
    int _subscriptions;
    public string Url { get; }
    public volatile int Status = 200;
    public ConcurrentQueue<(string Topic, string Text, bool Keep)> Posts { get; } = new();
    public int Followers { get { lock (_followers) return _followers.Count; } }
    public int Subscriptions => Volatile.Read(ref _subscriptions);

    public FakeInboxServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://localhost:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(() => HandleAsync(context));
            }
        });
    }

    public void Keep(string topic, string text)
    {
        lock (_kept) _kept.Add((Guid.NewGuid().ToString("N")[..12], DateTimeOffset.UtcNow.ToUnixTimeSeconds(), topic, text));
    }

    static string Line(string ev, string id, long time, string topic, string? text)
        => JsonSerializer.Serialize(new { id, time, @event = ev, topic, message = text });

    List<string> KeptSince(string topic, long? since)
    {
        lock (_kept) return since == null ? [] : _kept.Where(k => k.Topic == topic && k.Time >= since).Select(k => Line("message", k.Id, k.Time, topic, k.Text)).ToList();
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        string[] parts = request.Url!.AbsolutePath.Trim('/').Split('/');
        string topic = parts[0];
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long? since = request.QueryString["since"] switch { null => null, "12h" => now - 43200, var s => long.Parse(s) };
        try
        {
            if (Status != 200)
            {
                response.StatusCode = Status;
                response.Close();
                return;
            }
            if (request.HttpMethod == "POST")
            {
                string text = await new StreamReader(request.InputStream).ReadToEndAsync();
                bool keep = request.Headers["Cache"] != "no";
                Posts.Enqueue((topic, text, keep));
                string id = Guid.NewGuid().ToString("N")[..12];
                if (keep) lock (_kept) _kept.Add((id, now, topic, text));
                List<Func<string, Task>> sends;
                lock (_followers) sends = _followers.Where(f => f.Topic == topic).Select(f => f.Send).ToList();
                foreach (var send in sends)
                {
                    try { await send(Line("message", id, now, topic, text)); } catch { }
                }
                await Write(response, Line("message", id, now, topic, text) + "\n");
                response.Close();
                return;
            }
            if (parts.Length > 1 && parts[1] == "json" && request.QueryString["poll"] == "1")
            {
                await Write(response, string.Concat(KeptSince(topic, since).Select(l => l + "\n")));
                response.Close();
                return;
            }
            if (parts.Length > 1 && parts[1] == "ws" && request.IsWebSocketRequest)
            {
                var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var gate = new SemaphoreSlim(1);
                async Task Send(string line)
                {
                    await gate.WaitAsync();
                    try { await ws.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, CancellationToken.None); }
                    finally { gate.Release(); }
                }
                await Send(Line("open", "o", now, topic, null));
                foreach (string line in KeptSince(topic, since)) await Send(line);
                var follower = (topic, (Func<string, Task>)Send);
                lock (_followers) _followers.Add(follower);
                Interlocked.Increment(ref _subscriptions);
                try { await ws.ReceiveAsync(new byte[16], _stop.Token); } catch { }
                lock (_followers) _followers.Remove(follower);
                return;
            }
            if (parts.Length > 1 && parts[1] == "json")
            {
                response.SendChunked = true;
                var gate = new SemaphoreSlim(1);
                async Task Send(string line)
                {
                    await gate.WaitAsync();
                    try
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                        await response.OutputStream.WriteAsync(bytes);
                        await response.OutputStream.FlushAsync();
                    }
                    finally { gate.Release(); }
                }
                await Send(Line("open", "o", now, topic, null));
                foreach (string line in KeptSince(topic, since)) await Send(line);
                var follower = (topic, (Func<string, Task>)Send);
                lock (_followers) _followers.Add(follower);
                Interlocked.Increment(ref _subscriptions);
                try { await Task.Delay(Timeout.Infinite, _stop.Token); } catch { }
                lock (_followers) _followers.Remove(follower);
                return;
            }
            response.StatusCode = 404;
            response.Close();
        }
        catch { try { response.Abort(); } catch { } }
    }

    static async Task Write(HttpListenerResponse response, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
