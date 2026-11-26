using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Sharing;

namespace WinNotch;

internal enum FriendRequestResult { Accepted, Declined, NoResponse }

/// <summary>What came of using a friend's invite code.</summary>
internal enum InviteResult { Added, Sent, AlreadyFriends, OwnCode, NotFound }

/// <summary>
/// Friends on this PC: our identity (the private key is encrypted for this Windows account), the friends list,
/// and our inbox, followed while Beam is on and we have a friend code. Settings are only touched on the UI thread.
/// </summary>
internal sealed partial class FriendsService
{
    static readonly byte[] KeyEntropy = "WinNotch friends key"u8.ToArray(); // former name: keys saved before the rename must still open

    /// <summary>How long the sender waits for the friend's notch to answer; older invitations are stale.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    /// <summary>
    /// How long a sender waits on the relay for the friend's notch. Longer than <see cref="Patience"/>, which the receiver
    /// holds the note's age to: the note's trip through the inbox and their connection to the relay come on top of it.
    /// </summary>
    public static readonly TimeSpan SendPatience = TimeSpan.FromSeconds(40);
    /// <summary>How long a stream invitation waits for the friend to click Watch.</summary>
    public static readonly TimeSpan StreamPatience = TimeSpan.FromSeconds(60);
    /// <summary>An older "are you there?" or answer is history: it came in while Holm wasn't running.</summary>
    const long FreshSeconds = 30;
    /// <summary>A "something waits for you" note is answered this long after it was left.</summary>
    const long WaitingSeconds = 24 * 60 * 60;
    /// <summary>Bounded UTC clock skew allowed when validating authenticated <see cref="FriendNote.Sent"/> timestamps.</summary>
    const long MaxClockSkewSeconds = 300;
    const int MaxSeenEntries = 500;

    readonly AppSettings _settings;
    /// <summary>Runs on the UI thread (the WPF app: its Dispatcher, see FriendsService.Wpf.cs).</summary>
    readonly Func<Action, Task> _ui;
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
    /// <summary>A friend sent a new name, status, color or picture, already saved (UI thread).</summary>
    public event Action<Friend>? ProfileReceived;
    /// <summary>A friend wants to stream their screen to us; meet them on the relay with the secret (UI thread).</summary>
    public event Action<Friend, byte[]>? StreamOffered;
    /// <summary>A friend's notch told us what they're doing, or that they stopped (UI thread).</summary>
    public event Action<Friend>? ActivityChanged;
    /// <summary>What we're doing, for the friends who ask; null when there's nothing or we don't share it.</summary>
    public Func<FriendActivity?>? MyActivity { get; set; }
    /// <summary>How many achievements we have, for the friends who ask; null to keep it to ourselves.</summary>
    public Func<int?>? MyTrophies { get; set; }
    /// <summary>A friend was added or removed (UI thread).</summary>
    public event Action? ListChanged;
    /// <summary>Another device of this account came, left or got a new name (UI thread).</summary>
    public event Action? OwnDevicesChanged;
    /// <summary>A chat message from a friend: its id, text and when it was sent (Unix time; UI thread).</summary>
    public event Action<Friend, string, string, long>? ChatReceived;

    /// <summary>A GIF from a friend: the message's id, the GIF's link and when it was sent (UI thread).</summary>
    public event Action<Friend, string, string, long>? GifReceived;

    /// <summary>A friend reacted to a chat message: its id and the emoji, or null when they took it away (UI thread).</summary>
    public event Action<Friend, string, string?>? ReactionReceived;

    /// <summary>Someone used our invite code and is now a friend, without asking (UI thread).</summary>
    public event Action<Friend>? InviteAccepted;
    /// <summary>A request of ours was answered after we stopped waiting for it: yes (true) or no (UI thread).</summary>
    public event Action<Friend, bool>? LateAnswer;
    /// <summary>Our invite codes or our unanswered requests changed (UI thread).</summary>
    public event Action? InvitesChanged;

    readonly WaveLimit _wavesOut = new(), _wavesIn = new();
    /// <summary>What friends told us they're doing, by key; only shown while they count as online.</summary>
    readonly Dictionary<string, FriendActivity?> _activities = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _trophies = new(StringComparer.Ordinal);
    /// <summary>Profile versions on their way, by friend key: one note each, however often we're asked to sync.</summary>
    readonly Dictionary<string, string> _profilesOut = new(StringComparer.Ordinal);

    /// <summary>
    /// How the identity's private key is kept in the settings file, for this user only, and the account's name.
    /// The WPF app: Windows' DPAPI and the Windows account (FriendsService.Wpf.cs); the cross-platform app sets its own.
    /// </summary>
    internal static Func<byte[], byte[], byte[]> ProtectKey = (_, _) => throw new PlatformNotSupportedException();
    internal static Func<byte[], byte[], byte[]> UnprotectKey = (_, _) => throw new PlatformNotSupportedException();
    internal static Func<string> AccountName = () => Environment.UserName;
    /// <summary>What the account's other devices call this one: the computer's name.</summary>
    internal static Func<string> DeviceName = () => Environment.MachineName;

    readonly OwnDevices _devices = new();

    /// <summary>Tests run inboxes on this computer; Holm itself never has its inbox there.</summary>
    internal static bool AllowLoopbackServers;

