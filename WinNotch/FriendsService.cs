using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

internal enum FriendRequestResult { Accepted, Declined, NoResponse }

/// <summary>
/// Friends on this PC: our identity (the private key is encrypted for this Windows account), the friends list,
/// and our inbox, followed while Beam is on and we have a friend code. Settings are only touched on the UI thread.
/// </summary>
internal sealed class FriendsService
{
    static readonly byte[] KeyEntropy = "WinNotch friends key"u8.ToArray(); // former name: keys saved before the rename must still open

    /// <summary>How long the sender waits for the friend's notch to answer; older invitations are stale.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    /// <summary>An older "are you there?" or answer is history: it came in while Holm wasn't running.</summary>
    const long FreshSeconds = 30;

    readonly AppSettings _settings;
    readonly Dispatcher _ui;
    FriendIdentity? _me;
    bool _loaded;
    CancellationTokenSource? _follow;
    readonly ConcurrentDictionary<string, TaskCompletionSource<FriendRequestResult>> _friendRequests = new(StringComparer.Ordinal);
    readonly HashSet<string> _cancelledFriendRequests = new(StringComparer.Ordinal);
    readonly FriendPresence _presence = new();

    /// <summary>A friend wants to send us files; meet them on the relay with the secret (UI thread).</summary>
    public event Action<Friend, byte[]>? FilesOffered;
    /// <summary>Someone added us as a friend; this is their card, to add them back (UI thread).</summary>
    public event Action<Friend>? AddedUs;
    /// <summary>A friend request arrived and needs an explicit answer (UI thread).</summary>
    public event Action<Friend, string>? FriendRequested;
    /// <summary>A pending friend request was cancelled by its sender (UI thread).</summary>
    public event Action<Friend, string>? FriendRequestCancelled;
    /// <summary>A friend removed us from their list (UI thread).</summary>
    public event Action<Friend>? RemovedUs;
    /// <summary>A friend's notch answered or asked: their Holm is running now (UI thread).</summary>
    public event Action<Friend>? FriendOnline;
    /// <summary>A friend waved (UI thread); already limited to one per friend every 30 seconds.</summary>
    public event Action<Friend>? Waved;

    readonly WaveLimit _wavesOut = new(), _wavesIn = new();

    public FriendsService(AppSettings settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
        int removed = RemoveDuplicateFriendCards(_settings.Friends);
        if (removed > 0)
        {
            _settings.Save();
            Diag.Log($"Friends: removed {removed} duplicate or invalid saved entries");
        }
    }

    public IReadOnlyList<Friend> Friends => _settings.Friends;

