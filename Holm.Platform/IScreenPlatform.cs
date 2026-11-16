using System;
using System.Threading.Tasks;

namespace Holm.Platform;

/// <summary>A piece of the screen as 32-bit BGRA pixels, top row first.</summary>
public sealed record ScreenImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads what the screen shows, for the colour picker (Windows: GDI; Linux: X11). Everything else about picking is the app's own,
/// the same on every system.
/// </summary>
public interface IScreenPlatform
{
    /// <summary>The pixels of a rectangle in the desktop's device pixels; null when the screen can't be read.</summary>
    Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height);

    /// <summary>Whether text can be read from pictures here (Windows: an OCR language; Linux: tesseract installed).</summary>
    bool CanReadText { get; }

    /// <summary>The picture's text, one line per line ("" if it has none), or null when text can't be read here. On this computer only.</summary>
    Task<string?> ReadTextAsync(string path);
}

/// <summary>For systems without a backend yet: the screen can't be read.</summary>
public sealed class NoScreenPlatform : IScreenPlatform
{
    public Task<ScreenImage?> CaptureAsync(int x, int y, int width, int height) => Task.FromResult<ScreenImage?>(null);
    public bool CanReadText => false;
    public Task<string?> ReadTextAsync(string path) => Task.FromResult<string?>(null);
}