    /// <summary>An inbox on this computer (localhost, 127.x, ::1): a test's, never a real one other devices can reach.</summary>
    internal static bool IsLoopbackServer(string? url)
        => url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    public FriendsService(AppSettings settings, Func<Action, Task> ui)
    {
        _settings = settings;
        _ui = ui;
        // An inbox on this computer or an unsafe server URL: nothing else reaches it. Holm's relay instead.
        if (_settings.FriendsServer != null
            && ((!AllowLoopbackServers && IsLoopbackServer(_settings.FriendsServer))
                || !SecurityGuards.IsSafePublicHttpsUri(FriendIdentity.NormalizeServer(_settings.FriendsServer), allowLoopbackWhenTesting: true)))
        {
            _settings.FriendsServer = null;
            _settings.FriendsRelay = true;
            _settings.Save();
            Diag.Log("Friends: the inbox server was unsafe or local; back to Holm's relay");
        }
        int removed = RemoveDuplicateFriendCards(_settings.Friends);
        if (removed > 0)
        {
            _settings.Save();
            Diag.Log($"Friends: removed {removed} duplicate or invalid saved entries");
        }
        if (Prune(Now)) _settings.Save();
        if (_settings.FriendsKey != null && Me is { } me && _settings.Friends.RemoveAll(f => f.Key == me.PublicKey) > 0)
        {
            _settings.Save();
            Diag.Log("Friends: our own card was among the friends; removed");
        }
    }

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Invite codes are kept a day past their end, for requests that wait in our inbox while Holm is closed.</summary>
    const long InviteGrace = 24 * 60 * 60;
    /// <summary>An unanswered request is forgotten after this long: the note it waits for is long gone from the server.</summary>
    const long PendingFor = 2 * 24 * 60 * 60;

    bool Prune(long now)
        => _settings.FriendInvites.RemoveAll(i => i.Expires + InviteGrace < now)
         + _settings.PendingFriendRequests.RemoveAll(p => p.Sent + PendingFor < now)
         + _settings.IncomingFriendRequests.RemoveAll(p => p.Sent + PendingFor < now) > 0;

    /// <summary>Our invite codes that still work, newest last.</summary>
    public IReadOnlyList<OwnInvite> Invites => _settings.FriendInvites.Where(i => i.Expires > Now).ToList();

    /// <summary>Requests we sent that haven't been answered yet.</summary>
    public IReadOnlyList<PendingFriendRequest> Pending => _settings.PendingFriendRequests;
    public IReadOnlyList<PendingFriendRequest> Incoming
        => _settings.IncomingFriendRequests.Where(p => p.Sent + PendingFor >= Now && Find(p.Card.Key) == null).ToList();

    /// <summary>
    /// The invite code to give a friend: the current one while it works for at least another hour, else a new one,
    /// whose sealed card is left on the server first.
    /// </summary>
    /// <exception cref="IOException">The server couldn't be reached.</exception>
    public async Task<OwnInvite> InviteAsync(bool fresh, CancellationToken ct)
    {
        var me = EnsureMe();
        long now = Now;
        Prune(now);
        if (!fresh && _settings.FriendInvites.LastOrDefault(i => i.Expires - now > 3600) is { } current) return current;
        string code = FriendInvite.New();
        long expires = now + (long)FriendInvite.Lifetime.TotalSeconds - 120; // a little before the server lets the card go
        await FriendInbox.PublishInviteAsync(FriendInvite.Topic(code), FriendInvite.Seal(code, me.FriendCode, expires), ct);
        var invite = new OwnInvite { Code = code, Proof = FriendInvite.Proof(code), Expires = expires };
        _settings.FriendInvites.Add(invite);
        _settings.Save();
        InvitesChanged?.Invoke();
        return invite;
    }

