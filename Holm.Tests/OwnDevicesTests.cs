using System;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>Notes between the devices of one account (the phone app, other PCs), which share one identity and inbox.</summary>
public class OwnDevicesTests
{
    static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    static readonly string Pc = OwnDevice.NewId(), Phone = OwnDevice.NewId();

    [Fact]
    public void A_device_note_sealed_for_ourselves_opens_with_its_id_and_question()
    {
        using var me = FriendIdentity.Create("Elias", null);
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Device, "Pixel 9", 10, Device: Phone, Query: true));
        var note = FriendNotes.Open(me, text, out string sender);
        Assert.NotNull(note);
        Assert.Equal(FriendNoteKind.Device, note!.Kind);
        Assert.Equal("Pixel 9", note.From);
        Assert.Equal(Phone, note.Device);
        Assert.True(note.Query);
        Assert.Equal(me.PublicKey, sender); // sealed by our own identity: one of our devices
    }

    [Fact]
    public void The_phone_tells_the_pc_what_happens_on_it()
    {
        using var me = FriendIdentity.Create("Elias", null);
        var status = new PhoneStatus(PhoneStatus.Notification, "Lena", "WhatsApp · See you at 8", 0, true);
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Phone, "Pixel 9", 10, Device: Phone, Phone: status));
        var note = FriendNotes.Open(me, text, out _);
        Assert.Equal(FriendNoteKind.Phone, note!.Kind);
        Assert.Equal(Phone, note.Device);
        Assert.Equal(status, note.Phone);

        // Music that paused says so; a kind this version doesn't know, or no title, leaves the note unopened.
        var paused = new PhoneStatus(PhoneStatus.Music, "Song", "Artist", 0, false);
        text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Phone, "Pixel 9", 10, Device: Phone, Phone: paused));
        Assert.False(FriendNotes.Open(me, text, out _)!.Phone!.On);
        Assert.Null(PhoneStatus.From("weather", "Sunny", null, 0, true));
        Assert.Equal(PhoneStatus.Battery, PhoneStatus.From("battery", "Charged", "100%", 0, true)!.Kind);
        // Now and then the phone's state, for the phone tool: the battery, charging, the song.
        var state = PhoneStatus.From("status", "87%", "Golden Hour · Holm Demo", 0, false)!;
        Assert.Equal(("87%", "Golden Hour · Holm Demo", false), (state.Title, state.Detail, state.On));

        // Its connections travel along as JSON and read as words; anything else in there is left out.
        var full = new PhoneStatus(PhoneStatus.Status, "87%", null, 0, true, "{\"net\":\"wifi\",\"vpn\":true,\"bt\":\"AirPods 80%\",\"dnd\":true}");
        text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Phone, "Pixel 9", 10, Device: Phone, Phone: full));
        var back = FriendNotes.Open(me, text, out _)!.Phone!;
        Assert.Equal(new[] { "Wi-Fi", "VPN", "AirPods 80%", "Do Not Disturb" }, back.Connections());
        Assert.Empty(new PhoneStatus(PhoneStatus.Status, "87%", null, 0, true, "not json").Connections());

        // A notification carries its key (the PC can close it on the phone); a PC's "close" is never read as a status.
        var message = new PhoneStatus(PhoneStatus.Notification, "Lena", "WhatsApp · Hi", 0, true, Key: "0|com.whatsapp|1|null|10");
        text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Phone, "Pixel 9", 10, Device: Phone, Phone: message));
        Assert.Equal("0|com.whatsapp|1|null|10", FriendNotes.Open(me, text, out _)!.Phone!.Key);
        var close = new PhoneStatus(PhoneStatus.Dismiss, "dismiss", null, 0, true, Key: "0|com.whatsapp|1|null|10");
        text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Phone, "PC", 10, Device: Pc, ForDevice: Phone, Phone: close));
        Assert.Null(FriendNotes.Open(me, text, out _));
        Assert.Null(PhoneStatus.CleanKey(new string('k', 400)));
        Assert.Null(PhoneStatus.From(PhoneStatus.Call, "  ", null, 0, true));
        Assert.Equal(80, PhoneStatus.From(PhoneStatus.Call, new string('a', 200), null, 0, true)!.Title.Length);
    }

    [Fact]
    public void A_device_note_without_a_valid_id_is_left_unopened()
    {
        using var me = FriendIdentity.Create("Elias", null);
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Device, "Pixel 9", 10, Device: "not-an-id"));
        Assert.Null(FriendNotes.Open(me, text, out _));
    }

    [Fact]
    public void Files_for_one_device_say_which_and_from_which()
    {
        using var me = FriendIdentity.Create("Elias", null);
        var secret = ShareCode.NewSecret();
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Files, "Pixel 9", 10, Secret: secret, Device: Phone, ForDevice: Pc));
        var note = FriendNotes.Open(me, text, out _)!;
        Assert.Equal(FriendNoteKind.Files, note.Kind);
        Assert.Equal(secret, note.Secret);
        Assert.Equal(Phone, note.Device);
        Assert.Equal(Pc, note.ForDevice);
    }

    [Fact]
    public void Notes_between_friends_carry_no_device()
    {
        using var me = FriendIdentity.Create("Elias", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Files, "Elias", 10, Secret: ShareCode.NewSecret()));
        var note = FriendNotes.Open(lena, text, out _)!;
        Assert.Null(note.Device);
        Assert.Null(note.ForDevice);
    }

    [Fact]
    public void Devices_count_while_they_spoke_lately()
    {
        var devices = new OwnDevices();
        Assert.True(devices.Saw(Phone, "Pixel 9", T0));
        Assert.False(devices.Saw(Phone, "Pixel 9", T0.AddMinutes(1))); // nothing new to show
        Assert.True(devices.Saw(Phone, "Elias' Pixel", T0.AddMinutes(2))); // a new name is
        Assert.Single(devices.Recent(T0.AddMinutes(5)));
        Assert.Empty(devices.Recent(T0.AddMinutes(2) + OwnDevices.Fresh));
        Assert.False(devices.Saw("nope", "Pixel 9", T0));
    }

    [Fact]
    public void Names_are_cleaned_and_a_failed_send_forgets_the_device()
    {
        var devices = new OwnDevices();
        devices.Saw(Pc, "  Office\u0007PC  ", T0);
        Assert.Equal("OfficePC", devices.Find(Pc)!.Name);
        devices.Saw(Phone, "\u0001", T0);
        Assert.Equal("Holm", devices.Find(Phone)!.Name);
        devices.Forget(Pc);
        Assert.Null(devices.Find(Pc));
    }
}
