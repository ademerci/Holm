using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
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
                $"state: {_state}, shape {_shapeW} x {_shapeH}");
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

    /// <summary>The window's content as drawn, at a scale of 2 on every system so pictures can be laid over each other.</summary>
    void Snapshot(string file)
    {
        const double scale = 2;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(_root);
        bitmap.Save(file, new PngBitmapEncoderOptions());
    }
}
