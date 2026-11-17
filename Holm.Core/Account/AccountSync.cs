using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinNotch.Sharing;

namespace WinNotch.Account;

// What a Holm account keeps in sync, and how two computers' versions are put together. Each section is one sealed
// document in Firestore (users/<uid>/sync/<section>); nothing here talks to the network.
//
//   settings      the switches and choices that mean the same on every computer, newest change per setting wins
//   profile       name, status, color and picture, as friends see them; the newest edit wins
//   achievements  counters (the higher one) and unlocks (the earlier date)
//   friends       the friends list; added and removed are dated, so a removal on one PC reaches the others
//   identity      the friends key pair and inbox, so friends reach you on every PC with the same friend code
//
// Kept on each computer only: which display and where the notch sits, its size, paths (shelf, AI app), notes,
// reminders, the weather place, GIF history and key, the relay, files waiting to be sent, invite codes and requests.

public static class SyncSections
{
    public const string Settings = "settings", Profile = "profile", Achievements = "achievements", Friends = "friends", Identity = "identity";
    public static readonly IReadOnlyList<string> All = new[] { Identity, Friends, Profile, Settings, Achievements };
}

public sealed class SettingsPayload
{
    public Dictionary<string, JsonElement> Values { get; set; } = new();
    /// <summary>When each value was last changed (Unix seconds); 0 for "never changed since this PC joined".</summary>
    public Dictionary<string, long> Stamps { get; set; } = new();
}

public sealed class ProfilePayload
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? Color { get; set; }
    public string? Picture { get; set; }
    public long Stamp { get; set; }
}

public sealed class AchievementsPayload
{
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, long> Unlocked { get; set; } = new();
    public string? StreakDay { get; set; }
}

public sealed class SyncedFriend
{
    public string Name { get; set; } = "";
    public string Topic { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Server { get; set; }
    public string? Picture { get; set; }
    public string? Status { get; set; }
    public string? Color { get; set; }
    /// <summary>When the friend was (last) added on any PC of the account.</summary>
    public long Added { get; set; }

    public static SyncedFriend From(Friend f, long added) => new()
    {
        Name = f.Name, Topic = f.Topic, Key = f.Key, Server = f.Server, Picture = f.Picture, Status = f.Status, Color = f.Color, Added = added,
    };

    public Friend ToFriend() => new() { Name = Name, Topic = Topic, Key = Key, Server = Server, Picture = Picture, Status = Status, Color = Color };
}

public sealed class FriendsPayload
{
    public List<SyncedFriend> Friends { get; set; } = new();
    /// <summary>Friends removed, by key, and when. A friend added again later counts again.</summary>
    public Dictionary<string, long> Removed { get; set; } = new();
}

public sealed class IdentityPayload
{
    public string Topic { get; set; } = "";
    public string? Server { get; set; }
    /// <summary>The private key, PKCS#8, base64. Only ever stored sealed with the account's data key.</summary>
    public string Key { get; set; } = "";
    public long Stamp { get; set; }

    /// <summary>Tells two identities apart without the private key: the inbox and the public key.</summary>
    public string Fingerprint()
    {
        using var key = ECDiffieHellman.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(Key), out _);
        return Topic + "|" + FriendIdentity.Base64Url(key.ExportSubjectPublicKeyInfo());
    }
}

/// <summary>What this computer remembers about the last sync, to tell its own changes from the account's.</summary>
public sealed class SyncState
{
    public Dictionary<string, JsonElement> SettingsValues { get; set; } = new();
    public Dictionary<string, long> SettingsStamps { get; set; } = new();
    public string? ProfileHash { get; set; }
    public long ProfileStamp { get; set; }
    public Dictionary<string, long> FriendsAdded { get; set; } = new();
    public List<string> FriendsKnown { get; set; } = new();
    public Dictionary<string, long> FriendsRemoved { get; set; } = new();
    /// <summary>The identity last in sync (<see cref="IdentityPayload.Fingerprint"/>), null before the first sync.</summary>
    public string? IdentityFingerprint { get; set; }
    public long IdentityStamp { get; set; }
}

public static class AccountJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] ToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? FromBytes<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Options);

    /// <summary>Two payloads are the same when they serialize the same (the order of dictionaries is kept as built).</summary>
    public static bool Same<T>(T a, T b) => ToBytes(a).AsSpan().SequenceEqual(ToBytes(b));
}

