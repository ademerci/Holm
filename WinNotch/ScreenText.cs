using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace WinNotch;

/// <summary>The text in a screenshot, read by Windows' own OCR (Windows.Media.Ocr), on this PC only.</summary>
internal static class ScreenText
{
    /// <summary>Whether Windows can read text at all: it needs the OCR part of a language pack.</summary>
    public static bool Available
    {
        get
        {
            try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
            catch (Exception) { return false; } // whatever the reason, there is no OCR to offer
        }
    }

    /// <summary>The picture's text, line by line ("" if it has none), or null when Windows can't read text.</summary>
    public static async Task<string?> ReadAsync(string path)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return null;
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var (width, height) = OcrText.Fit(decoder.PixelWidth, decoder.PixelHeight, OcrEngine.MaxImageDimension);
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                                                                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return OcrText.Join(result.Lines.Select(line => line.Text));
    }
}

/// <summary>The plain parts of reading text from a picture.</summary>
internal static class OcrText
{
    /// <summary>The size to hand the OCR: the picture's own, or shrunk to fit its largest side.</summary>
    public static (uint Width, uint Height) Fit(uint width, uint height, uint max)
    {
        if (width <= max && height <= max) return (width, height);
        double scale = (double)max / Math.Max(width, height);
        return (Math.Max(1, (uint)Math.Round(width * scale)), Math.Max(1, (uint)Math.Round(height * scale)));
    }

    /// <summary>One line per recognized line, ready to paste; blank ones left out.</summary>
    public static string Join(IEnumerable<string> lines)
        => string.Join("\r\n", lines.Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>"1 line", "12 lines".</summary>
    public static string Count(string text)
    {
        int lines = text.Length == 0 ? 0 : text.Split("\r\n").Length;
        return lines == 1 ? "1 line" : $"{lines} lines";
    }
}