    /// <summary>Revokes an active invite code locally so future requests carrying its proof are not auto-accepted.</summary>
    public bool RevokeInvite(string code)
    {
        if (_settings.FriendInvites.RemoveAll(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        _settings.Save();
        InvitesChanged?.Invoke();
        return true;
    }

    /// <summary>Revokes all active invite codes locally.</summary>
    public void RevokeAllInvites()
    {
        if (_settings.FriendInvites.Count == 0) return;
        _settings.FriendInvites.Clear();
        _settings.Save();
        InvitesChanged?.Invoke();
    }

    /// <summary>
    /// Uses a friend's invite code: fetches their card and sends a request that carries the code's proof, which their
    /// notch accepts without asking. Waits up to <paramref name="wait"/> for that; after it, the request stays open
    /// and you become friends as soon as their Holm sees it.
    /// </summary>
    /// <exception cref="IOException">The server couldn't be reached.</exception>
    public async Task<(InviteResult Result, Friend? Card)> UseInviteAsync(string code, TimeSpan wait, CancellationToken ct)
    {
        if (_settings.FriendInvites.Any(i => i.Code == code)) return (InviteResult.OwnCode, null);
        Friend? card = null;
        for (int attempt = 0; attempt < 2 && card == null; attempt++)
        {
            // The server stores a new card a moment after it was left: a code made just now may need a second look.
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var cards = await FriendInbox.FetchInviteAsync(FriendInvite.Topic(code), ct);
            long now = Now;
            card = cards.Select(c => FriendInvite.Open(code, c, now)).FirstOrDefault(c => c != null);
        }
        if (card == null) return (InviteResult.NotFound, null);
        if (IsMe(card)) return (InviteResult.OwnCode, card);
        if (Find(card.Key) != null) return (InviteResult.AlreadyFriends, card);
        var result = await RequestAddAsync(card, wait, ct, FriendInvite.Proof(code));
        return (result == FriendRequestResult.Accepted ? InviteResult.Added : InviteResult.Sent, card);
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
            byte[] pkcs8 = UnprotectKey(Convert.FromBase64String(_settings.FriendsKey), KeyEntropy);
            var key = ECDiffieHellman.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            CryptographicOperations.ZeroMemory(pkcs8);
            return new FriendIdentity(MyName, _settings.FriendsTopic, OwnServer, key);
        }
        catch (Exception ex)
        {
            // E.g. settings copied from another PC or account: the key can't be decrypted here.
            Diag.Log("Friend identity unreadable, a new one will be made: " + ex.Message);
            return null;
        }
    }

    /// <summary>Where our friend code says our inbox is: the server set in the settings, else the relay once it let us in.</summary>
    string? OwnServer => _settings.FriendsServer ?? (_settings.FriendsRelay ? FriendIdentity.RelayServer : null);

    /// <summary>Our identity, made on first use. Friends who had an unreadable older one must add us again.</summary>
    public FriendIdentity EnsureMe()
    {
        if (Me is { } me) return me;
        me = FriendIdentity.Create(MyName, OwnServer);
        byte[] pkcs8 = me.Key.ExportPkcs8PrivateKey();
        _settings.FriendsKey = Convert.ToBase64String(ProtectKey(pkcs8, KeyEntropy));
        CryptographicOperations.ZeroMemory(pkcs8);
        _settings.FriendsTopic = me.Topic;
        // Catch up from now on: a friend may write while Holm isn't running.
        _settings.FriendsSince = _settings.FriendsRelaySince = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _settings.FriendsSeen.Clear();
        _settings.IncomingFriendRequests.Clear();
        _settings.Save();
        _me = me;
        Restart();
        return me;
    }

    /// <summary>The name in our profile, or the Windows account's.</summary>
    public string MyName => FriendProfile.CleanText(_settings.ProfileName, FriendProfile.MaxName) ?? AccountName();

    /// <summary>Our profile as friends get it.</summary>
    public FriendProfile MyProfile()
    {
        byte[]? picture = null;
        if (_settings.ProfilePicture != null)
        {
            try { picture = Convert.FromBase64String(_settings.ProfilePicture); } catch (FormatException) { }
        }
        return new FriendProfile(MyName, FriendProfile.CleanText(_settings.ProfileStatus, FriendProfile.MaxStatus),
                                 FriendProfile.CleanColor(_settings.ProfileColor), FriendProfile.IsPicture(picture) ? picture : null);
    }

    /// <summary>The profile was edited: our notes carry the new name, and friends get the new profile.</summary>
    public void ProfileChanged()
    {
        if (_me is { } me && me.Name != MyName)
        {
            _me = me.WithName(MyName); // the same key and inbox
            if (_follow != null) Restart(); // the inbox answers with our name too
        }
        SyncProfiles();
    }

    /// <summary>
    /// Sends our profile to each friend who doesn't have this version yet: after an edit, to a new friend, and to friends
    /// made before profiles existed. Nothing goes out before we have a friend code. A friend on a Holm before 1.8 leaves
    /// the note unopened.
    /// </summary>
    public void SyncProfiles()
    {
        if (!_settings.Beam || Me is not { } me) return;
        var profile = MyProfile();
        string version = profile.Version;
        foreach (var friend in _settings.Friends.ToList())
        {
            if (friend.ProfileSent == version) continue;
            if (_profilesOut.TryGetValue(friend.Key, out var sending) && sending == version) continue;
            _profilesOut[friend.Key] = version;
            string text = FriendNotes.Seal(me, friend, new FriendNote(FriendNoteKind.Profile, profile.Name,
                                                                      DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Profile: profile));
            _ = Task.Run(async () =>
            {
                bool sent = false;
                try { await FriendInbox.PostAsync(friend, text, CancellationToken.None); sent = true; }
                catch (IOException ex) { Diag.Log($"Friends: couldn't send the profile to {friend.Name}: {ex.Message}"); }
                await _ui(() =>
                {
                    if (_profilesOut.TryGetValue(friend.Key, out var v) && v == version) _profilesOut.Remove(friend.Key);
                    if (!sent || Find(friend.Key) is not { } current) return;
                    current.ProfileSent = version;
                    _settings.Save();
                });
            });
        }
    }

    // ───────────── Holm account (Account/AccountService.cs) ─────────────

    /// <summary>Our identity for the account to keep (sealed): the inbox and the private key. Null before we have one.</summary>
    internal Account.IdentityPayload? ExportIdentity()
    {
        if (Me is not { } me) return null;
        byte[] pkcs8 = me.Key.ExportPkcs8PrivateKey();
        try { return new Account.IdentityPayload { Topic = me.Topic, Server = _settings.FriendsServer, Key = Convert.ToBase64String(pkcs8) }; }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    /// <summary>
    /// Becomes the account's identity, so friends reach us here with the same friend code as on our other PCs.
    /// The inbox is caught up on what the servers keep (ntfy 12 hours, the relay a week), as after a restart.
    /// </summary>
    internal void AdoptIdentity(Account.IdentityPayload identity)
    {
        byte[] pkcs8 = Convert.FromBase64String(identity.Key);
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            _settings.FriendsKey = Convert.ToBase64String(ProtectKey(pkcs8, KeyEntropy));
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
        _settings.FriendsTopic = identity.Topic;
        if (identity.Server != null
            && ((!AllowLoopbackServers && IsLoopbackServer(identity.Server))
                || !SecurityGuards.IsSafePublicHttpsUri(FriendIdentity.NormalizeServer(identity.Server), allowLoopbackWhenTesting: true)))
        {
            _settings.FriendsServer = null;
            _settings.FriendsRelay = true;
        }
        else _settings.FriendsServer = identity.Server;
        _settings.FriendsSince = Now - 12 * 60 * 60;
        _settings.FriendsRelaySince = Now - 7 * 24 * 60 * 60; // what the relay keeps
        _settings.FriendsSeen.Clear();
        _settings.IncomingFriendRequests.Clear();
        _me = new FriendIdentity(MyName, identity.Topic, OwnServer, key);
        _loaded = true;
        _settings.Save();
        Restart();
        Diag.Log("Friends: took the account's identity");
    }

    /// <summary>Puts the account's friends list in place of ours (already merged with ours).</summary>
    internal void ReplaceFriends(IReadOnlyList<Friend> friends)
    {
        _settings.Friends.Clear();
        _settings.Friends.AddRange(friends);
        _settings.Save();
        ListChanged?.Invoke();
        SyncProfiles();
    }

    /// <summary>
    /// After taking the account's identity: friends who only knew this PC's old one get our new friend code, and their
    /// notch offers to add us back (as when someone adds them).
    /// </summary>
    internal void Reintroduce(IEnumerable<Friend> friends)
    {
        if (Me is not { } me) return;
        foreach (var friend in friends.ToList())
        {
            var note = new FriendNote(FriendNoteKind.AddedYou, me.Name, Now, Code: me.FriendCode);
            string text = FriendNotes.Seal(me, friend, note);
            _ = Task.Run(async () =>
            {
                try { await FriendInbox.PostAsync(friend, text, CancellationToken.None); }
                catch (IOException ex) { Diag.Log($"Friends: couldn't tell {friend.Name} our new code: {ex.Message}"); }
            });
        }
    }

    public Friend? Find(string publicKey) => _settings.Friends.FirstOrDefault(f => f.Key == publicKey);

    // ───────────── the account's other devices ─────────────

    /// <summary>This device's id among the account's devices, made on first use.</summary>
    public string DeviceId
    {
        get
        {
            if (!OwnDevice.IsId(_settings.DeviceId))
            {
                _settings.DeviceId = OwnDevice.NewId();
                _settings.Save();
            }
            return _settings.DeviceId!;
        }
    }

    /// <summary>The account's other devices that spoke lately (they share our identity: Sharing/OwnDevices.cs).</summary>
    public IReadOnlyList<OwnDevice> OwnDevicesNow => _follow == null ? Array.Empty<OwnDevice>() : _devices.Recent(DateTime.UtcNow);

    /// <summary>Our own inbox as a friend card: what notes to another device of ours are sealed for and posted to.</summary>
    Friend OwnCard(FriendIdentity me, string name) => new() { Name = name, Topic = me.Topic, Key = me.PublicKey, Server = me.Server };

    /// <summary>The device as a card to show and send to (the name is the device's; the inbox is our own).</summary>
    public Friend CardOf(OwnDevice device) => OwnCard(EnsureMe(), device.Name);

    /// <summary>Like <see cref="InviteAsync"/>, for one of our own devices: only that one comes to the relay.</summary>
    /// <exception cref="IOException">The note couldn't be delivered.</exception>
    public Task InviteDeviceAsync(OwnDevice device, byte[] secret, CancellationToken ct)
    {
        var me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.Files, DeviceName(), Now, Secret: secret, Device: DeviceId, ForDevice: device.Id);
        return FriendInbox.PostAsync(OwnCard(me, device.Name), FriendNotes.Seal(me, OwnCard(me, device.Name), note), ct);
    }

    /// <summary>Sending to it just failed: it's gone.</summary>
    public void ForgetDevice(OwnDevice device)
    {
        _devices.Forget(device.Id);
        OwnDevicesChanged?.Invoke();
    }

    /// <summary>
    /// Says in our inbox that this device runs, for the account's other PCs; with <paramref name="query"/> it asks
    /// them to say so too. Live only: the server passes it on and doesn't keep it.
    /// </summary>
    void AnnounceDevice(bool query)
    {
        if (_follow == null || Me is not { } me) return;
        string text = FriendNotes.Seal(me, OwnCard(me, ""), new FriendNote(FriendNoteKind.Device, DeviceName(), Now, Device: DeviceId, Query: query ? true : null));
        _ = Task.Run(async () =>
        {
            try { await FriendInbox.PostAsync(OwnCard(me, ""), text, CancellationToken.None, keep: false); }
            catch (IOException ex) { Diag.Log("Friends: couldn't reach our own inbox: " + ex.Message); }
        });
    }

    /// <summary>A note sealed by our own identity: from another PC of this account, or our own coming back.</summary>
    void HandleOwn(FriendIdentity me, FriendNote note, long age)
    {
        if (note.Device is not { } id || id == DeviceId) return;
        switch (note.Kind)
        {
            case FriendNoteKind.Device when age <= FreshSeconds && IsTimestampFresh(note.Sent, FreshSeconds):
                if (_devices.Saw(id, note.From, DateTime.UtcNow)) OwnDevicesChanged?.Invoke();
                if (note.Query == true) AnnounceDevice(query: false);
                break;
            case FriendNoteKind.Files when note.ForDevice != DeviceId:
                break; // for another of our devices
            case FriendNoteKind.Files when age > Patience.TotalSeconds || !IsTimestampFresh(note.Sent, (long)Patience.TotalSeconds):
                Diag.Log("Friends: another device of ours wanted to send files while Holm wasn't running");
                break;
            case FriendNoteKind.Files:
                if (_devices.Saw(id, note.From, DateTime.UtcNow)) OwnDevicesChanged?.Invoke();
                FilesOffered?.Invoke(OwnCard(me, _devices.Find(id)?.Name ?? note.From), note.Secret!);
                break;
        }
    }

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
        ListChanged?.Invoke();
        SyncProfiles();
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

    /// <summary>
    /// Sends a friend request and adds the card only after the other person accepts. The request is kept until it's
    /// answered: if nobody answers within <paramref name="timeout"/> it stays open (a later yes still makes you friends,
    /// see <see cref="LateAnswer"/>); only cancelling through <paramref name="ct"/> withdraws it.
    /// <paramref name="invite"/> is the proof of their invite code, if the request was made with one.
    /// </summary>
    public async Task<FriendRequestResult> RequestAddAsync(Friend friend, TimeSpan timeout, CancellationToken ct, string? invite = null)
    {
        if (Find(friend.Key) is { } existing)
        {
            UpdateFriendCard(existing, friend);
            return FriendRequestResult.Accepted;
        }

        var me = EnsureMe(); // starts our inbox before the request is posted
        var previous = _settings.PendingFriendRequests.FirstOrDefault(p => p.Card.Key == friend.Key);
        string requestId = previous?.RequestId ?? Guid.NewGuid().ToString("N");
        var answer = new TaskCompletionSource<FriendRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_friendRequests.TryAdd(requestId, answer)) return FriendRequestResult.NoResponse;
        var pending = new PendingFriendRequest { RequestId = requestId, Card = friend, Sent = Now, ByInvite = invite != null };
        _settings.PendingFriendRequests.RemoveAll(p => p.Card.Key == friend.Key);
        _settings.PendingFriendRequests.Add(pending);
        _settings.Save();
        InvitesChanged?.Invoke();
        try
        {
            var note = new FriendNote(FriendNoteKind.FriendRequest, me.Name, Now, Code: me.FriendCode, RequestId: requestId, Invite: invite);
            await FriendInbox.PostAsync(friend, FriendNotes.Seal(me, friend, note), ct);
            try
            {
                var result = await answer.Task.WaitAsync(timeout, ct);
                ct.ThrowIfCancellationRequested();
                return result; // the answer itself added them (HandleNote)
            }
            catch (TimeoutException) { return FriendRequestResult.NoResponse; } // stays open
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CancelRequestAsync(pending);
            throw;
        }
        catch (IOException)
        {
            if (previous == null) ForgetPending(requestId);
            throw;
        }
        finally { _friendRequests.TryRemove(requestId, out _); }
    }

