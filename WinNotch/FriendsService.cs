using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Friends on this PC: our identity (the private key is encrypted for this Windows account), the friends list,
/// and our inbox, followed while Beam is on and we have a friend code. Settings are only touched on the UI thread.
/// </summary>
internal sealed class FriendsService
{
    static readonly byte[] KeyEntropy = "WinNotch friends key"u8.ToArray(); // former name: keys saved before the rename must still open

    /// <summary>How long the sender waits for the friend's notch to answer; older invitations are stale.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    readonly AppSettings _settings;
    readonly Dispatcher _ui;
    FriendIdentity? _me;
    bool _loaded;
    CancellationTokenSource? _follow;

    /// <summary>A friend wants to send us files; meet them on the relay with the secret (UI thread).</summary>
    public event Action<Friend, byte[]>? FilesOffered;
    /// <summary>Someone added us as a friend; this is their card, to add them back (UI thread).</summary>
    public event Action<Friend>? AddedUs;
    /// <summary>A friend removed us from their list (UI thread).</summary>
    public event Action<Friend>? RemovedUs;

    public FriendsService(AppSettings settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
    }

    public IReadOnlyList<Friend> Friends => _settings.Friends;

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
            case FriendNoteKind.RemovedYou when friend != null:
                _settings.Friends.RemoveAll(f => f.Key == friend.Key);
                _settings.Save();
                Diag.Log($"Friends: {friend.Name} removed us");
                RemovedUs?.Invoke(friend);
                break;
            case FriendNoteKind.RemovedYou:
                Diag.Log("Friends: ignored a removal note from someone who isn't a friend");
                break;
        }
    }
}
