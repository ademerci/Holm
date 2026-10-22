using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>A file a friend beamed us, saved in Downloads.</summary>
internal sealed record BeamReceived(string From, string Path);

/// <summary>
/// Beam on this PC: our identity (the private key is encrypted for this Windows account), the friends list,
/// following our inbox while we have friends, and saving what arrives to Downloads. Settings are only touched
/// on the UI thread.
/// </summary>
internal sealed class BeamService
{
    static readonly byte[] KeyEntropy = "WinNotch Beam key"u8.ToArray();

    readonly AppSettings _settings;
    readonly Dispatcher _ui;
    readonly Dictionary<string, long?> _limits = new(); // per server, asked once per run
    BeamIdentity? _me;
    bool _loaded;
    CancellationTokenSource? _follow;

    /// <summary>A file arrived and was saved (raised on the UI thread).</summary>
    public event Action<BeamReceived>? Received;
    /// <summary>Something arrived that we didn't take; the text says why (raised on the UI thread).</summary>
    public event Action<string>? Refused;

    public BeamService(AppSettings settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
    }

    public IReadOnlyList<BeamFriend> Friends => _settings.Friends;

    /// <summary>Who we are, once set up (the first time you copy your code or add a friend).</summary>
    public BeamIdentity? Me
    {
        get
        {
            if (!_loaded) { _loaded = true; _me = Load(); }
            return _me;
        }
    }