/// <summary>The settings that travel with the account.</summary>
internal static class SettingsSync
{
    /// <summary>
    /// Switches and choices that mean the same on every computer. Left out on purpose: Monitor, NotchPosition and the
    /// scales (each display is different), AiPath and Shelf (paths), NoteText and ReminderList (they "stay on this
    /// PC", as the app promises), the weather place, Relay, GifSearchKey (a key), RecentGifs, the send queue, and
    /// everything about friends (own sections).
    /// </summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        nameof(AppSettings.Animations), nameof(AppSettings.Bounce), nameof(AppSettings.BounceLevel), nameof(AppSettings.ReactiveEqualizer),
        nameof(AppSettings.MiniIdle), nameof(AppSettings.Draggable), nameof(AppSettings.HideOverBrowsers), nameof(AppSettings.MediaControls),
        nameof(AppSettings.CheckForUpdatesAutomatically), nameof(AppSettings.Ai), nameof(AppSettings.Messages), nameof(AppSettings.MutedApps),
        nameof(AppSettings.QuietHours), nameof(AppSettings.QuietFrom), nameof(AppSettings.QuietUntil), nameof(AppSettings.QuietVips),
        nameof(AppSettings.Downloads), nameof(AppSettings.Vpn), nameof(AppSettings.Privacy), nameof(AppSettings.Calls),
        nameof(AppSettings.PauseForCalls), nameof(AppSettings.Screenshots), nameof(AppSettings.ScreenshotText), nameof(AppSettings.SleepTimer),
        nameof(AppSettings.Notes), nameof(AppSettings.Reminders), nameof(AppSettings.Calculator), nameof(AppSettings.ClipboardHistory),
        nameof(AppSettings.AppVolume), nameof(AppSettings.ClearPasswords), nameof(AppSettings.ColorPicker), nameof(AppSettings.Focumon),
        nameof(AppSettings.CapsLock), nameof(AppSettings.AllNotifications), nameof(AppSettings.NotchOnlyPopups), nameof(AppSettings.Dictation),
        nameof(AppSettings.HotKey), nameof(AppSettings.DiskWarning), nameof(AppSettings.UsbDrives), nameof(AppSettings.Connectivity),
        nameof(AppSettings.Weather), nameof(AppSettings.SkyColors), nameof(AppSettings.InstrumentLook), nameof(AppSettings.AiUsage),
        nameof(AppSettings.ClockShowSeconds), nameof(AppSettings.ClockShowDate), nameof(AppSettings.ClockShowMedia), nameof(AppSettings.ClockStyle),
        nameof(AppSettings.ClockColor), nameof(AppSettings.WeatherAutoLocation), nameof(AppSettings.Beam), nameof(AppSettings.AchievementPopups),
        nameof(AppSettings.ShareActivity), nameof(AppSettings.ChatPopups), nameof(AppSettings.QuickReactions), nameof(AppSettings.Waves),
        nameof(AppSettings.SendFolders), nameof(AppSettings.BeamText), nameof(AppSettings.SendLater),
    };

    static PropertyInfo Property(string name) => typeof(AppSettings).GetProperty(name) ?? throw new InvalidOperationException("No setting " + name);

    /// <summary>The synced settings as they are now.</summary>
    public static Dictionary<string, JsonElement> Capture(AppSettings settings)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string name in Names)
        {
            var property = Property(name);
            values[name] = JsonSerializer.SerializeToElement(property.GetValue(settings), property.PropertyType, AccountJson.Options);
        }
        return values;
    }

    /// <summary>"Choose app…" points at a program on this PC: another PC's choice doesn't replace it, nor is it taken there.</summary>
    static bool Applies(string name, AppSettings settings, JsonElement value)
        => name != nameof(AppSettings.Ai) || settings.Ai != "custom" && !(value.ValueKind == JsonValueKind.String && value.GetString() == "custom");

    /// <summary>Notes this PC's changes since the last sync in <paramref name="state"/>, dated <paramref name="now"/>.</summary>
    public static void NoteLocalChanges(SyncState state, Dictionary<string, JsonElement> current, long now)
    {
        foreach (var (name, value) in current)
        {
            if (state.SettingsValues.TryGetValue(name, out var before))
            {
                if (before.GetRawText() != value.GetRawText()) state.SettingsStamps[name] = now;
            }
            else state.SettingsStamps.TryAdd(name, 0); // first sync on this PC: the account's choice wins
            state.SettingsValues[name] = value;
        }
    }

    /// <summary>
    /// Puts this PC's settings and the account's together, newest change per setting, applies the account's newer ones
    /// to <paramref name="settings"/> and returns the merged payload and the names that changed here.
    /// </summary>
    public static (SettingsPayload Merged, List<string> Changed) Merge(AppSettings settings, SyncState state, SettingsPayload? remote)
    {
        var merged = new SettingsPayload();
        var changed = new List<string>();
        foreach (string name in Names)
        {
            var local = state.SettingsValues[name];
            long localStamp = state.SettingsStamps.GetValueOrDefault(name);
            if (remote != null && remote.Values.TryGetValue(name, out var theirs) && remote.Stamps.GetValueOrDefault(name) is long theirStamp
                && (theirStamp > localStamp || theirStamp == localStamp && localStamp == 0))
            {
                if (theirs.GetRawText() != local.GetRawText() && Applies(name, settings, theirs))
                {
                    try
                    {
                        var property = Property(name);
                        property.SetValue(settings, theirs.Deserialize(property.PropertyType, AccountJson.Options));
                        changed.Add(name);
                        local = theirs;
                    }
                    catch (JsonException) { } // a value this version can't read: keep ours
                }
                localStamp = theirStamp;
            }
            merged.Values[name] = local;
            merged.Stamps[name] = localStamp;
            state.SettingsValues[name] = local;
            state.SettingsStamps[name] = localStamp;
        }
        return (merged, changed);
    }
}

