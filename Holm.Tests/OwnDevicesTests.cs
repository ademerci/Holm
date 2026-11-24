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