    BeamIdentity? Load()
    {
        if (_settings.BeamTopic == null || _settings.BeamKey == null) return null;
        try
        {
            byte[] pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(_settings.BeamKey), KeyEntropy, DataProtectionScope.CurrentUser);
            var key = ECDiffieHellman.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            CryptographicOperations.ZeroMemory(pkcs8);
            return new BeamIdentity(_settings.BeamName ?? "Me", _settings.BeamTopic, _settings.BeamServer, key);
        }
        catch (Exception ex)
        {
            // E.g. settings copied from another PC or account: the key can't be decrypted here.
            Diag.Log("Beam identity unreadable, a new one will be made: " + ex.Message);
            return null;
        }
    }

    public BeamIdentity CreateIdentity(string name)
    {
        var me = BeamIdentity.Create(name, _settings.BeamServer);
        byte[] pkcs8 = me.Key.ExportPkcs8PrivateKey();
        _settings.BeamKey = Convert.ToBase64String(ProtectedData.Protect(pkcs8, KeyEntropy, DataProtectionScope.CurrentUser));
        CryptographicOperations.ZeroMemory(pkcs8);
        _settings.BeamName = name;
        _settings.BeamTopic = me.Topic;
        // Catch up from now on: a friend may beam us while WinNotch isn't running.
        _settings.BeamSince = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _settings.BeamSeen.Clear();
        _settings.Save();
        (_me, _loaded) = (me, true);
        Restart();
        return me;
    }

    public void Rename(string name)
    {
        if (Me is not { } me) return;
        _settings.BeamName = name;
        _settings.Save();
        _me = new BeamIdentity(name, me.Topic, _settings.BeamServer, me.Key); // same key and inbox, new name in our code
    }

    public void AddFriend(BeamFriend friend)
    {
        _settings.Friends.RemoveAll(f => f.Key == friend.Key || f.Topic == friend.Topic);
        _settings.Friends.Add(friend);
        _settings.Save();
        Restart();
    }

    public void RemoveFriend(BeamFriend friend)
    {
        _settings.Friends.Remove(friend);
        _settings.Save();
        Restart();
    }

    /// <summary>Follows our inbox while there is someone to receive from; costs nothing otherwise.</summary>
    public void Restart()
    {
        Stop();
        if (Me is not { } me || _settings.Friends.Count == 0) return;
        var cts = new CancellationTokenSource();
        _follow = cts;
        long? since = _settings.BeamSince;
        _ = Task.Run(() => BeamClient.FollowAsync(me, since,
            incoming => TakeAsync(me, incoming, cts.Token),
            time => _ui.InvokeAsync(() => { if (_follow == cts) { _settings.BeamSince = time; _settings.Save(); } }),
            Diag.Log, cts.Token));
    }

    public void Stop()
    {
        _follow?.Cancel();
        _follow = null;
    }

    /// <summary>Runs on the inbox thread: decides, downloads, decrypts and saves one beam.</summary>
    async Task TakeAsync(BeamIdentity me, BeamIncoming incoming, CancellationToken ct)
    {
        // The catch-up after a reconnect can repeat the last message.
        var friend = await _ui.InvokeAsync(() =>
        {
            if (_settings.BeamSeen.Contains(incoming.Id)) return null;
            _settings.BeamSeen.Add(incoming.Id);
            if (_settings.BeamSeen.Count > 100) _settings.BeamSeen.RemoveRange(0, _settings.BeamSeen.Count - 100);
            var known = _settings.Friends.FirstOrDefault(f => f.Key == incoming.SenderKey);
            if (known == null) Refused?.Invoke("A stranger tried to beam you a file");
            return known;
        });
        if (friend == null) return;

        try
        {
            byte[] blob = await BeamClient.DownloadAsync(me, incoming, ct);
            if (BeamCrypto.Open(me, friend.Key, blob) is not { } opened)
            {
                Diag.Log("Beam: a file from " + friend.Name + " didn't decrypt");
                return;
            }
            string path = Save(opened.Header.Name, opened.File);
            Diag.Log("Beam: received a file from " + friend.Name);
            _ = _ui.InvokeAsync(() => Received?.Invoke(new BeamReceived(friend.Name, path)));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Most likely it waited longer than the server keeps files (3 hours on ntfy.sh), or Downloads is full.
            Diag.Log("Beam: couldn't take a file from " + friend.Name + ": " + ex.Message);
            _ = _ui.InvokeAsync(() => Refused?.Invoke("Missed a file from " + friend.Name));
        }
    }

    // ───────────── sending ─────────────

    /// <summary>Beams one file. Throws <see cref="BeamException"/> with a message fit for the notch.</summary>
    public async Task SendAsync(BeamFriend to, string path, CancellationToken ct)
    {
        if (Me is not { } me) throw new BeamException(BeamFailure.Rejected, "Set up Beam first");
        if (Directory.Exists(path)) throw new BeamException(BeamFailure.Rejected, "Folders can't be beamed");
        var info = new FileInfo(path);
        if (!info.Exists) throw new BeamException(BeamFailure.Rejected, "File not found");

        string server = BeamIdentity.NormalizeServer(to.Server);
        if (!_limits.TryGetValue(server, out long? limit))
        {
            // ntfy.sh's limit without an account, should it ever stop telling us.
            limit = await BeamClient.FileLimitAsync(server, ct) ?? (server == BeamIdentity.DefaultServer ? 2 * 1024 * 1024 : null);
            _limits[server] = limit;
        }
        long cap = Math.Min(limit ?? BeamClient.MaxBytes, BeamClient.MaxBytes);
        // The encrypted file is a little bigger than the file itself.
        if (info.Length + 1024 > cap) throw new BeamException(BeamFailure.TooLarge, "Too big: up to " + Megabytes(cap - 1024));

        byte[] file = await File.ReadAllBytesAsync(path, ct);
        try
        {
            await BeamClient.SendAsync(me, to, info.Name, file, ct);
        }
        catch (BeamException ex) when (ex.Failure == BeamFailure.TooLarge)
        {
            // With a known per-file limit the size fit, so it's the daily or total allowance on the server.
            throw new BeamException(BeamFailure.TooLarge, limit != null ? "Server limit reached, try later" : "Too big for the server");
        }
        finally { CryptographicOperations.ZeroMemory(file); }
    }

    static string Megabytes(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.#} MB" : $"{bytes / 1024} KB";

    // ───────────── saving ─────────────

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    static string DownloadsFolder()
    {
        var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref downloads, 0, IntPtr.Zero, out IntPtr path) == 0)
        {
            try { return Marshal.PtrToStringUni(path) ?? ""; }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    static string Save(string name, byte[] file)
    {
        string folder = DownloadsFolder();
        Directory.CreateDirectory(folder);
        string safe = BeamFiles.SafeName(name), stem = Path.GetFileNameWithoutExtension(safe), extension = Path.GetExtension(safe);
        for (int i = 0; ; i++)
        {
            string path = Path.Combine(folder, i == 0 ? safe : $"{stem} ({i}){extension}");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(file);
                MarkFromInternet(path);
                return path;
            }
            catch (IOException) when (File.Exists(path) && i < 1000) { }
        }
    }

    /// <summary>
    /// The "Mark of the Web": Windows then treats the file like a browser download (SmartScreen, Office
    /// Protected View, the "unblock" checkbox), since it came from someone else's PC.
    /// </summary>
    static void MarkFromInternet(string path)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (Exception ex) { Diag.Log("Beam: couldn't mark the file as downloaded: " + ex.Message); }
    }
}