internal static class ProfileSync
{
    public static ProfilePayload Capture(AppSettings s) => new() { Name = s.ProfileName, Status = s.ProfileStatus, Color = s.ProfileColor, Picture = s.ProfilePicture };

    static string Hash(ProfilePayload p)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Name}\n{p.Status}\n{p.Color}\n{p.Picture}")));

    /// <summary>The newer profile wins; returns the merged one and whether this PC's was replaced.</summary>
    public static (ProfilePayload Merged, bool Changed) Merge(AppSettings settings, SyncState state, ProfilePayload? remote, long now)
    {
        var local = Capture(settings);
        string hash = Hash(local);
        if (state.ProfileHash == null) state.ProfileStamp = 0; // first sync on this PC: the account's profile wins
        else if (state.ProfileHash != hash) state.ProfileStamp = now;
        local.Stamp = state.ProfileStamp;
        bool changed = false;
        if (remote != null && (remote.Stamp > local.Stamp || remote.Stamp == local.Stamp && local.Stamp == 0) && Hash(remote) != hash)
        {
            settings.ProfileName = FriendProfile.CleanText(remote.Name, FriendProfile.MaxName);
            settings.ProfileStatus = FriendProfile.CleanText(remote.Status, FriendProfile.MaxStatus);
            settings.ProfileColor = FriendProfile.CleanColor(remote.Color);
            settings.ProfilePicture = remote.Picture;
            local = Capture(settings);
            local.Stamp = remote.Stamp;
            changed = true;
        }
        else if (remote != null && remote.Stamp > local.Stamp) local.Stamp = remote.Stamp;
        state.ProfileHash = Hash(local);
        state.ProfileStamp = local.Stamp;
        return (local, changed);
    }
}

public static class AchievementsSync
{
    /// <summary>Higher counters, earlier unlocks, the later day of the streak.</summary>
    public static AchievementsPayload Merge(AchievementsPayload local, AchievementsPayload? remote)
    {
        var merged = new AchievementsPayload
        {
            Counters = new Dictionary<string, long>(local.Counters, StringComparer.Ordinal),
            Unlocked = new Dictionary<string, long>(local.Unlocked, StringComparer.Ordinal),
            StreakDay = local.StreakDay,
        };
        if (remote == null) return merged;
        foreach (var (counter, value) in remote.Counters)
            if (value > merged.Counters.GetValueOrDefault(counter)) merged.Counters[counter] = value;
        foreach (var (id, at) in remote.Unlocked)
            if (!merged.Unlocked.TryGetValue(id, out long mine) || at < mine) merged.Unlocked[id] = at;
        if (string.CompareOrdinal(remote.StreakDay, merged.StreakDay) > 0) merged.StreakDay = remote.StreakDay;
        return merged;
    }
}

