using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class SendLaterTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    static QueuedSend Queued(DateTime? asked = null, DateTime? noted = null) => new()
    {
        FriendKey = "lena", FriendName = "Lena", Paths = { Path.Combine("C:", "Holiday.zip") }, QueuedUtc = T0, AskedUtc = asked, NotedUtc = noted,
    };

    [Fact]
    public void Asks_every_quarter_hour_at_first_then_hourly()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), SendLater.AskEvery(TimeSpan.FromMinutes(30)));
        Assert.Equal(TimeSpan.FromHours(1), SendLater.AskEvery(TimeSpan.FromHours(3)));
        var send = Queued(asked: T0);
        Assert.False(SendLater.AskDue(send, T0.AddMinutes(14)));
        Assert.True(SendLater.AskDue(send, T0.AddMinutes(15)));
        send.AskedUtc = T0.AddHours(3);
        Assert.False(SendLater.AskDue(send, T0.AddHours(3).AddMinutes(30)));
        Assert.True(SendLater.AskDue(send, T0.AddHours(4)));
    }

    [Fact]
    public void Leaves_a_note_every_six_hours()
    {
        Assert.True(SendLater.NoteDue(Queued(), T0));
        var send = Queued(noted: T0);
        Assert.False(SendLater.NoteDue(send, T0.AddHours(5)));
        Assert.True(SendLater.NoteDue(send, T0.AddHours(6)));
    }

    [Fact]
    public void A_day_costs_few_notes()
    {
        // Every look the plan wants in the first day: well within the server's few hundred a day.
        var send = Queued();
        int notes = 0;
        for (var now = T0; now < T0.AddDays(1); now = now.AddMinutes(1))
        {
            if (SendLater.AskDue(send, now)) { send.AskedUtc = now; notes++; }
            if (SendLater.NoteDue(send, now)) { send.NotedUtc = now; notes++; }
        }
        Assert.InRange(notes, 20, 40);
    }

    [Fact]
    public void Gives_up_after_a_week()
    {
        Assert.False(SendLater.Expired(Queued(), T0.AddDays(6)));
        Assert.True(SendLater.Expired(Queued(), T0.AddDays(7)));
    }

    [Fact]
    public void Plans_the_next_look()
    {
        Assert.Null(SendLater.NextLook(Array.Empty<QueuedSend>(), T0));
        Assert.Equal(TimeSpan.Zero, SendLater.NextLook(new[] { Queued() }, T0));          // never asked: now
        var asked = Queued(asked: T0, noted: T0);
        Assert.Equal(TimeSpan.FromMinutes(10), SendLater.NextLook(new[] { asked }, T0.AddMinutes(5)));
    }

    [Fact]
    public void A_clock_set_back_asks_again()
    {
        var send = Queued(asked: T0.AddHours(1), noted: T0.AddHours(1));
        Assert.True(SendLater.AskDue(send, T0));
        Assert.True(SendLater.NoteDue(send, T0));
    }

    [Fact]
    public void Describes_what_waits()
    {
        Assert.Equal("Holiday.zip", SendLater.Describe(Queued()));
        Assert.Equal("a text", SendLater.Describe(new QueuedSend { Text = true, Paths = { "x.txt" } }));
        Assert.Equal("2 files", SendLater.Describe(new QueuedSend { Paths = { "a", "b" } }));
    }

    [Fact]
    public void Knows_its_own_files()
    {
        string temp = Path.Combine(Path.GetTempPath(), "Holm", "Beam");
        Assert.True(SendLater.IsHolmsOwn(Path.Combine(temp, "Docs.zip"), temp));
        Assert.False(SendLater.IsHolmsOwn(Path.Combine(Path.GetTempPath(), "Holm", "Beamer", "x.zip"), temp));
        Assert.False(SendLater.IsHolmsOwn(Path.Combine(temp, "..", "x.zip"), temp));
    }

    [Fact]
    public void The_queue_survives_a_restart_and_old_settings_load()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"BeamText\":false}")!;
        Assert.False(settings.BeamText);
        Assert.True(settings.SendLater);
        Assert.Empty(settings.SendQueue);
        settings.SendQueue.Add(Queued(asked: T0));
        var again = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        var send = Assert.Single(again.SendQueue);
        Assert.Equal("lena", send.FriendKey);
        Assert.Equal(T0, send.AskedUtc);
        Assert.Equal(T0, send.QueuedUtc);
    }

    [Fact]
    public void The_waiting_note_travels_sealed()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.Seal(noah, lena.AsFriend(), new FriendNote(FriendNoteKind.Waiting, "Noah", 1_800_000_000));
        Assert.Equal(FriendNoteKind.Waiting, FriendNotes.Open(lena, text, out _)!.Kind);
    }

    [Fact]
    public void A_failed_send_forgets_that_they_were_online()
    {
        var presence = new FriendPresence();
        Assert.True(presence.Saw("lena", T0));
        Assert.False(presence.Saw("lena", T0.AddSeconds(5)));   // still online: no news
        presence.Forget("lena");
        Assert.False(presence.IsOnline("lena", T0.AddSeconds(6)));
        Assert.True(presence.Saw("lena", T0.AddSeconds(30)));   // back: that's news, and the queue sends
    }
}
