using WinNotch.Notify;

namespace WinNotch.Tests;

public class NotificationBadgesTests
{
    const string WhatsAppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
    const string TeamsId = "MSTeams_8wekyb3d8bbwe!MSTeams";

    /// <summary>A database laid out like Windows' wpndatabase.db, in WAL mode like the real one.</summary>
    static WinSqlite MakeDatabase(TempFolder folder, string whatsAppBadge)
    {
        var db = WinSqlite.Open(Path.Combine(folder.Path, "wpndatabase.db"));
        db.Execute($"""
            PRAGMA journal_mode = WAL;
            PRAGMA wal_autocheckpoint = 0;
            CREATE TABLE NotificationHandler (RecordId INTEGER PRIMARY KEY, PrimaryId TEXT, HandlerType TEXT);
            CREATE TABLE Notification (Id INTEGER PRIMARY KEY, HandlerId INTEGER, Type TEXT, Payload BLOB, ArrivalTime INTEGER);
            INSERT INTO NotificationHandler VALUES
                (240, '{WhatsAppId}', 'app:immersive'),
                (245, '{TeamsId}', 'app:immersive'),
                (300, 'E046963F.LenovoSettingsforEnterprise_k1h2ywk1493x8!App', 'app:immersive'),
                (310, 'Claude_pzs8sxrjxfjjc!Claude', 'app:immersive');
            INSERT INTO Notification VALUES
                (1, 240, 'badge', CAST('<badge value="{whatsAppBadge}"/>' AS BLOB), 0),
                (2, 245, 'badge', CAST('<badge value="9"/>' AS BLOB), 0),
                (3, 300, 'badge', CAST('<badge value="activity"/>' AS BLOB), 0),
                (4, 310, 'toast', CAST('<toast><visual><binding template="ToastGeneric"><text>private</text></binding></visual></toast>' AS BLOB), 0);
            """);
        return db;
    }