public static class FriendsSync
{
    /// <summary>
    /// Puts this PC's friends and the account's together. A friend counts while their latest "added" is later than
    /// their latest "removed", whichever PC did it. Returns the friends to keep here (in this PC's order, new ones
    /// last) and the merged payload.
    /// </summary>
    public static (List<Friend> Friends, FriendsPayload Merged, bool Changed) Merge(IReadOnlyList<Friend> localFriends, SyncState state,
                                                                                    FriendsPayload? remote, long now)
    {
        var present = new HashSet<string>(localFriends.Select(f => f.Key), StringComparer.Ordinal);
        var known = new HashSet<string>(state.FriendsKnown, StringComparer.Ordinal);
        // This PC's own changes since the last sync.
        foreach (string key in present)
            if (!known.Contains(key) || !state.FriendsAdded.ContainsKey(key)) state.FriendsAdded[key] = now;
        foreach (string key in known)
            if (!present.Contains(key)) state.FriendsRemoved[key] = now;

        var added = new Dictionary<string, long>(state.FriendsAdded, StringComparer.Ordinal);
        var removed = new Dictionary<string, long>(state.FriendsRemoved, StringComparer.Ordinal);
        var cards = new Dictionary<string, Friend>(StringComparer.Ordinal);
        foreach (var friend in localFriends) cards[friend.Key] = friend;
        if (remote != null)
        {
            foreach (var theirs in remote.Friends)
            {
                if (string.IsNullOrWhiteSpace(theirs.Key) || string.IsNullOrWhiteSpace(theirs.Topic)) continue;
                if (theirs.Added > added.GetValueOrDefault(theirs.Key, long.MinValue)) added[theirs.Key] = theirs.Added;
                if (cards.TryGetValue(theirs.Key, out var mine)) mine.KeepProfileOf(theirs.ToFriend());
                else cards[theirs.Key] = theirs.ToFriend();
            }
            foreach (var (key, at) in remote.Removed)
                if (at > removed.GetValueOrDefault(key, long.MinValue)) removed[key] = at;
        }

        bool Counts(string key) => added.TryGetValue(key, out long a) && a > removed.GetValueOrDefault(key, long.MinValue);

        var result = localFriends.Where(f => Counts(f.Key)).ToList();
        foreach (var (key, card) in cards)
            if (!present.Contains(key) && Counts(key)) result.Add(card);
        bool changed = result.Count != localFriends.Count || result.Where((f, i) => !ReferenceEquals(f, localFriends[i])).Any();

        var merged = new FriendsPayload
        {
            Friends = result.Select(f => SyncedFriend.From(f, added[f.Key])).ToList(),
            // Removals older than a year have long reached every PC.
            Removed = removed.Where(r => r.Value > now - 365L * 24 * 60 * 60 && !Counts(r.Key)).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal),
        };
        state.FriendsAdded = result.ToDictionary(f => f.Key, f => added[f.Key], StringComparer.Ordinal);
        state.FriendsRemoved = new Dictionary<string, long>(merged.Removed, StringComparer.Ordinal);
        state.FriendsKnown = result.Select(f => f.Key).ToList();
        return (result, merged, changed);
    }
}

/// <summary>What to do with the friends identity (key pair and inbox).</summary>
public enum IdentityStep
{
    /// <summary>Both have the same one, or neither has one.</summary>
    Nothing,
    /// <summary>The account has none yet: this PC's goes up.</summary>
    Upload,
    /// <summary>This PC takes the account's (it had none, had no friends, or the account's changed since the last sync).</summary>
    Adopt,
    /// <summary>Both have a different one and this PC has friends of its own: the person decides.</summary>
    Ask,
}

public static class IdentitySync
{
    /// <param name="localFingerprint">This PC's identity, null if it has none yet.</param>
    /// <param name="localFriends">How many friends this PC has.</param>
    /// <param name="choice">The person's answer to <see cref="IdentityStep.Ask"/>: true takes the account's, false keeps this PC's.</param>
    public static IdentityStep Decide(string? localFingerprint, int localFriends, string? remoteFingerprint, SyncState state, bool? choice)
    {
        if (remoteFingerprint == null) return localFingerprint == null ? IdentityStep.Nothing : IdentityStep.Upload;
        if (localFingerprint == remoteFingerprint) return IdentityStep.Nothing;
        if (localFingerprint == null) return IdentityStep.Adopt;
        // Ours was the account's at the last sync: another PC changed it since, follow it.
        if (state.IdentityFingerprint == localFingerprint) return IdentityStep.Adopt;
        if (localFriends == 0) return IdentityStep.Adopt;
        return choice switch { true => IdentityStep.Adopt, false => IdentityStep.Upload, null => IdentityStep.Ask };
    }
}