    void ForgetPending(string requestId)
    {
        if (_settings.PendingFriendRequests.RemoveAll(p => p.RequestId == requestId) == 0) return;
        _settings.Save();
        InvitesChanged?.Invoke();
    }

    /// <summary>Withdraws a request we sent: tells them, and forgets it here.</summary>
    public async Task CancelRequestAsync(PendingFriendRequest pending)
    {
        ForgetPending(pending.RequestId);
        if (Me is not { } me) return;
        try { await SendFriendRequestCancellationAsync(me, pending.Card, pending.RequestId); }
        catch (IOException ex) { Diag.Log($"Couldn't withdraw the request to {pending.Card.Name}: {ex.Message}"); }
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
        ForgetIncoming(friend.Key, requestId);
    }

    void ForgetIncoming(string sender, string requestId)
    {
        if (_settings.IncomingFriendRequests.RemoveAll(p => p.RequestId == requestId && p.Card.Key == sender) == 0) return;
        _settings.Save();
        InvitesChanged?.Invoke();
    }

    void AddOrUpdateFriend(Friend card, string? requestId = null)
    {
        if (Find(card.Key) is { } existing) UpdateFriendCard(existing, card);
        else
        {
            card.AddedByRequestId = requestId;
            _settings.Friends.Add(card);
            _settings.Save();
            ListChanged?.Invoke();
            SyncProfiles();
        }
    }

