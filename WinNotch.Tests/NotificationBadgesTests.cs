using WinNotch.Notify;

namespace WinNotch.Tests;

public class NotificationBadgesTests
{
    /// <summary>A database laid out like Windows' wpndatabase.db, in WAL mode like the real one.</summary>
    static WinSqlite MakeDatabase(TempFolder folder, string teamsBadge)
    {
        var db = WinSqlite.Open(Path.Combine(folder.Path, "wpndatabase.db"));
        db.Execute($"""
            PRAGMA journal_mode = WAL;
            PRAGMA wal_autocheckpoint = 0;
            CREATE TABLE NotificationHandler (RecordId INTEGER PRIMARY KEY, PrimaryId TEXT, HandlerType TEXT);
            CREATE TABLE Notification (Id INTEGER PRIMARY KEY, HandlerId INTEGER, Type TEXT, Payload BLOB, ArrivalTime INTEGER);
            INSERT INTO NotificationHandler VALUES
                (245, 'MSTeams_8wekyb3d8bbwe!MSTeams', 'app:immersive'),
                (300, 'E046963F.LenovoSettingsforEnterprise_k1h2ywk1493x8!App', 'app:immersive'),
                (310, 'Claude_pzs8sxrjxfjjc!Claude', 'app:immersive');
            INSERT INTO Notification VALUES
                (1, 245, 'badge', CAST('<badge value="{teamsBadge}"/>' AS BLOB), 0),
                (2, 300, 'badge', CAST('<badge value="activity"/>' AS BLOB), 0),
                (3, 310, 'toast', CAST('<toast><visual><binding template="ToastGeneric"><text>private</text></binding></visual></toast>' AS BLOB), 0);
            """);
        return db;
    }

    [Fact]
    public void Reads_the_badges_and_nothing_else()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "4"); // kept open: the rows are still only in the write-ahead log

        var badges = NotificationBadges.Read(folder.Path);

        Assert.Equal("4", badges["MSTeams_8wekyb3d8bbwe!MSTeams"]);
        Assert.Equal("activity", badges["E046963F.LenovoSettingsforEnterprise_k1h2ywk1493x8!App"]);
        Assert.False(badges.ContainsKey("Claude_pzs8sxrjxfjjc!Claude")); // a toast, not a badge
        Assert.Equal(4, NotificationBadges.CountFor(badges, Messengers.Get("teams")!));
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("whatsapp")!));
    }

    [Fact]
    public void Sees_the_badge_change()
    {
        using var folder = new TempFolder();
        using var db = MakeDatabase(folder, "0");
        Assert.Equal(0, NotificationBadges.CountFor(NotificationBadges.Read(folder.Path), Messengers.Get("teams")!));

        db.Execute("""UPDATE Notification SET Payload = CAST('<badge value="7"/>' AS BLOB) WHERE Id = 1;""");
        Assert.Equal(7, NotificationBadges.CountFor(NotificationBadges.Read(folder.Path), Messengers.Get("teams")!));
    }

    [Fact]
    public void A_glyph_is_no_number()
    {
        var badges = new Dictionary<string, string> { ["MSTeams_8wekyb3d8bbwe!MSTeams"] = "alert" };
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("teams")!));
    }

    [Fact]
    public void Reads_WhatsApp_numeric_badges_and_ignores_invalid_negative_counts()
    {
        var badges = new Dictionary<string, string>
        {
            ["5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"] = "6",
            ["MSTeams_8wekyb3d8bbwe!MSTeams"] = "-1",
        };
        Assert.Equal(6, NotificationBadges.CountFor(badges, Messengers.Get("whatsapp")!));
        Assert.Null(NotificationBadges.CountFor(badges, Messengers.Get("teams")!));
    }

    [Fact]
    public void Leaves_the_original_untouched_and_no_copy_behind()
    {
        using var folder = new TempFolder();
        using (var db = MakeDatabase(folder, "2")) { }
        var before = Directory.GetFiles(folder.Path).ToDictionary(f => f, File.GetLastWriteTimeUtc);
        int copies = Directory.GetDirectories(Path.GetTempPath(), "WinNotch-badges-*").Length;

        NotificationBadges.Read(folder.Path);

        Assert.Equal(before, Directory.GetFiles(folder.Path).ToDictionary(f => f, File.GetLastWriteTimeUtc));
        Assert.Equal(copies, Directory.GetDirectories(Path.GetTempPath(), "WinNotch-badges-*").Length);
    }

    [Fact]
    public void A_missing_database_is_an_IOException()
    {
        using var folder = new TempFolder();
        Assert.ThrowsAny<IOException>(() => NotificationBadges.Read(folder.Path));
    }

    /// <summary>The real database on this PC: readable, and Teams' badge is a number if Teams is installed.</summary>
    [Fact]
    [Trait("Category", "Local")]
    public void Reads_this_PCs_notification_database()
    {
        if (!File.Exists(Path.Combine(NotificationBadges.DefaultFolder, "wpndatabase.db"))) return;
        var badges = NotificationBadges.Read();
        var teams = badges.Keys.Where(k => Messengers.ForApp(k)?.Key == "teams").ToList();
        foreach (var key in teams) Assert.Matches("^[0-9]+$|^[a-z]+$", badges[key]);
    }
}
