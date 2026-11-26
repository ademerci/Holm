using System;
using System.Security.Cryptography;
using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>Notes between the devices of one account (PCs signed in to the same account), which share one identity and inbox.</summary>
public class OwnDevicesTests
{
    static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    static readonly string Pc = OwnDevice.NewId(), Laptop = OwnDevice.NewId();

    [Fact]
    public void A_device_note_sealed_for_ourselves_opens_with_its_id_and_question()
    {
        using var me = FriendIdentity.Create("Elias", null);
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Device, "Work Laptop", 10, Device: Laptop, Query: true));
        var note = FriendNotes.Open(me, text, out string sender);
        Assert.NotNull(note);
        Assert.Equal(FriendNoteKind.Device, note!.Kind);
        Assert.Equal("Work Laptop", note.From);
        Assert.Equal(Laptop, note.Device);
        Assert.True(note.Query);
        Assert.Equal(me.PublicKey, sender); // sealed by our own identity: one of our devices
    }

    [Fact]
    public void Legacy_phone_note_is_silently_ignored()
    {
        using var me = FriendIdentity.Create("Elias", null);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string minimal = FriendNotes.SealKind(me, me.AsFriend(), "phone", "Old Phone", now);
        Assert.Null(FriendNotes.Open(me, minimal, out _));

        // Full legacy "phone" payload carrying a valid device id and status fields must still open as null without throwing.
        byte[] plain = Encoding.UTF8.GetBytes(
            $$"""{"K":"phone","F":"Old Phone","T":{{now}},"D":"{{Laptop}}","Y":"notification","V":"Lena","W":"WhatsApp · Hi","Z":0,"A":true,"I":"0|com.whatsapp|1"}""");
        byte[] sharedKey = me.Key.DeriveKeyFromHash(me.Key.PublicKey, HashAlgorithmName.SHA256, "WinNotch friends v1"u8.ToArray(), null);
        byte[] sealedNote = new byte[12 + 16 + plain.Length];
        RandomNumberGenerator.Fill(sealedNote.AsSpan(0, 12));
        using (var aes = new AesGcm(sharedKey, 16))
            aes.Encrypt(sealedNote.AsSpan(0, 12), plain, sealedNote.AsSpan(28), sealedNote.AsSpan(12, 16), Encoding.UTF8.GetBytes(me.Topic));
        string fullLegacy = FriendNotes.Prefix + me.PublicKey + " " + FriendIdentity.Base64Url(sealedNote);

        Assert.Null(FriendNotes.Open(me, fullLegacy, out _));
    }

    [Fact]
    public void A_device_note_without_a_valid_id_is_left_unopened()
    {
        using var me = FriendIdentity.Create("Elias", null);
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Device, "Work Laptop", 10, Device: "not-an-id"));
        Assert.Null(FriendNotes.Open(me, text, out _));
    }

    [Fact]
    public void Files_for_one_device_say_which_and_from_which()
    {
        using var me = FriendIdentity.Create("Elias", null);
        var secret = ShareCode.NewSecret();
        string text = FriendNotes.Seal(me, me.AsFriend(), new FriendNote(FriendNoteKind.Files, "Work Laptop", 10, Secret: secret, Device: Laptop, ForDevice: Pc));
        var note = FriendNotes.Open(me, text, out _)!;
        Assert.Equal(FriendNoteKind.Files, note.Kind);
        Assert.Equal(secret, note.Secret);
        Assert.Equal(Laptop, note.Device);
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
        Assert.True(devices.Saw(Laptop, "Work Laptop", T0));
        Assert.False(devices.Saw(Laptop, "Work Laptop", T0.AddMinutes(1))); // nothing new to show
        Assert.True(devices.Saw(Laptop, "Studio Laptop", T0.AddMinutes(2))); // a new name is
        Assert.Single(devices.Recent(T0.AddMinutes(5)));
        Assert.Empty(devices.Recent(T0.AddMinutes(2) + OwnDevices.Fresh));
        Assert.False(devices.Saw("nope", "Work Laptop", T0));
    }

    [Fact]
    public void The_same_device_under_a_new_id_replaces_the_old_one()
    {
        // A PC after a reinstall: same name, new id. The old one would stay as a device that never answers.
        var devices = new OwnDevices();
        devices.Saw(Laptop, "Work Laptop", T0);
        devices.Saw(Pc, "Office PC", T0);
        string updated = Guid.NewGuid().ToString("N");
        Assert.True(devices.Saw(updated, "work laptop", T0.AddMinutes(1)));
        var now = devices.Recent(T0.AddMinutes(2));
        Assert.Equal(2, now.Count);
        Assert.Contains(now, d => d.Id == updated);
        Assert.DoesNotContain(now, d => d.Id == Laptop);
    }

    [Fact]
    public void Names_are_cleaned_and_a_failed_send_forgets_the_device()
    {
        var devices = new OwnDevices();
        devices.Saw(Pc, "  Office\u0007PC  ", T0);
        Assert.Equal("OfficePC", devices.Find(Pc)!.Name);
        devices.Saw(Laptop, "\u0001", T0);
        Assert.Equal("Holm", devices.Find(Laptop)!.Name);
        devices.Forget(Pc);
        Assert.Null(devices.Find(Pc));
    }
}
