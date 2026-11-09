using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Profile pictures: any picture you choose becomes a small square JPEG (it has to fit in one friend note), and a
/// friend's picture becomes a brush for the round avatars.
/// </summary>
internal static class ProfilePicture
{
    /// <summary>Pixels per side: sharp in a 34 px circle at 150 % scaling, and small enough for a note.</summary>
    public const int Size = 64;

    /// <summary>The middle square of the picture, as a JPEG under <see cref="FriendProfile.MaxPictureBytes"/>; null if it won't go.</summary>
    public static byte[]? Encode(BitmapSource picture)
    {
        int side = Math.Min(picture.PixelWidth, picture.PixelHeight);
        if (side <= 0) return null;
        BitmapSource square = new CroppedBitmap(picture, new Int32Rect((picture.PixelWidth - side) / 2, (picture.PixelHeight - side) / 2, side, side));
        // A busy picture may need fewer pixels to fit a note; a face or a logo keeps all 64.
        foreach (int size in new[] { Size, 56, 48 })
        {
            var flat = Flatten(square, side, size);
            for (int quality = 85; quality >= 35; quality -= 10)
            {
                var jpeg = new JpegBitmapEncoder { QualityLevel = quality };
                jpeg.Frames.Add(BitmapFrame.Create(flat));
                using var stream = new MemoryStream();
                jpeg.Save(stream);
                if (stream.Length <= FriendProfile.MaxPictureBytes) return stream.ToArray();
            }
        }
        return null;
    }

    /// <summary>The square at <paramref name="size"/> pixels on the avatars' dark gray: JPEG has no transparency.</summary>
    static BitmapSource Flatten(BitmapSource square, int side, int size)
    {
        BitmapSource small = new TransformedBitmap(square, new ScaleTransform((double)size / side, (double)size / side));
        var flat = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)), null, new Rect(0, 0, size, size));
            dc.DrawImage(small, new Rect(0, 0, size, size));
        }
        flat.Render(visual);
        flat.Freeze();
        return flat;
    }

    /// <summary>Reads a picture file and encodes it. Null for a file that isn't a picture WPF can read.</summary>
    public static byte[]? FromFile(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 512; // a phone photo needn't be decoded in full to become 64 pixels
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return Encode(image);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException
                                      or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Diag.Log("Profile picture: " + ex.Message);
            return null;
        }
    }

    static readonly Dictionary<string, Brush?> Brushes = new(StringComparer.Ordinal);

    /// <summary>A round avatar's fill for a picture in base64; cached, since the share view asks for each chip each time.</summary>
    public static Brush? BrushOf(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        if (Brushes.TryGetValue(base64, out var cached)) return cached;
        Brush? brush = null;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            if (FriendProfile.IsPicture(bytes))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException or ArgumentException
                                      or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Diag.Log("Friend's picture unreadable: " + ex.Message);
        }
        if (Brushes.Count > 200) Brushes.Clear();
        Brushes[base64] = brush;
        return brush;
    }
}
