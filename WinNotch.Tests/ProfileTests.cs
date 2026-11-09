using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class ProfileTests
{
    static T OnSta<T>(Func<T> run)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = run(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    /// <summary>A photo-like picture: noise is the hardest thing for JPEG to make small.</summary>
    static BitmapSource Noise(int width, int height)
    {
        var random = new Random(7);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Any_picture_becomes_a_small_square_jpeg()
    {
        byte[]? jpeg = OnSta(() => ProfilePicture.Encode(Noise(1200, 800)));
        Assert.NotNull(jpeg);
        Assert.True(FriendProfile.IsPicture(jpeg));
        Assert.True(jpeg!.Length <= FriendProfile.MaxPictureBytes, $"{jpeg.Length} bytes");
        var size = OnSta(() =>
        {
            var frame = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        });
        Assert.Equal(size.PixelWidth, size.PixelHeight);
        Assert.InRange(size.PixelWidth, 48, ProfilePicture.Size);
    }

    [Fact]
    public void A_plain_photo_keeps_the_full_size()
    {
        // Smooth color like a face or the sky comes out at the full 64 pixels.
        int width = OnSta(() =>
        {
            var target = new RenderTargetBitmap(600, 600, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Colors.SkyBlue, Colors.Orange, 45), null, new Rect(0, 0, 600, 600));
                dc.DrawEllipse(Brushes.SaddleBrown, null, new Point(300, 260), 140, 170);
            }
            target.Render(visual);
            target.Freeze();
            byte[]? jpeg = ProfilePicture.Encode(target);
            return BitmapDecoder.Create(new MemoryStream(jpeg!), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
        });
        Assert.Equal(ProfilePicture.Size, width);
    }

    [Fact]
    public void The_largest_profile_still_fits_one_inbox_message()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var picture = new byte[FriendProfile.MaxPictureBytes];
        new Random(3).NextBytes(picture);
        picture[0] = 0xFF; picture[1] = 0xD8; picture[2] = 0xFF;
        string name = new string('N', FriendProfile.MaxName);
        string status = string.Concat(Enumerable.Repeat("🎧", FriendProfile.MaxStatus)); // two UTF-16 units each
        var profile = new FriendProfile(name, status, "#FF7A59", picture);

        string text = FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Profile, name, 1, Profile: profile));

        Assert.True(text.Length < 4096, $"{text.Length} characters"); // ntfy turns longer messages into attachments
        var note = FriendNotes.Open(lena, text, out string sender);
        Assert.Equal(me.PublicKey, sender);
        Assert.Equal(FriendNoteKind.Profile, note!.Kind);
        Assert.Equal(name, note.Profile!.Name);
        Assert.Equal(status, note.Profile.Status);
        Assert.Equal("#FF7A59", note.Profile.Color);
        Assert.Equal(picture, note.Profile.Picture);
        Assert.Equal(profile.Version, note.Profile.Version);
    }

    [Fact]
    public void A_profile_note_keeps_only_what_is_safe_to_show()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var notAPicture = new byte[100]; // no JPEG or PNG signature
        var profile = new FriendProfile("Noah", "line\nbreak\u0007 and a long tail " + new string('x', 200), "red", notAPicture);

        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Profile, "Noah", 1, Profile: profile)), out _);

        Assert.Null(note!.Profile!.Picture);
        Assert.Null(note.Profile.Color);
        Assert.DoesNotContain(note.Profile.Status!, c => char.IsControl(c));
        Assert.True(note.Profile.Status!.Length <= FriendProfile.MaxStatus);
    }

    [Fact]
    public void The_version_changes_with_anything_in_the_profile()
    {
        var a = new FriendProfile("Noah", "hi", null, new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 4, 5, 6 });
        Assert.Equal(a.Version, (a with { }).Version);
        Assert.NotEqual(a.Version, (a with { Status = "busy" }).Version);
        Assert.NotEqual(a.Version, (a with { Color = "#34C759" }).Version);
        Assert.NotEqual(a.Version, (a with { Name = "Noah G" }).Version);
        Assert.NotEqual(a.Version, (a with { Picture = null }).Version);
    }

    [Fact]
    public void A_newer_friend_card_keeps_the_profile_they_sent()
    {
        var older = new Friend { Name = "Lena", Key = "k", Picture = "abc", Status = "hi", Color = "#34C759", ProfileSent = "v1" };
        var newer = new Friend { Name = "Lena H", Key = "k" };
        newer.KeepProfileOf(older);
        Assert.Equal(("abc", "hi", "#34C759", "v1"), (newer.Picture, newer.Status, newer.Color, newer.ProfileSent));
        Assert.Equal("Lena H", newer.Name);
    }

    [Fact]
    public void A_received_profile_renames_the_friend_and_clears_what_they_removed()
    {
        var friend = new Friend { Name = "Lena", Key = "k", Picture = "abc", Status = "hi" };
        friend.TakeProfile(new FriendProfile("Lena Hoffmann", null, "#5E5CE6", null));
        Assert.Equal(("Lena Hoffmann", null, "#5E5CE6", null), (friend.Name, friend.Status, friend.Color, friend.Picture));
    }

    [Fact]
    public void Holm_before_profiles_leaves_the_note_unopened_and_profile_free_settings_still_load()
    {
        // The note kind is new; an unknown kind opens to null on the older side (same path as SealKind's unknown kinds).
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        Assert.Null(FriendNotes.Open(lena, FriendNotes.SealKind(me, lena.AsFriend(), "profile-v9", "Noah", 1), out _));

        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"Friends":[{"Name":"Lena","Topic":"t","Key":"k"}]}""")!;
        Assert.Null(settings.ProfilePicture);
        Assert.Null(settings.Friends[0].Picture);
    }
}
