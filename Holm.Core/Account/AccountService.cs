using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Sharing;

namespace WinNotch.Account;

/// <summary>
/// The Holm account on this PC: signing up, in and out, deleting it, and keeping settings, profile, achievements,
/// friends and the friends identity in sync with the account's other PCs. Optional: without an account Holm works as
/// before. The session (refresh token) and the data key are kept in account.json, encrypted for this user like the
/// friends key. Settings, friends and achievements are only touched on the UI thread.
/// </summary>
internal sealed class AccountService : IDisposable
{
    static readonly byte[] Entropy = "Holm account"u8.ToArray();
    /// <summary>A change is synced this long after it was saved, so a burst of changes goes up once.</summary>
    static readonly TimeSpan Debounce = TimeSpan.FromSeconds(8);
    /// <summary>The account's changes from other PCs are fetched this often while Holm runs (and at every start).</summary>
    // shortcut: a sync reads 5 documents (24 a day per PC at this pace); one document per account would cut that to 1, once the free tier gets tight.
    static readonly TimeSpan PullEvery = TimeSpan.FromHours(1);

    readonly AppSettings _settings;
    readonly AchievementBook _achievements;
    readonly FriendsService _friends;
    readonly Func<Action, Task> _ui;
    readonly FirebaseClient _firebase;
    readonly string _path;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Timer _soon, _every;
    AccountFile _file;
    FirebaseSession? _session;
    byte[]? _dataKey;
    bool _applying, _disposed;
    /// <summary>The person's answer when this PC's friends identity and the account's differ (null: not asked yet).</summary>
    bool? _identityChoice;

    /// <summary>Signed in or out, syncing started or ended, an error (UI thread).</summary>
    public event Action? Changed;
    /// <summary>
    /// This PC has friends under its own identity and the account has another one: ask which to keep, then call
    /// <see cref="ChooseIdentityAsync"/> (UI thread).
    /// </summary>
    public event Action? IdentityQuestion;
    /// <summary>
    /// Settings came from the account. The UI wraps the change (its argument) to run what each changed switch does,
    /// as if it had been flipped here.
    /// </summary>
    public Action<Action>? ApplyingSettings { get; set; }
    /// <summary>The profile came from the account (UI thread).</summary>
    public event Action? ProfileApplied;

    public AccountService(AppSettings settings, AchievementBook achievements, FriendsService friends, Func<Action, Task> ui, string folder,
                          FirebaseClient? firebase = null)
    {
        _settings = settings;
        _achievements = achievements;
        _friends = friends;
        _ui = ui;
        _firebase = firebase ?? new FirebaseClient(FirebaseProject.ApiKey, FirebaseProject.ProjectId);
        _path = Path.Combine(folder, "account.json");
        _file = Load(_path);
        _soon = new Timer(_ => _ = SyncQuietlyAsync());
        _every = new Timer(_ => _ = SyncQuietlyAsync());
        AppSettings.Saved += OnLocalChange;
        _achievements.Changed += OnLocalChange;
    }

    /// <summary>Whether this build knows the account service (a build without the Firebase project's details doesn't).</summary>
    public static bool Available => FirebaseProject.IsConfigured;

    public bool SignedIn => _file.Uid != null;
    public string? Email => _file.Email;
    public bool Syncing { get; private set; }
    public DateTimeOffset? LastSync => _file.LastSync is long t and > 0 ? DateTimeOffset.FromUnixTimeSeconds(t) : null;
    /// <summary>Why the last sync failed, in words for the person; null when it worked.</summary>
    public string? Problem { get; private set; }

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Starts syncing if signed in: now, then every <see cref="PullEvery"/> and after changes.</summary>
    public void Start()
    {
        if (!SignedIn || !Available) return;
        _every.Change(TimeSpan.FromSeconds(5), PullEvery);
    }