    static bool HasSafeServer(Friend card)
        => card.Server == null || SecurityGuards.IsSafePublicHttpsUri(FriendIdentity.NormalizeServer(card.Server), allowLoopbackWhenTesting: true);

    void UpdateFriendCard(Friend existing, Friend card)
    {
        if (!HasSafeServer(card)) return;
        int at = _settings.Friends.IndexOf(existing);
        if (at < 0) return;
        card.AddedByRequestId ??= existing.AddedByRequestId;
        card.KeepProfileOf(existing);
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
        ListChanged?.Invoke();

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

    /// <summary>Leaves a note asking the friend's notch to watch our stream, meeting on the relay with <paramref name="secret"/>.</summary>
    /// <exception cref="IOException">The note couldn't be delivered.</exception>
    public Task InviteToStreamAsync(Friend to, byte[] secret, CancellationToken ct)
    {
        var me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.Stream, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret: secret);
        return FriendInbox.PostAsync(to, FriendNotes.Seal(me, to, note), ct);
    }

    /// <summary>Waves at a friend. False when it's too soon after the last wave (see <see cref="WaveLimit"/>).</summary>
    public bool Wave(Friend to)
    {
        if (!_wavesOut.Allow(to.Key, DateTime.UtcNow)) return false;
        Tell(EnsureMe(), to, FriendNoteKind.Wave);
        return true;
    }

