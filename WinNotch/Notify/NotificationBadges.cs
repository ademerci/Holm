using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNotch.Notify;

/// <summary>
/// The numbers on app icons in the taskbar ("badges"), read from Windows' notification database.
/// Apps such as WhatsApp keep these up to date even when no toast reaches the notification
/// center. Only badges are read, never notification texts (toasts are hashed to notice a change).
/// </summary>
/// <remarks>
/// The database is in use by Windows, so we read a copy (database and write-ahead log) in a private
/// temp folder. It is a few MB and only read when it changed (see <see cref="Stamp"/>).
/// </remarks>
public static class NotificationBadges
{
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications");

    const string Database = "wpndatabase.db";
    static readonly Regex Value = new("value=\"([^\"]*)\"");

    /// <summary>App id → badge value ("3", "activity", …) for every app that has a badge.</summary>
    /// <exception cref="IOException">The database couldn't be read.</exception>
    public static Dictionary<string, string> Read(string? folder = null)
        => ReadSnapshot(folder).Values;

    /// <summary>
    /// A marker that changes whenever Windows writes to the notification database, or null if it can't be read.
    /// It takes a fifth of a millisecond, so it can be checked every few seconds instead of copying the database.
    /// </summary>
    /// <remarks>
    /// Besides the sizes and times of the database and its write-ahead log, it holds the header of the log's index
    /// (the -shm file), which SQLite rewrites on every commit. Windows keeps that file mapped in memory, so its
    /// own time never moves, and folder watchers can miss the write to the log; the header doesn't.
    /// </remarks>
    public static string? Stamp(string? folder = null)
    {
        string path = Path.Combine(folder ?? DefaultFolder, Database);
        try
        {
            var db = new FileInfo(path);
            if (!db.Exists) return null;
            var wal = new FileInfo(path + "-wal");
            var stamp = new StringBuilder().Append(db.Length).Append(':').Append(db.LastWriteTimeUtc.Ticks);
            if (wal.Exists) stamp.Append('|').Append(wal.Length).Append(':').Append(wal.LastWriteTimeUtc.Ticks);
            if (File.Exists(path + "-shm"))
            {
                var header = new byte[48]; // the first copy of the index header: change counter, last frame, salts, checksums
                using var index = new FileStream(path + "-shm", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
                int read = index.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
                stamp.Append('|').Append(Convert.ToHexString(header, 0, read));
            }
            return stamp.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The badges, their revisions, and a fingerprint of the toasts: it changes when a toast arrives, goes or is
    /// replaced, so the notification center only has to be asked then. Toast contents are hashed, never decoded.
    /// </summary>
    internal static (Dictionary<string, string> Values, Dictionary<string, string> Revisions, string Toasts) ReadSnapshot(string? folder = null)
    {
        folder ??= DefaultFolder;
        string copy = Path.Combine(Path.GetTempPath(), $"Holm-badges-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        try
        {
            CopyShared(Path.Combine(folder, Database), Path.Combine(copy, Database));
            if (File.Exists(Path.Combine(folder, Database + "-wal")))
                CopyShared(Path.Combine(folder, Database + "-wal"), Path.Combine(copy, Database + "-wal"));

            var badges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = WinSqlite.Open(Path.Combine(copy, Database));
            db.Query("select h.PrimaryId, n.Payload, n.Id, n.ArrivalTime from Notification n join NotificationHandler h on h.RecordId = n.HandlerId where n.Type = 'badge' order by n.ArrivalTime, n.Id",
                row =>
                {
                    string? app = row.Text(0);
                    var match = Value.Match(row.Text(1) ?? "");
                    if (app != null && match.Success)
                    {
                        badges[app] = match.Groups[1].Value;
                        revisions[app] = row.Text(2) + ":" + row.Text(3);
                    }
                });
            var toasts = new StringBuilder();
            db.Query("select rowid, Id, ArrivalTime, Payload from Notification where Type = 'toast' order by rowid",
                row => toasts.Append(row.Text(0)).Append(':').Append(row.Text(1)).Append(':').Append(row.Text(2)).Append(':').Append(row.Hash(3)).Append(' '));
            return (badges, revisions, toasts.ToString());
        }
        finally
        {
            try { Directory.Delete(copy, recursive: true); } catch { }
        }
    }

    /// <summary>The number on a messenger's badge: 0 for none, null if it has no badge or shows a glyph instead.</summary>
    public static int? CountFor(IReadOnlyDictionary<string, string> badges, Messenger app)
    {
        int? count = null;
        foreach (var (appId, value) in badges)
        {
            if (Messengers.ForApp(appId)?.Key != app.Key) continue;
            if (int.TryParse(value, out int n) && n >= 0) count = Math.Max(count ?? 0, n);
        }
        return count;
    }

    static void CopyShared(string from, string to)
    {
        // Windows keeps the files open; share everything so neither side has to wait.
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }
}

/// <summary>How many messages are new according to an app's badge, since you last looked at the app.</summary>
public sealed class BadgeCounter
{
    int? _count;
    int _seen;

    /// <summary>The number on the badge, null until read.</summary>
    public int? Count => _count;

    /// <summary>New since you last looked.</summary>
    public int Unread => _count is int count ? Math.Max(0, count - _seen) : 0;

    /// <summary>Takes the badge's number; true if it went up (a message came in).</summary>
    public bool Update(int count, bool refreshed = false)
    {
        int? before = _count;
        _count = count;
        if (before == null)
        {
            // Existing unread messages belong on the badge too. They aren't a new arrival, so don't pop out.
            return false;
        }
        // Apps often count conversations rather than messages. A fresh Windows badge can
        // announce another message while its numeric value stays at one.
        if (refreshed && count > 0) _seen = 0;
        _seen = Math.Min(_seen, count); // read some in the app: fewer are new
        return count > before || (refreshed && count > 0);
    }

    /// <summary>You looked at the app.</summary>
    public void MarkSeen()
    {
        if (_count is int count) _seen = count;
    }

    public void Reset()
    {
        _count = null;
        _seen = 0;
    }
}

/// <summary>A few calls into the SQLite that ships with Windows (winsqlite3.dll), so we need no library of our own.</summary>
public sealed class WinSqlite : IDisposable
{
    const int OK = 0, ROW = 100, DONE = 101;
    const int OPEN_READWRITE = 0x2, OPEN_CREATE = 0x4, OPEN_FULLMUTEX = 0x10000;

    IntPtr _db;

    WinSqlite(IntPtr db) => _db = db;

    /// <summary>Opens (or creates) a database file.</summary>
    /// <exception cref="IOException">SQLite refused.</exception>
    public static WinSqlite Open(string path)
    {
        int rc = sqlite3_open_v2(Utf8(path), out IntPtr db, OPEN_READWRITE | OPEN_CREATE | OPEN_FULLMUTEX, IntPtr.Zero);
        if (rc != OK)
        {
            string error = db != IntPtr.Zero ? Message(db) : $"error {rc}";
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new IOException("SQLite: " + error);
        }
        return new WinSqlite(db);
    }

    /// <summary>Runs statements that return no rows.</summary>
    public void Execute(string sql)
    {
        if (sqlite3_exec(_db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out IntPtr error) == OK) return;
        string message = error != IntPtr.Zero ? Marshal.PtrToStringUTF8(error) ?? "" : Message(_db);
        if (error != IntPtr.Zero) sqlite3_free(error);
        throw new IOException("SQLite: " + message);
    }

    /// <summary>Runs one query and hands each row to <paramref name="each"/>.</summary>
    public void Query(string sql, Action<Row> each)
    {
        if (sqlite3_prepare_v2(_db, Utf8(sql), -1, out IntPtr statement, IntPtr.Zero) != OK)
            throw new IOException("SQLite: " + Message(_db));
        try
        {
            while (true)
            {
                int rc = sqlite3_step(statement);
                if (rc == DONE) return;
                if (rc != ROW) throw new IOException("SQLite: " + Message(_db));
                each(new Row(statement));
            }
        }
        finally { sqlite3_finalize(statement); }
    }

    public void Dispose()
    {
        if (_db == IntPtr.Zero) return;
        sqlite3_close_v2(_db);
        _db = IntPtr.Zero;
    }

    public readonly struct Row
    {
        readonly IntPtr _statement;
        internal Row(IntPtr statement) => _statement = statement;

        /// <summary>A column as text; blobs are read as UTF-8 too.</summary>
        public string? Text(int column)
        {
            IntPtr data = sqlite3_column_blob(_statement, column);
            int length = sqlite3_column_bytes(_statement, column);
            if (data == IntPtr.Zero) return null;
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>A hash of a column's bytes, to notice a change without decoding it. Random per process, so never store it.</summary>
        public int Hash(int column)
        {
            IntPtr data = sqlite3_column_blob(_statement, column);
            int length = sqlite3_column_bytes(_statement, column);
            var hash = new HashCode();
            hash.Add(length);
            if (data != IntPtr.Zero && length > 0)
            {
                byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
                Marshal.Copy(data, bytes, 0, length);
                hash.AddBytes(bytes.AsSpan(0, length));
                ArrayPool<byte>.Shared.Return(bytes);
            }
            return hash.ToHashCode();
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");
    static string Message(IntPtr db) => Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "";

    [DllImport("winsqlite3")] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3")] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3")] static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr error);
    [DllImport("winsqlite3")] static extern void sqlite3_free(IntPtr p);
    [DllImport("winsqlite3")] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3")] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3")] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3")] static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
    [DllImport("winsqlite3")] static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport("winsqlite3")] static extern IntPtr sqlite3_errmsg(IntPtr db);
}