    void OnLocalChange()
    {
        if (_applying || !SignedIn || AppSettings.ReadOnly || _disposed) return;
        // Holm saves its settings for many things the account doesn't keep (the friends inbox's catch-up point, every
        // 45 seconds while it's followed): only a change to what the account keeps is worth a sync.
        try { if (_synced != null && LocalFingerprint() == _synced) return; }
        catch (InvalidOperationException) { } // a list changed while it was read (saved off the UI thread): sync to be safe
        _soon.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    /// <summary><see cref="LocalFingerprint"/> after the last sync; null before the first.</summary>
    string? _synced;
    /// <summary>Whether <see cref="IdentityQuestion"/> was raised and not answered yet: it's asked once, not at every sync.</summary>
    bool _identityAsked;
    /// <summary>Rounds that reached the account (for tests).</summary>
    internal int Rounds { get; private set; }

    /// <summary>Everything the account keeps from this PC, as one hash (UI thread).</summary>
    string LocalFingerprint()
    {
        var text = new System.Text.StringBuilder();
        foreach (var (name, value) in SettingsSync.Capture(_settings)) text.Append(name).Append('=').Append(value.GetRawText()).Append('\n');
        var profile = ProfileSync.Capture(_settings);
        text.Append(profile.Name).Append('\n').Append(profile.Status).Append('\n').Append(profile.Color).Append('\n').Append(profile.Picture).Append('\n');
        foreach (var f in _settings.Friends) text.Append(f.Key).Append('|').Append(f.Name).Append('|').Append(f.Status).Append('|').Append(f.Color).Append('|').Append(f.Picture).Append('\n');
        text.Append(_settings.FriendsTopic).Append('|').Append(_settings.FriendsKey).Append('\n');
        text.Append(System.Text.Encoding.UTF8.GetString(AccountJson.ToBytes(_achievements.Export())));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
    }

    // ───────────── Signing up, in, out ─────────────

    /// <summary>Makes a new account and puts this PC's data in it.</summary>
    /// <exception cref="AccountException">Firebase said no (the message says why) or couldn't be reached.</exception>
    public async Task SignUpAsync(string email, string password, CancellationToken ct)
    {
        email = AccountKeys.NormalizeEmail(email);
        var keys = await Task.Run(() => AccountKeys.Derive(email, password), ct).ConfigureAwait(false);
        var session = await _firebase.SignUpAsync(email, keys.SignInSecret, ct).ConfigureAwait(false);
        byte[] dataKey = AccountVault.NewDataKey();
        await _firebase.PutAsync(UserPath(session.Uid), new Dictionary<string, string> { ["vault"] = AccountVault.WrapDataKey(keys, session.Uid, dataKey) },
                                 null, session.IdToken, ct).ConfigureAwait(false);
        await BeginAsync(session, dataKey).ConfigureAwait(false);
    }

    /// <summary>Signs in to an existing account; its data and this PC's are put together.</summary>
    /// <exception cref="AccountException">Wrong email or password, or Firebase couldn't be reached.</exception>
    public async Task SignInAsync(string email, string password, CancellationToken ct)
    {
        email = AccountKeys.NormalizeEmail(email);
        var keys = await Task.Run(() => AccountKeys.Derive(email, password), ct).ConfigureAwait(false);
        var session = await _firebase.SignInAsync(email, keys.SignInSecret, ct).ConfigureAwait(false);
        var user = await _firebase.GetAsync(UserPath(session.Uid), session.IdToken, ct).ConfigureAwait(false);
        byte[] dataKey;
        if (user?.Fields.GetValueOrDefault("vault") is { } wrapped)
        {
            try { dataKey = AccountVault.UnwrapDataKey(keys, session.Uid, wrapped); }
            catch (CryptographicException ex) { throw new AccountException("The account's data can't be opened with this password.", "VAULT", ex); }
        }
        else
        {
            // Signed up, but the key never reached the account (the connection dropped): make it now.
            dataKey = AccountVault.NewDataKey();
            await _firebase.PutAsync(UserPath(session.Uid), new Dictionary<string, string> { ["vault"] = AccountVault.WrapDataKey(keys, session.Uid, dataKey) },
                                     user?.UpdateTime, session.IdToken, ct).ConfigureAwait(false);
        }
        await BeginAsync(session, dataKey).ConfigureAwait(false);
    }

    async Task BeginAsync(FirebaseSession session, byte[] dataKey)
    {
        await _ui(() =>
        {
            _session = session;
            _dataKey = dataKey;
            _identityChoice = null;
            _identityAsked = false;
            _synced = null;
            _file = new AccountFile
            {
                Email = session.Email, Uid = session.Uid,
                RefreshToken = Protect(System.Text.Encoding.UTF8.GetBytes(session.RefreshToken)),
                DataKey = Protect(dataKey),
            };
            Save();
            Problem = null;
            Changed?.Invoke();
        }).ConfigureAwait(false);
        Diag.Log("Account: signed in");
        await SyncAsync(CancellationToken.None).ConfigureAwait(false);
        Start();
    }

    /// <summary>Signs this PC out. Its data stays here, the account's stays in the account.</summary>
    public void SignOut()
    {
        _every.Change(Timeout.Infinite, Timeout.Infinite);
        _soon.Change(Timeout.Infinite, Timeout.Infinite);
        _session = null;
        if (_dataKey != null) CryptographicOperations.ZeroMemory(_dataKey);
        _dataKey = null;
        _file = new AccountFile();
        try { File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Error(ex); }
        Problem = null;
        Changed?.Invoke();
        Diag.Log("Account: signed out");
    }

    /// <summary>Deletes the account and everything in it. This PC keeps its own data and is signed out.</summary>
    /// <exception cref="AccountException">Firebase couldn't be reached, or wants a fresh sign-in first.</exception>
    public async Task DeleteAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = await SessionAsync(ct).ConfigureAwait(false);
            foreach (string section in SyncSections.All)
                await _firebase.DeleteAsync(SectionPath(session.Uid, section), session.IdToken, ct).ConfigureAwait(false);
            await _firebase.DeleteAsync(UserPath(session.Uid), session.IdToken, ct).ConfigureAwait(false);
            await _firebase.DeleteAccountAsync(session.IdToken, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        await _ui(SignOut).ConfigureAwait(false);
        Diag.Log("Account: deleted");
    }

    /// <summary>The answer to <see cref="IdentityQuestion"/>: true takes the account's identity, false keeps this PC's.</summary>
    public Task ChooseIdentityAsync(bool useAccounts)
    {
        _identityChoice = useAccounts;
        _identityAsked = false;
        return SyncAsync(CancellationToken.None);
    }

    // ───────────── Sync ─────────────

    async Task SyncQuietlyAsync()
    {
        try { await SyncAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { Diag.Log("Account sync: " + ex.Message); }
    }

    /// <summary>Fetches the account's data, puts it together with this PC's and sends back what the account lacks.</summary>
    public async Task SyncAsync(CancellationToken ct)
    {
        if (!SignedIn || !Available || _disposed) return;
        // One sync at a time; a second waits for the first and then catches what it missed.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        if (!SignedIn || _disposed) { _gate.Release(); return; }
        try
        {
            await _ui(() => { Syncing = true; Changed?.Invoke(); }).ConfigureAwait(false);
            string? problem = null;
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                    if (await SyncOnceAsync(ct).ConfigureAwait(false)) break; // false: another PC wrote meanwhile, go again
            }
            catch (AccountException ex) when (ex.Code is "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "USER_NOT_FOUND" or "USER_DISABLED")
            {
                problem = ex.Message;
                await _ui(SignOut).ConfigureAwait(false);
            }
            catch (AccountException ex) { problem = ex.Message; }
            catch (CryptographicException) { problem = "The account's data couldn't be opened. Sign out and in again."; }
            await _ui(() =>
            {
                Syncing = false;
                Problem = problem;
                if (problem == null && SignedIn) { _file.LastSync = Now; Save(); }
                Changed?.Invoke();
            }).ConfigureAwait(false);
            if (problem != null) Diag.Log("Account sync: " + problem);
        }
        finally { _gate.Release(); }
    }

    /// <summary>One round: read every section, merge on the UI thread, write the ones that changed. False on a write race.</summary>
    async Task<bool> SyncOnceAsync(CancellationToken ct)
    {
        var session = await SessionAsync(ct).ConfigureAwait(false);
        byte[] dataKey = DataKey();
        string uid = session.Uid;

        var reads = SyncSections.All.ToDictionary(s => s, s => _firebase.GetAsync(SectionPath(uid, s), session.IdToken, ct));
        await Task.WhenAll(reads.Values).ConfigureAwait(false);
        var docs = reads.ToDictionary(r => r.Key, r => r.Value.Result);
        T? Open<T>(string section) where T : class
            => docs[section]?.Fields.GetValueOrDefault("data") is { } sealedText
                ? AccountJson.FromBytes<T>(AccountVault.OpenSection(dataKey, uid, section, sealedText))
                : null;

        var remote = new Remote(Open<IdentityPayload>(SyncSections.Identity), Open<FriendsPayload>(SyncSections.Friends),
                                Open<ProfilePayload>(SyncSections.Profile), Open<SettingsPayload>(SyncSections.Settings),
                                Open<AchievementsPayload>(SyncSections.Achievements));
        Dictionary<string, byte[]> writes = new();
        bool ask = false;
        await _ui(() => writes = Merge(remote, Now, out ask)).ConfigureAwait(false);
        if (ask && !_identityAsked)
        {
            _identityAsked = true;
            await _ui(() => IdentityQuestion?.Invoke()).ConfigureAwait(false);
        }

        foreach (var (section, plain) in writes)
        {
            var fields = new Dictionary<string, string> { ["data"] = AccountVault.SealSection(dataKey, uid, section, plain) };
            if (!await _firebase.PutAsync(SectionPath(uid, section), fields, docs[section]?.UpdateTime, session.IdToken, ct).ConfigureAwait(false))
                return false;
        }
        await _ui(() => { Save(); _synced = LocalFingerprint(); Rounds++; }).ConfigureAwait(false);
        Diag.Log($"Account: synced ({writes.Count} sections sent)");
        return true;
    }

    sealed record Remote(IdentityPayload? Identity, FriendsPayload? Friends, ProfilePayload? Profile, SettingsPayload? Settings, AchievementsPayload? Achievements);

    /// <summary>Puts the account's data and this PC's together (UI thread); returns the sections to send, as JSON.</summary>
    Dictionary<string, byte[]> Merge(Remote remote, long now, out bool askIdentity)
    {
        var state = _file.State;
        var writes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        void Send<T>(string section, T merged, T? theirs) where T : class
        {
            if (theirs == null || !AccountJson.Same(merged, theirs)) writes[section] = AccountJson.ToBytes(merged);
        }

        _applying = true; // what the merge saves here is the account's, not a change to send back
        try
        {
            // The identity first: until it's settled, the friends list waits (its friends belong to one identity).
            askIdentity = false;
            var mine = _friends.ExportIdentity();
            string? myPrint = mine?.Fingerprint(), theirPrint = remote.Identity?.Fingerprint();
            var step = IdentitySync.Decide(myPrint, _friends.Friends.Count, theirPrint, state, _identityChoice);
            switch (step)
            {
                case IdentityStep.Ask:
                    askIdentity = true;
                    break;
                case IdentityStep.Upload:
                    mine!.Stamp = now;
                    writes[SyncSections.Identity] = AccountJson.ToBytes(mine);
                    state.IdentityFingerprint = myPrint;
                    state.IdentityStamp = now;
                    _identityChoice = null;
                    break;
                case IdentityStep.Adopt:
                    // Friends who only knew this PC's identity are told the account's, so they can add it back.
                    var onlyHere = state.IdentityFingerprint == null && myPrint != null
                        ? _friends.Friends.Where(f => remote.Friends?.Friends.Any(r => r.Key == f.Key) != true).ToList()
                        : new List<Friend>();
                    _friends.AdoptIdentity(remote.Identity!);
                    if (onlyHere.Count > 0) _friends.Reintroduce(onlyHere);
                    state.IdentityFingerprint = theirPrint;
                    state.IdentityStamp = remote.Identity!.Stamp;
                    _identityChoice = null;
                    break;
                default:
                    if (theirPrint != null) state.IdentityFingerprint = theirPrint;
                    break;
            }

            if (!askIdentity)
            {
                var (friends, mergedFriends, friendsChanged) = FriendsSync.Merge(_friends.Friends.ToList(), state, remote.Friends, now);
                if (friendsChanged) _friends.ReplaceFriends(friends);
                Send(SyncSections.Friends, mergedFriends, remote.Friends);
            }

            var (profile, profileChanged) = ProfileSync.Merge(_settings, state, remote.Profile, now);
            if (profileChanged) { _settings.Save(); ProfileApplied?.Invoke(); }
            Send(SyncSections.Profile, profile, remote.Profile);

            SettingsSync.NoteLocalChanges(state, SettingsSync.Capture(_settings), now);
            SettingsPayload mergedSettings = null!;
            List<string> changed = null!;
            void Apply() => (mergedSettings, changed) = SettingsSync.Merge(_settings, state, remote.Settings);
            if (ApplyingSettings is { } wrap) wrap(Apply); else Apply();
            if (changed.Count > 0) { _settings.Save(); Diag.Log("Account: settings from the account: " + string.Join(", ", changed)); }
            Send(SyncSections.Settings, mergedSettings, remote.Settings);

            var achievements = AchievementsSync.Merge(_achievements.Export(), remote.Achievements);
            _achievements.Import(achievements);
            Send(SyncSections.Achievements, achievements, remote.Achievements);
        }
        finally { _applying = false; }
        return writes;
    }

    // ───────────── Session and keys ─────────────

    async Task<FirebaseSession> SessionAsync(CancellationToken ct)
    {
        if (_session is { } s && s.Expires > DateTimeOffset.UtcNow.AddMinutes(2)) return s;
        string refresh = _session?.RefreshToken
                         ?? (_file.RefreshToken is { } sealedToken ? System.Text.Encoding.UTF8.GetString(Unprotect(sealedToken))
                             : throw new AccountException("You were signed out. Sign in again.", "INVALID_REFRESH_TOKEN"));
        var fresh = await _firebase.RefreshAsync(_file.Email ?? "", refresh, ct).ConfigureAwait(false);
        if (fresh.Uid != _file.Uid) throw new AccountException("You were signed out. Sign in again.", "INVALID_REFRESH_TOKEN");
        _session = fresh;
        if (fresh.RefreshToken != refresh)
            await _ui(() => { if (SignedIn) { _file.RefreshToken = Protect(System.Text.Encoding.UTF8.GetBytes(fresh.RefreshToken)); Save(); } }).ConfigureAwait(false);
        return fresh;
    }

    byte[] DataKey()
        => _dataKey ??= _file.DataKey is { } sealedKey ? Unprotect(sealedKey) : throw new AccountException("You were signed out. Sign in again.", "INVALID_REFRESH_TOKEN");

    static string UserPath(string uid) => "users/" + uid;
    static string SectionPath(string uid, string section) => $"users/{uid}/sync/{section}";

    static string Protect(byte[] data) => Convert.ToBase64String(FriendsService.ProtectKey(data, Entropy));
    static byte[] Unprotect(string data) => FriendsService.UnprotectKey(Convert.FromBase64String(data), Entropy);

    // ───────────── account.json ─────────────

    sealed class AccountFile
    {
        public string? Email { get; set; }
        public string? Uid { get; set; }
        /// <summary>Encrypted for this user (DPAPI on Windows, a key of the user's own on Linux).</summary>
        public string? RefreshToken { get; set; }
        /// <summary>The account's data key, encrypted for this user as well.</summary>
        public string? DataKey { get; set; }
        public long? LastSync { get; set; }
        public SyncState State { get; set; } = new();
    }

    static AccountFile Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(path), AccountJson.Options) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Account file unreadable, signed out: " + ex.Message);
            return new();
        }
    }

    void Save()
    {
        if (AppSettings.ReadOnly || _file.Uid == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_file, AccountJson.Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Log("Couldn't save the account: " + ex.Message); }
    }

    public void Dispose()
    {
        _disposed = true;
        AppSettings.Saved -= OnLocalChange;
        _achievements.Changed -= OnLocalChange;
        _soon.Dispose();
        _every.Dispose();
    }
}
