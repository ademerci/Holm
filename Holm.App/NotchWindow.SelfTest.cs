using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Holm.App;

/// <summary>
/// A run without a person, for CI on each system: <c>HOLM_SELFTEST=folder</c> starts the app as usual, opens the
/// notch by itself, reads what the backend offers, saves a picture of the closed and the open notch into the folder
/// and quits with exit code 0. A native call declared wrong crashes the process instead, and a picture that differs
/// from another system's shows where the look drifted apart.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly string? SelfTestFolder = Environment.GetEnvironmentVariable("HOLM_SELFTEST") is { Length: > 0 } f ? f : null;

    void StartSelfTest()
    {
        if (SelfTestFolder == null) return;
        _ = RunSelfTestAsync(SelfTestFolder);
    }

    async Task RunSelfTestAsync(string folder)
    {
        int exitCode = 0;
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(2500);
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "closed.png"));

            _hovered = true;
            ApplyState(animate: true);
            await Task.Delay(3000);
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "open.png"));

            bool volume = _audio.TryGetVolume(out float level, out bool muted);
            var power = _powerPlatform.Read();
            var report = string.Join(Environment.NewLine,
                $"system: {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})",
                $"backend: {_platform.GetType().Name}",
                $"scaling: {RenderScaling}",
                $"volume: {(volume ? $"{level:0.00}{(muted ? " muted" : "")}" : "none")}",
                $"meter: {(_audio.MeterWorking ? "follows the sound" : "bounces")}",
                $"battery: {(power.HasBattery ? $"{power.Percent} %{(power.OnAc ? ", plugged in" : "")}" : "none")}",
                $"media: {(_info == null ? "nothing playing" : $"{_info.Title} ({_info.App})")}",
                $"state: {_state}, shape {_shapeW} x {_shapeH}",
                // Where the notch sits in the window: pictures from two systems only match when this does.
                $"window: {Bounds.Width:0.#} x {Bounds.Height:0.#}, root at {_root.TranslatePoint(new Point(0, 0), this)}, island {_island.Bounds}",
                $"corners: {Corners(folder)}");
            File.WriteAllText(Path.Combine(folder, "report.txt"), report + Environment.NewLine);
            Console.WriteLine(report);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Self-test failed: " + e);
            exitCode = 1;
        }
        Dispatcher.UIThread.Post(() => { Close(); Environment.Exit(exitCode); });
    }

    /// <summary>What differs between machines rather than systems (the time, the volume) is set alike before a picture.</summary>
    /// <summary>The open picture's pixels just outside the notch, left and right, as A,R,G,B: should be see-through.</summary>
    static string Corners(string folder)
    {
        try
        {
            using var picture = new Bitmap(Path.Combine(folder, "open.png"));
            int stride = picture.PixelSize.Width * 4;
            var pixels = new byte[stride * picture.PixelSize.Height];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { picture.CopyPixels(new PixelRect(picture.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, stride); }
            finally { pin.Free(); }
            string At(int x, int y)
            {
                int o = y * stride + x * 4;
                return $"({x},{y})={pixels[o + 3]},{pixels[o + 2]},{pixels[o + 1]},{pixels[o]}";
            }
            int h = picture.PixelSize.Height, w = picture.PixelSize.Width;
            return string.Join(" ", At(5, h / 2), At(w - 6, h / 2), At(w / 2, h / 2), At(5, 2));
        }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    void FreezeForPicture()
    {
        _minute.Stop();
        _idleClock.Text = _clockBig.Text = "09:41";
        _dateText.Text = "Monday, November 16";
        _volSlider.Value = 0.5;
        _volSlider.Opacity = 1;
        UpdateVolumeIcon(0.5, false);
        UpdateLayout();
    }

    /// <summary>
    /// The notch as drawn, at a scale of 2 on every system so pictures can be laid over each other. The picture is
    /// as high as the notch needs, not as the window is: Windows keeps windows at least 39 px high, which only adds
    /// see-through rows.
    /// </summary>
    void Snapshot(string file)
    {
        const double scale = 2;
        var size = new PixelSize((int)Math.Ceiling(BaseWidth * scale), (int)Math.Ceiling(NeededWindowHeight(_shapeH) * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        // The window, not the notch's canvas: a Canvas is 0 px high (it doesn't size to what it holds), and rendering
        // it alone gave empty pictures. The bitmap's size cuts the window to the notch.
        // Without a compositor (a bare X server, as in CI) Avalonia fills the window with its fallback colour, black;
        // the notch is what's compared, so the picture keeps the see-through around it on every system.
        // Avalonia paints it on a part of the window's template, so that part is hidden while the picture is taken.
        var fallback = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_TransparencyFallback");
        var shown = fallback?.IsVisible ?? false;
        if (fallback != null) fallback.IsVisible = false;
        bitmap.Render(this);
        if (fallback != null) fallback.IsVisible = shown;
        bitmap.Save(file, new PngBitmapEncoderOptions());
    }
}