    /// <summary>Deduplicates old saved friend cards, keeping the latest card for each public key.</summary>
    internal static int RemoveDuplicateFriendCards(List<Friend> friends)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int removed = 0;
        for (int i = friends.Count - 1; i >= 0; i--)
        {
            var friend = friends[i];
            if (friend == null || string.IsNullOrWhiteSpace(friend.Key) || !seen.Add(friend.Key))
            {
                friends.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>Who we are, once set up (the first time you copy your friend code or add a friend).</summary>
    public FriendIdentity? Me
    {
        get
        {
            if (!_loaded) { _loaded = true; _me = Load(); }
            return _me;
        }
    }

    FriendIdentity? Load()
    {
        if (_settings.FriendsTopic == null || _settings.FriendsKey == null) return null;
        try
        {
            byte[] pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(_settings.FriendsKey), KeyEntropy, DataProtectionScope.CurrentUser);
            var key = ECDiffieHellman.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            CryptographicOperations.ZeroMemory(pkcs8);
            return new FriendIdentity(Native.DisplayName(), _settings.FriendsTopic, _settings.FriendsServer, key);
        }
        catch (Exception ex)
        {
            // E.g. settings copied from another PC or account: the key can't be decrypted here.
            Diag.Log("Friend identity unreadable, a new one will be made: " + ex.Message);
            return null;
        }
    }

    /// <summary>Our identity, made on first use. Friends who had an unreadable older one must add us again.</summary>
    public FriendIdentity EnsureMe()
    {
        if (Me is { } me) return me;
        me = FriendIdentity.Create(Native.DisplayName(), _settings.FriendsServer);
        byte[] pkcs8 = me.Key.ExportPkcs8PrivateKey();
        _settings.FriendsKey = Convert.ToBase64String(ProtectedData.Protect(pkcs8, KeyEntropy, DataProtectionScope.CurrentUser));
        CryptographicOperations.ZeroMemory(pkcs8);
        _settings.FriendsTopic = me.Topic;
        // Catch up from now on: a friend may write while Holm isn't running.
        _settings.FriendsSince = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _settings.FriendsSeen.Clear();
        _settings.Save();
        _me = me;
        Restart();
        return me;
    }

    public Friend? Find(string publicKey) => _settings.Friends.FirstOrDefault(f => f.Key == publicKey);

    public bool IsMe(Friend card) => Me?.PublicKey == card.Key;

    /// <summary>
    /// Adds or updates a friend. With <paramref name="tellThem"/>, their notch offers to add us back.
    /// Returns false if that note couldn't be delivered (the friend is added all the same).
    /// </summary>
    public async Task<bool> AddAsync(Friend friend, bool tellThem)
    {
        if (Find(friend.Key) != null) return true;
        var me = EnsureMe();
        _settings.Friends.Add(friend);
        _settings.Save();
        if (!tellThem) return true;
        try
        {
            var note = new FriendNote(FriendNoteKind.AddedYou, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Code: me.FriendCode);
            await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, note), CancellationToken.None);
            return true;
        }
        catch (IOException ex)
        {
            Diag.Log("Couldn't tell the new friend: " + ex.Message);
            return false;
        }
    }

    /// <summary>Sends a friend request and adds the card only after the other person accepts.</summary>
    public async Task<FriendRequestResult> RequestAddAsync(Friend friend, TimeSpan timeout, CancellationToken ct)
    {
        if (Find(friend.Key) is { } existing)
        {
            UpdateFriendCard(existing, friend);
            return FriendRequestResult.Accepted;
        }

        var me = EnsureMe(); // starts our inbox before the request is posted
        string requestId = Guid.NewGuid().ToString("N");
        var answer = new TaskCompletionSource<FriendRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_friendRequests.TryAdd(requestId, answer)) throw new InvalidOperationException("Duplicate friend request id");
        try
        {
            var note = new FriendNote(FriendNoteKind.FriendRequest, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Code: me.FriendCode, RequestId: requestId);
            await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, note), ct);
            try
            {
                var result = await answer.Task.WaitAsync(timeout, ct);
                ct.ThrowIfCancellationRequested();
                if (result == FriendRequestResult.Accepted) AddOrUpdateFriend(friend, requestId);
                return result;
            }
            catch (TimeoutException)
            {
                await SendFriendRequestCancellationAsync(me, friend, requestId);
                return FriendRequestResult.NoResponse;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await SendFriendRequestCancellationAsync(me, friend, requestId);
            throw;
        }
        finally { _friendRequests.TryRemove(requestId, out _); }
    }

    async Task SendFriendRequestCancellationAsync(FriendIdentity me, Friend friend, string requestId)
    {
        RemoveFriendAddedByRequest(requestId);
        var cancel = new FriendNote(FriendNoteKind.FriendRequestCancelled, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Code: me.FriendCode, RequestId: requestId);
        await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, cancel), CancellationToken.None);
    }

    /// <summary>Sends the user's answer to a request; accepted cards are saved after the reply is delivered.</summary>
    public async Task RespondToFriendRequestAsync(Friend friend, string requestId, bool accepted, CancellationToken ct = default)
    {
        if (Me is not { } me) me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.FriendResponse, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            RequestId: requestId, Accepted: accepted);
        await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, note), ct);
        if (accepted && !_cancelledFriendRequests.Contains(requestId)) AddOrUpdateFriend(friend, requestId);
    }

    void AddOrUpdateFriend(Friend card, string? requestId = null)
    {
        if (Find(card.Key) is { } existing) UpdateFriendCard(existing, card);
        else
        {
            card.AddedByRequestId = requestId;
            _settings.Friends.Add(card);
            _settings.Save();
        }
    }

    void UpdateFriendCard(Friend existing, Friend card)
    {
        int at = _settings.Friends.IndexOf(existing);
        if (at < 0) return;
        card.AddedByRequestId ??= existing.AddedByRequestId;
        _settings.Friends[at] = card;
        _settings.Save();
    }

    void RemoveFriendAddedByRequest(string requestId)
    {
        if (_settings.Friends.RemoveAll(friend => friend.AddedByRequestId == requestId) > 0) _settings.Save();
    }

    /// <summary>Removes a friend here and tells them to remove this relationship on their side too.</summary>
    public async Task<bool> RemoveAsync(Friend friend)
    {
        if (_settings.Friends.RemoveAll(f => f.Key == friend.Key) == 0) return true;
        _settings.Save();

        if (Me is not { } me)
        {
            Diag.Log($"Couldn't tell {friend.Name} that they were removed: our friend identity is unavailable");
            return false;
        }

        try
        {
            var note = new FriendNote(FriendNoteKind.RemovedYou, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, note), CancellationToken.None);
            return true;
        }
        catch (IOException ex)
        {
            Diag.Log($"Couldn't tell {friend.Name} that they were removed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Leaves a note asking the friend's notch to meet us on the relay with <paramref name="secret"/>.</summary>
    /// <exception cref="IOException">The note couldn't be delivered.</exception>
    public Task InviteAsync(Friend to, byte[] secret, CancellationToken ct)
    {
        var me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.Files, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret: secret);
        return FriendInbox.PostAsync(to, FriendNotes.Seal(me, to, note), ct);
    }

    /// <summary>Waves at a friend. False when it's too soon after the last wave (see <see cref="WaveLimit"/>).</summary>
    public bool Wave(Friend to)
    {
        if (!_wavesOut.Allow(to.Key, DateTime.UtcNow)) return false;
        Tell(EnsureMe(), to, FriendNoteKind.Wave);
        return true;
    }

    /// <summary>How long until the next wave to this friend may go.</summary>
    public TimeSpan WaveWait(Friend to) => _wavesOut.Wait(to.Key, DateTime.UtcNow);

    /// <summary>Follows our inbox while Beam is on and we have an identity; costs nothing otherwise.</summary>
    public void Restart()
    {
        Stop();
        if (!_settings.Beam || Me is not { } me) return;
        var cts = new CancellationTokenSource();
        _follow = cts;
        long? since = _settings.FriendsSince;
        _ = Task.Run(() => FriendInbox.FollowAsync(me, since,
            (id, age, text) => _ui.InvokeAsync(() => { if (_follow == cts) Handle(me, id, age, text); }).Task,
            time => _ui.InvokeAsync(() => { if (_follow == cts) { _settings.FriendsSince = time; _settings.Save(); } }),
            Diag.Log, cts.Token));
    }

    public void Stop()
    {
        _follow?.Cancel();
        _follow = null;
    }

    /// <summary>Whether the friend's Holm answered or asked in the last two minutes.</summary>
    public bool IsOnline(Friend friend) => _presence.IsOnline(friend.Key, DateTime.UtcNow);

    /// <summary>
    /// Asks the first friends (as many as the share view shows) whether their Holm is running; those that are answer
    /// within a second or two and <see cref="FriendOnline"/> follows. Friends heard from lately or asked a minute ago
    /// are skipped. Only friends learn that we're online, and only when they ask.
    /// </summary>
    public void PingAll(int max = 8)
    {
        if (_follow == null || Me is not { } me) return; // their answers come to our inbox, which we only follow while Beam is on
        foreach (var friend in _settings.Friends.Take(max))
            if (_presence.ShouldAsk(friend.Key, DateTime.UtcNow)) Tell(me, friend, FriendNoteKind.Ping);
    }

    static void Tell(FriendIdentity me, Friend to, FriendNoteKind kind)
    {
        string text = FriendNotes.Seal(me, to, new FriendNote(kind, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _ = Task.Run(async () =>
        {
            try { await FriendInbox.PostAsync(to, text, CancellationToken.None); }
            catch (IOException ex) { Diag.Log($"Friends: couldn't reach {to.Name}'s inbox: {ex.Message}"); }
        });
    }

    void SawOnline(Friend friend)
    {
        if (_presence.Saw(friend.Key, DateTime.UtcNow)) FriendOnline?.Invoke(friend);
    }

    void Handle(FriendIdentity me, string id, long age, string text)
    {
        // Catching up after a reconnect can repeat the last note.
        if (_settings.FriendsSeen.Contains(id)) return;
        _settings.FriendsSeen.Add(id);
        if (_settings.FriendsSeen.Count > 200) _settings.FriendsSeen.RemoveRange(0, _settings.FriendsSeen.Count - 200);

        if (FriendNotes.Open(me, text, out string sender) is not { } note)
        {
            Diag.Log("Friends: a note that isn't for us");
            return;
        }
        var friend = Find(sender);
        switch (note.Kind)
        {
            case FriendNoteKind.Files when friend == null:
                Diag.Log("Friends: someone who isn't a friend wanted to send files");
                break;
            case FriendNoteKind.Files when age > Patience.TotalSeconds:
                Diag.Log($"Friends: {friend.Name} wanted to send files while Holm wasn't running");
                break;
            case FriendNoteKind.Files:
                FilesOffered?.Invoke(friend, note.Secret!);
                break;
            case FriendNoteKind.AddedYou when FriendIdentity.ParseFriendCode(note.Code) is { } card:
                if (friend != null)
                {
                    // Already friends: keep their name and inbox current.
                    int at = _settings.Friends.IndexOf(friend);
                    _settings.Friends[at] = card;
                    _settings.Save();
                }
                else AddedUs?.Invoke(card);
                break;
            case FriendNoteKind.FriendRequest when FriendIdentity.ParseFriendCode(note.Code) is { } card && note.RequestId is { } requestId:
                if (friend != null)
                {
                    // Repair an older one-sided relationship automatically when the other person requests us.
                    UpdateFriendCard(friend, card);
                    _ = ReplyToRequestAsync(me, card, requestId, accepted: true);
                }
                else FriendRequested?.Invoke(card, requestId);
                break;
            case FriendNoteKind.FriendResponse when note.RequestId is { } responseId && note.Accepted is { } accepted:
                if (_friendRequests.TryGetValue(responseId, out var answer))
                    answer.TrySetResult(accepted ? FriendRequestResult.Accepted : FriendRequestResult.Declined);
                break;
            case FriendNoteKind.FriendRequestCancelled when FriendIdentity.ParseFriendCode(note.Code) is { } card && note.RequestId is { } cancelledId:
                _cancelledFriendRequests.Add(cancelledId);
                RemoveFriendAddedByRequest(cancelledId);
                FriendRequestCancelled?.Invoke(card, cancelledId);
                break;
            case FriendNoteKind.RemovedYou when friend != null:
                _settings.Friends.RemoveAll(f => f.Key == friend.Key);
                _settings.Save();
                Diag.Log($"Friends: {friend.Name} removed us");
                RemovedUs?.Invoke(friend);
                break;
            case FriendNoteKind.RemovedYou:
                Diag.Log("Friends: ignored a removal note from someone who isn't a friend");
                break;
            case FriendNoteKind.Ping or FriendNoteKind.Here when friend == null || age > FreshSeconds:
                break; // strangers don't learn whether we're online; old ones came while Holm wasn't running
            case FriendNoteKind.Ping:
                SawOnline(friend);
                if (_presence.ShouldAnswer(friend.Key, DateTime.UtcNow)) Tell(me, friend, FriendNoteKind.Here);
                break;
            case FriendNoteKind.Here:
                SawOnline(friend);
                break;
            case FriendNoteKind.Wave when friend == null || age > WaveLimit.FreshSeconds:
                break; // strangers can't wave; an old wave came while Holm wasn't running
            case FriendNoteKind.Wave:
                SawOnline(friend);
                if (_settings.Waves && _wavesIn.Allow(friend.Key, DateTime.UtcNow)) Waved?.Invoke(friend);
                break;
        }
    }

    async Task ReplyToRequestAsync(FriendIdentity me, Friend card, string requestId, bool accepted)
    {
        try
        {
            var note = new FriendNote(FriendNoteKind.FriendResponse, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                RequestId: requestId, Accepted: accepted);
            await FriendInbox.PostAsync(card, FriendNotes.Seal(me, card, note), CancellationToken.None);
        }
        catch (IOException ex) { Diag.Log($"Couldn't confirm {card.Name}'s friend request: {ex.Message}"); }
    }
}

/// <summary>
/// Which friends are online, and when to ask or answer them, by public key. Every note counts against the inbox
/// server's daily limit, so nobody is asked or answered more than once in a while.
/// </summary>
internal sealed class FriendPresence
{
    /// <summary>A friend counts as online this long after their notch last answered or asked.</summary>
    public static readonly TimeSpan OnlineFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AskEvery = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan AnswerEvery = TimeSpan.FromSeconds(10);

    readonly Dictionary<string, DateTime> _seen = new(), _asked = new(), _answered = new();

    public bool IsOnline(string key, DateTime now) => _seen.TryGetValue(key, out var at) && now - at < OnlineFor;

    /// <summary>Their notch answered or asked. True if they weren't online before.</summary>
    public bool Saw(string key, DateTime now)
    {
        bool was = IsOnline(key, now);
        _seen[key] = now;
        return !was;
    }

    /// <summary>Whether to ask them now: not if we heard from them lately or asked them a minute ago.</summary>
    public bool ShouldAsk(string key, DateTime now)
    {
        if (IsOnline(key, now) || _asked.TryGetValue(key, out var at) && now - at < AskEvery) return false;
        _asked[key] = now;
        return true;
    }

    /// <summary>Whether to answer their question now.</summary>
    public bool ShouldAnswer(string key, DateTime now)
    {
        if (_answered.TryGetValue(key, out var at) && now - at < AnswerEvery) return false;
        _answered[key] = now;
        return true;
    }
}