    [Fact]
    public void Reads_the_badges_and_nothing_else()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "4"); // kept open: the rows are still only in the write-ahead log

        var badges = NotificationBadges.Read(folder.Path);

        Assert.Equal("4", badges[WhatsAppId]);
        Assert.Equal("activity", badges["E046963F.LenovoSettingsforEnterprise_k1h2ywk1493x8!App"]);
        Assert.False(badges.ContainsKey("Claude_pzs8sxrjxfjjc!Claude")); // a toast, not a badge
        Assert.Equal(4, NotificationBadges.CountFor(badges, Messengers.Get("whatsapp")!));
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("discord")!));
    }

    [Fact]
    public void Teams_badges_count_for_no_messenger()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "0");
        var badges = NotificationBadges.Read(folder.Path);

        Assert.Equal("9", badges[TeamsId]); // it's in the database...
        Assert.Null(Messengers.ForApp(TeamsId)); // ...but Teams isn't a messenger any more
        Assert.All(Messengers.All, app => Assert.NotEqual(9, NotificationBadges.CountFor(badges, app)));
    }

    [Fact]
    public void Sees_the_badge_change()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "0");
        Assert.Equal(0, NotificationBadges.CountFor(NotificationBadges.Read(folder.Path), Messengers.Get("whatsapp")!));

        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="7"/>' AS BLOB) WHERE Id = 1;""");
        Assert.Equal(7, NotificationBadges.CountFor(NotificationBadges.Read(folder.Path), Messengers.Get("whatsapp")!));
    }

    [Fact]
    public void A_glyph_is_no_number()
    {
        var badges = new Dictionary<string, string> { [WhatsAppId] = "alert" };
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("whatsapp")!));
    }

    [Fact]
    public void Reads_numeric_badges_and_ignores_invalid_negative_counts()
    {
        var badges = new Dictionary<string, string>
        {
            [WhatsAppId] = "6",
            ["com.squirrel.Discord.Discord"] = "-1",
        };
        Assert.Equal(6, NotificationBadges.CountFor(badges, Messengers.Get("whatsapp")!));
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("discord")!));
    }

    [Fact]
    public void Leaves_the_original_untouched_and_no_copy_behind()
    {
        using var folder = new TempFolder();
        using (var db = MakeDatabase(folder, "2")) { }
        var before = Directory.GetFiles(folder.Path).ToDictionary(f => f, File.GetLastWriteTimeUtc);
        int copies = Directory.GetDirectories(Path.GetTempPath(), "Holm-badges-*").Length;

        NotificationBadges.Read(folder.Path);

        Assert.Equal(before, Directory.GetFiles(folder.Path).ToDictionary(f => f, File.GetLastWriteTimeUtc));
        Assert.Equal(copies, Directory.GetDirectories(Path.GetTempPath(), "Holm-badges-*").Length);
    }

    [Fact]
    public void A_missing_database_is_an_IOException()
    {
        using var folder = new TempFolder();
        Assert.ThrowsAny<IOException>(() => NotificationBadges.Read(folder.Path));
    }

    [Fact]
    public void The_stamp_stays_while_nothing_is_written()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "2");
        string? stamp = NotificationBadges.Stamp(folder.Path);

        Assert.NotNull(stamp);
        NotificationBadges.Read(folder.Path); // reading a copy writes nothing
        Assert.Equal(stamp, NotificationBadges.Stamp(folder.Path));
    }

    [Fact]
    public void The_stamp_moves_with_every_commit()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "2");
        var stamps = new List<string?> { NotificationBadges.Stamp(folder.Path) };

        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="3"/>' AS BLOB) WHERE Id = 1;""");
        stamps.Add(NotificationBadges.Stamp(folder.Path));
        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="4"/>' AS BLOB) WHERE Id = 1;""");
        stamps.Add(NotificationBadges.Stamp(folder.Path));
        db.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); // the log starts over, as Windows' does now and then
        stamps.Add(NotificationBadges.Stamp(folder.Path));
        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="5"/>' AS BLOB) WHERE Id = 1;""");
        stamps.Add(NotificationBadges.Stamp(folder.Path));

        Assert.All(stamps, Assert.NotNull);
        Assert.Equal(stamps.Count, stamps.Distinct().Count());
    }

    [Fact]
    public void A_missing_database_has_no_stamp()
    {
        using var folder = new TempFolder();
        Assert.Null(NotificationBadges.Stamp(folder.Path));
    }

    [Fact]
    public void The_toast_fingerprint_ignores_badges()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "2");
        var before = NotificationBadges.ReadSnapshot(folder.Path);

        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="3"/>' AS BLOB) WHERE Id = 1;""");
        var after = NotificationBadges.ReadSnapshot(folder.Path);

        Assert.NotEqual(before.Values[WhatsAppId], after.Values[WhatsAppId]);
        Assert.Equal(before.Toasts, after.Toasts);
    }

    [Theory]
    [InlineData("INSERT INTO Notification VALUES (5, 240, 'toast', CAST('<toast>new</toast>' AS BLOB), 7);")]
    [InlineData("DELETE FROM Notification WHERE Id = 4;")]
    [InlineData("UPDATE Notification SET ArrivalTime = 9 WHERE Id = 4;")]
    // Same id, same time, same length: only the content differs (an app replacing its notification).
    [InlineData("UPDATE Notification SET Payload = CAST('<toast><visual><binding template=\"ToastGeneric\"><text>privatE</text></binding></visual></toast>' AS BLOB) WHERE Id = 4;")]
    public void The_toast_fingerprint_sees_every_toast_change(string change)
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "2");
        string before = NotificationBadges.ReadSnapshot(folder.Path).Toasts;

        db.Execute(change);

        Assert.NotEqual(before, NotificationBadges.ReadSnapshot(folder.Path).Toasts);
    }

    [Fact]
    public void The_toast_fingerprint_holds_no_text()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "2");
        string toasts = NotificationBadges.ReadSnapshot(folder.Path).Toasts;

        Assert.NotEmpty(toasts);
        Assert.DoesNotContain("private", toasts);
        Assert.DoesNotContain("toast", toasts);
    }

    /// <summary>The real database on this PC: readable, and the messengers' badges are numbers or glyph names.</summary>
    [Fact]
    [Trait("Category", "Local")]
    public void Reads_this_PCs_notification_database()
    {
        if (!File.Exists(Path.Combine(NotificationBadges.DefaultFolder, "wpndatabase.db"))) return;
        var badges = NotificationBadges.Read();
        var messengers = badges.Keys.Where(k => Messengers.ForApp(k) != null).ToList();
        foreach (var key in messengers) Assert.Matches("^-?[0-9]+$|^[a-z]+$", badges[key]);
    }
}