    /// <summary>Sends one chat message (<see cref="ChatText.MaxBytes"/> at most).</summary>
    /// <exception cref="IOException">The note couldn't be delivered to their inbox.</exception>
    public Task SendChatAsync(Friend to, string id, string text, long sent, CancellationToken ct, string? gif = null)
    {
        var me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.Chat, me.Name, sent, Text: gif ?? text, MessageId: id, Gif: gif);
        return FriendInbox.PostAsync(to, FriendNotes.Seal(me, to, note), ct);
    }

    /// <summary>Reacts to a chat message with an emoji, or takes the reaction away (null).</summary>
    /// <exception cref="IOException">The note couldn't be delivered to their inbox.</exception>
    public Task SendReactionAsync(Friend to, string messageId, string? emoji, CancellationToken ct)
    {
        var me = EnsureMe();
        var note = new FriendNote(FriendNoteKind.Reaction, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text: emoji, MessageId: messageId);
        return FriendInbox.PostAsync(to, FriendNotes.Seal(me, to, note), ct);
    }

    /// <summary>Asks one friend whether their Holm runs (if not asked a minute ago); <see cref="FriendOnline"/> follows if so.</summary>
    public void AskIfHere(Friend friend)
    {
        if (_follow == null || Me is not { } me) return; // the answer comes to our inbox
        if (_presence.ShouldAsk(friend.Key, DateTime.UtcNow)) Tell(me, friend, FriendNoteKind.Ping);
    }

    /// <summary>Leaves a note that something waits; their Holm answers when it next runs.</summary>
    public void TellWaiting(Friend friend)
    {
        if (_follow == null || Me is not { } me) return;
        Tell(me, friend, FriendNoteKind.Waiting);
    }

    /// <summary>Sending to them just failed: they're not online, whatever they said a minute ago.</summary>
    public void MarkOffline(Friend friend) => _presence.Forget(friend.Key);

    /// <summary>How long until the next wave to this friend may go.</summary>
    public TimeSpan WaveWait(Friend to) => _wavesOut.Wait(to.Key, DateTime.UtcNow);

    /// <summary>Follows our inbox while Beam is on and we have an identity; costs nothing otherwise.</summary>
    public void Restart()
    {
        Stop();
        if (!_settings.Beam || Me is not { } me) return;
        var cts = new CancellationTokenSource();
        _follow = cts;
        Func<string, long, string, Task> received = (id, age, text) => _ui(() => { if (_follow == cts) Handle(id, age, text); });
        // Our inbox on the server the settings name, or else on ntfy (where older friends write) and on the relay.
        _ = Task.Run(() => FriendInbox.FollowAsync(_settings.FriendsServer, me.Topic, _settings.FriendsSince, received,
            time => _ = _ui(() => { if (_follow == cts) { _settings.FriendsSince = time; _settings.Save(); } }),
            Diag.Log, cts.Token));
        if (_settings.FriendsServer == null)
            _ = Task.Run(() => FriendInbox.FollowRelayAsync(me.Topic, _settings.FriendsRelaySince ?? Now, received,
                time => _ = _ui(() => { if (_follow == cts) { _settings.FriendsRelaySince = time; _settings.Save(); } }),
                () => _ = _ui(() => { if (_follow == cts) MoveToRelay(); }),
                Diag.Log, cts.Token, me.RelayReadToken));
        SyncProfiles();
        AnnounceDevice(query: true);
    }

    /// <summary>
    /// The relay let us in for the first time: from now on our friend code names it, and every friend gets the new code,
    /// which their Holm (any version) takes in silently for a friend it has. Their notes then come to the relay; a friend
    /// that hasn't heard yet still writes to ntfy, which we keep following.
    /// </summary>
    internal void MoveToRelay()
    {
        if (_settings.FriendsRelay || _settings.FriendsServer != null || Me is not { } me) return;
        _settings.FriendsRelay = true;
        _settings.Save();
        _me = new FriendIdentity(me.Name, me.Topic, FriendIdentity.RelayServer, me.Key);
        Diag.Log("Friends: our inbox moved to the relay");
        Reintroduce(_settings.Friends);
    }

    public void Stop()
    {
        _follow?.Cancel();
        _follow = null;
    }

    /// <summary>What the friend is doing, as their notch last said; null when they're offline, idle or don't share it.</summary>
    public FriendActivity? ActivityOf(Friend friend)
        => IsOnline(friend) && _activities.TryGetValue(friend.Key, out var activity) ? activity : null;

    /// <summary>How many achievements the friend has, as their notch last said; null if it never said.</summary>
    public int? TrophiesOf(Friend friend) => _trophies.TryGetValue(friend.Key, out int n) ? n : null;

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
        // The account's other devices too (other PCs), at most once a minute.
        if (_presence.ShouldAsk(me.PublicKey, DateTime.UtcNow)) AnnounceDevice(query: true);
    }

    void Tell(FriendIdentity me, Friend to, FriendNoteKind kind)
    {
        // Asking or answering "are you there?" carries what we're doing, like a status in a chat app.
        bool presence = kind is FriendNoteKind.Ping or FriendNoteKind.Here;
        var activity = presence ? MyActivity?.Invoke() : null;
        int? trophies = presence ? MyTrophies?.Invoke() : null;
        string text = FriendNotes.Seal(me, to, new FriendNote(kind, me.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Activity: activity, Trophies: trophies));
        _ = Task.Run(async () =>
        {
            // "Are you there?" and its answer are stale after 30 seconds: the server passes them on without keeping them.
            try { await FriendInbox.PostAsync(to, text, CancellationToken.None, keep: !presence); }
            catch (IOException ex) { Diag.Log($"Friends: couldn't reach {to.Name}'s inbox: {ex.Message}"); }
        });
    }

    void SawOnline(Friend friend)
    {
        if (_presence.Saw(friend.Key, DateTime.UtcNow)) FriendOnline?.Invoke(friend);
    }

    static bool IsTimestampFresh(long sent, long maxAgeSeconds)
    {
        long now = Now;
        return sent >= now - maxAgeSeconds - MaxClockSkewSeconds && sent <= now + MaxClockSkewSeconds;
    }

    static string ReplayKeyFor(FriendNote note, string rawText)
        => note.Nonce != null
            ? "n:" + note.Nonce
            : "h:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawText)))[..24];

    bool RememberSeen(string key)
    {
        if (_settings.FriendsSeen.Contains(key)) return false;
        _settings.FriendsSeen.Add(key);
        if (_settings.FriendsSeen.Count > MaxSeenEntries)
            _settings.FriendsSeen.RemoveRange(0, _settings.FriendsSeen.Count - MaxSeenEntries);
        return true;
    }

    void Handle(string id, long age, string text)
    {
        if (_me is not { } me) return;
        age = Math.Max(0, age);
        // Catching up after a reconnect can repeat the last note by server id.
        if (!RememberSeen(id)) return;

        if (FriendNotes.Open(me, text, out string sender) is not { } note)
        {
            Diag.Log("Friends: a note that isn't for us");
            return;
        }

        // Replay protection across servers and restarts using authenticated note nonce or ciphertext digest (HOLM-SEC-006).
        string replayKey = ReplayKeyFor(note, text);
        if (!RememberSeen(replayKey))
        {
            Diag.Log("Friends: ignored replayed note");
            return;
        }
        _settings.Save();

        if (sender == me.PublicKey)
        {
            HandleOwn(me, note, age);
            return;
        }
        var friend = Find(sender);
        switch (note.Kind)
        {
            case FriendNoteKind.Files when friend == null:
                Diag.Log("Friends: someone who isn't a friend wanted to send files");
                break;
            case FriendNoteKind.Files when age > Patience.TotalSeconds || !IsTimestampFresh(note.Sent, (long)Patience.TotalSeconds):
                Diag.Log($"Friends: {friend.Name} wanted to send files while Holm wasn't running");
                break;
            case FriendNoteKind.Files:
                FilesOffered?.Invoke(friend, note.Secret!);
                break;
            case FriendNoteKind.AddedYou when FriendIdentity.ParseFriendCode(note.Code) is { } card && HasSafeServer(card):
                if (friend != null)
                {
                    // Already friends: keep their name and inbox current.
                    UpdateFriendCard(friend, card);
                }
                else AddedUs?.Invoke(card);
                break;
            case FriendNoteKind.FriendRequest when FriendIdentity.ParseFriendCode(note.Code) is { } card && HasSafeServer(card) && note.RequestId is { } requestId:
                if (friend != null)
                {
                    // Repair an older one-sided relationship automatically when the other person requests us.
                    UpdateFriendCard(friend, card);
                    _ = ReplyToRequestAsync(me, card, requestId, accepted: true);
                }
                else if (age <= PendingFor && !_cancelledFriendRequests.Contains(requestId))
                {
                    bool isNew = !_settings.IncomingFriendRequests.Any(p => p.RequestId == requestId && p.Card.Key == sender);
                    if (!isNew) break;
                    long arrival = Now - age;
                    _settings.IncomingFriendRequests.Add(new PendingFriendRequest { Card = card, RequestId = requestId, Sent = arrival });
                    _settings.Save();
                    InvitesChanged?.Invoke();
                    // Enforce arrival time AND authenticated note.Sent against invite expiry so backdated dto.T cannot bypass expiry (HOLM-SEC-006).
                    // Consume invite on redemption (single-use, HOLM-SEC-028).
                    OwnInvite? matched = note.Invite is { } proof
                        ? _settings.FriendInvites.FirstOrDefault(i => i.Proof == proof
                            && arrival <= i.Expires + 300
                            && note.Sent <= i.Expires + 300
                            && Math.Abs(note.Sent - arrival) <= 600)
                        : null;
                    if (matched != null)
                    {
                        _settings.FriendInvites.Remove(matched);
                        _settings.Save();
                        InvitesChanged?.Invoke();
                        _ = AcceptInviteAsync(card, requestId);
                    }
                    else FriendRequested?.Invoke(card, requestId);
                }
                break;
            case FriendNoteKind.FriendResponse when note.RequestId is { } responseId && note.Accepted is { } accepted:
            {
                // Only the person we asked can answer, and the answer counts whenever it comes.
                var pending = _settings.PendingFriendRequests.FirstOrDefault(p => p.RequestId == responseId && p.Card.Key == sender);
                bool waiting = _friendRequests.TryGetValue(responseId, out var answer);
                if (pending != null)
                {
                    ForgetPending(responseId);
                    if (accepted) AddOrUpdateFriend(pending.Card, responseId);
                    if (!waiting) LateAnswer?.Invoke(pending.Card, accepted);
                }
                if (waiting && pending != null) answer!.TrySetResult(accepted ? FriendRequestResult.Accepted : FriendRequestResult.Declined);
                break;
            }
            case FriendNoteKind.FriendRequestCancelled when FriendIdentity.ParseFriendCode(note.Code) is { } card && note.RequestId is { } cancelledId:
                _cancelledFriendRequests.Add(cancelledId);
                ForgetIncoming(sender, cancelledId);
                RemoveFriendAddedByRequest(cancelledId);
                FriendRequestCancelled?.Invoke(card, cancelledId);
                break;
            case FriendNoteKind.RemovedYou when friend != null:
                _settings.Friends.RemoveAll(f => f.Key == friend.Key);
                _settings.Save();
                Diag.Log($"Friends: {friend.Name} removed us");
                RemovedUs?.Invoke(friend);
                ListChanged?.Invoke();
                break;
            case FriendNoteKind.RemovedYou:
                Diag.Log("Friends: ignored a removal note from someone who isn't a friend");
                break;
            case FriendNoteKind.Ping or FriendNoteKind.Here when friend == null || age > FreshSeconds || !IsTimestampFresh(note.Sent, FreshSeconds):
                break; // strangers don't learn whether we're online; old ones came while Holm wasn't running
            case FriendNoteKind.Ping:
                Took(friend, note.Activity, note.Trophies);
                SawOnline(friend);
                if (_presence.ShouldAnswer(friend.Key, DateTime.UtcNow)) Tell(me, friend, FriendNoteKind.Here);
                break;
            case FriendNoteKind.Here:
                Took(friend, note.Activity, note.Trophies);
                SawOnline(friend);
                break;
            case FriendNoteKind.Waiting when friend == null || age > WaitingSeconds || !IsTimestampFresh(note.Sent, WaitingSeconds):
                break; // the inbox server keeps notes for about 12 hours anyway
            case FriendNoteKind.Waiting:
                // They have something for us and will send it once they hear we're here.
                if (age <= FreshSeconds) SawOnline(friend);
                if (_presence.ShouldAnswer(friend.Key, DateTime.UtcNow)) Tell(me, friend, FriendNoteKind.Here);
                break;
            case FriendNoteKind.Wave when friend == null || age > WaveLimit.FreshSeconds || !IsTimestampFresh(note.Sent, WaveLimit.FreshSeconds):
                break; // strangers can't wave; an old wave came while Holm wasn't running
            case FriendNoteKind.Wave:
                SawOnline(friend);
                if (_settings.Waves && _wavesIn.Allow(friend.Key, DateTime.UtcNow)) Waved?.Invoke(friend);
                break;
            case FriendNoteKind.Stream when friend == null || age > StreamPatience.TotalSeconds || !IsTimestampFresh(note.Sent, (long)StreamPatience.TotalSeconds):
                break; // strangers can't stream to us; an old invitation came while Holm wasn't running
            case FriendNoteKind.Stream:
                SawOnline(friend);
                StreamOffered?.Invoke(friend, note.Secret!);
                break;
            case FriendNoteKind.Chat when friend == null:
                Diag.Log("Friends: a chat message from someone who isn't a friend");
                break;
            case FriendNoteKind.Chat:
                if (age <= FreshSeconds) SawOnline(friend);
                if (note.Gif != null) GifReceived?.Invoke(friend, note.MessageId!, note.Gif, note.Sent);
                else ChatReceived?.Invoke(friend, note.MessageId!, note.Text!, note.Sent);
                break;
            case FriendNoteKind.Reaction when friend == null:
                break; // only friends react
            case FriendNoteKind.Reaction:
                if (age <= FreshSeconds) SawOnline(friend);
                ReactionReceived?.Invoke(friend, note.MessageId!, note.Text);
                break;
            case FriendNoteKind.Profile when friend == null || note.Profile == null:
                break; // only friends show up with a picture
            case FriendNoteKind.Profile:
                friend.TakeProfile(note.Profile);
                _settings.Save();
                ProfileReceived?.Invoke(friend);
                SyncProfiles(); // they may not have ours yet, e.g. added before profiles existed
                break;
        }
    }

    void Took(Friend friend, FriendActivity? activity, int? trophies)
    {
        bool had = _activities.TryGetValue(friend.Key, out var before);
        _activities[friend.Key] = activity;
        bool more = trophies is { } n && (!_trophies.TryGetValue(friend.Key, out int was) || was != n);
        if (trophies is { } count) _trophies[friend.Key] = count;
        if (!had || before != activity || more) ActivityChanged?.Invoke(friend);
    }

    /// <summary>Someone who had our invite code asked: say yes and add them.</summary>
    async Task AcceptInviteAsync(Friend card, string requestId)
    {
        try
        {
            await RespondToFriendRequestAsync(card, requestId, accepted: true);
            Diag.Log("Friends: someone joined with our invite code");
            InviteAccepted?.Invoke(card);
        }
        catch (IOException ex)
        {
            Diag.Log("Friends: couldn't answer an invite request: " + ex.Message);
            FriendRequested?.Invoke(card, requestId); // ask the person instead
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

    /// <summary>They're not online after all (a send to them just failed).</summary>
    public void Forget(string key) => _seen.Remove(key);

    /// <summary>Whether to answer their question now.</summary>
    public bool ShouldAnswer(string key, DateTime now)
    {
        if (_answered.TryGetValue(key, out var at) && now - at < AnswerEvery) return false;
        _answered[key] = now;
        return true;
    }
}
