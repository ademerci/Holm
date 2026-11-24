using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch.Account;
using WinNotch.Notify;

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
    List<long>? _selfTestMotionFrames;

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
            _hovered = false;
            ApplyState(animate: false);
            if (_expandedView.IsVisible)
                throw new InvalidOperationException("The closed notch still lays out the expanded view.");
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "closed.png"));
            SnapshotNotificationBadges(folder);
            CheckNotificationRestart();
            CheckMediaButtonAlignment();
            CheckPhoneActionsFit();
            var aiIcons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            foreach (var preset in AiPresets) aiIcons.Children.Add(AiTile(preset.Key, BadgeLogoSize)!);
            SnapshotIconStrip(aiIcons, folder, "ai-launcher-icons.png");
            // Verity at her sizes in the notch and big: awake, asleep, hungry, surprised, cheering and chewing, on the notch's black.
            var verity = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Background = Avalonia.Media.Brushes.Black };
            foreach (var (size, mood, cue, chewing) in new[]
            {
                (16.0, WinNotch.VerityMood.Awake, VerityCue.None, false), (30.0, WinNotch.VerityMood.Awake, VerityCue.None, false),
                (96.0, WinNotch.VerityMood.Awake, VerityCue.None, false), (96.0, WinNotch.VerityMood.Asleep, VerityCue.None, false),
                (96.0, WinNotch.VerityMood.Hungry, VerityCue.None, false), (96.0, WinNotch.VerityMood.Awake, VerityCue.Surprised, false),
                (96.0, WinNotch.VerityMood.Awake, VerityCue.Cheer, false), (96.0, WinNotch.VerityMood.Awake, VerityCue.None, true),
            })
            {
                var face = new Holm.App.Controls.VerityFace { Width = size, Height = size, Mood = mood, Chewing = chewing, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
                if (cue != VerityCue.None) face.Express(cue);
                verity.Children.Add(face);
            }
            SnapshotIconStrip(verity, folder, "verity.png");
            // And moving: a leap with its somersault and the hops after it, one pose every tenth of a second.
            var roam = new WinNotch.VerityRoam(480, 30, new Random(4));
            roam.Jump();
            var moves = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Background = Avalonia.Media.Brushes.Black };
            for (int frame = 0; frame < 36; frame++)
            {
                for (int i = 0; i < 3; i++) roam.Step(1 / 30.0, WinNotch.VerityMood.Awake);
                if (frame % 2 == 1) continue;
                var face = new Holm.App.Controls.VerityFace { Width = 30, Height = 30, Margin = new Thickness(2, 34 - roam.Lift, 2, roam.Lift) };
                face.Pose(roam.Squash, roam.Angle, roam.Gaze);
                moves.Children.Add(face);
            }
            SnapshotIconStrip(moves, folder, "verity-moves.png");

            var motionFrames = new List<long>();
            _selfTestMotionFrames = motionFrames;
            _hovered = true;
            ApplyState(animate: true);
            await Task.Delay(3000);
            _selfTestMotionFrames = null;
            // An actual pointer can enter or leave the notch while the app is posing.
            _hovered = true;
            ApplyState(animate: false);
            // A snap can release the opening headroom. X11 reports the new client width asynchronously.
            if (Math.Abs(ClientSize.Width - Width) * RenderScaling >= 1)
                await Task.Delay(200);
            if (!_expandedView.IsVisible || _phoneView.IsVisible)
                throw new InvalidOperationException("The open notch has the wrong views in layout.");
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "open.png"));
            CheckWeatherLocationWhilePlaying();
            CheckExpandedVolumeFeedback();
            await CheckWideSpringFrameAsync();
            CheckOpeningContentSteady();
            await CheckVerityPlacementAsync(folder);
            // The Beam tool's page: send and receive.
            ShowTool(Tool.Beam);
            ApplyState(animate: false);
            await Task.Delay(800); // the page fades in
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "beam.png"));
            ShowTool(Tool.None);
            ApplyState(animate: false);
            await SnapshotHubAsync(folder);

            // The Account page as someone signed out sees it. A self-test never signs in; its account file would sit in the folder.
            _account ??= new AccountService(_settings, _achievements, _friends, action => Dispatcher.UIThread.InvokeAsync(action).GetTask(), folder);
            ShowHub(AccountPage.Key);
            await Task.Delay(1500);
            if (_settingsWindow is { } hub)
            {
                using var picture = new RenderTargetBitmap(new PixelSize((int)hub.Bounds.Width, (int)hub.Bounds.Height));
                picture.Render(hub);
                picture.Save(Path.Combine(folder, "account.png"), new PngBitmapEncoderOptions());
            }

            bool volume = _audio.TryGetVolume(out float level, out bool muted);
            var power = _powerPlatform.Read();
            var report = string.Join(Environment.NewLine,
                $"system: {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})",
                $"backend: {_platform.GetType().Name}",
                $"scaling: {RenderScaling}",
                DescribeMotionFrames(motionFrames),
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

    void CheckWeatherLocationWhilePlaying()
    {
        var info = _info;
        bool weatherEnabled = _settings.Weather, mediaEnabled = _settings.MediaControls;
        try
        {
            _info = new MediaInfo { Title = "Playing for the weather check", IsPlaying = true };
            _settings.Weather = _settings.MediaControls = true;
            RefreshHomePlayer();
            ChoosePlace();
            if (!_weatherBox.IsEffectivelyVisible || _mediaPanel.IsVisible)
                throw new InvalidOperationException("The weather location field is hidden behind the media view.");
            EndTyping();
            EndChoosingPlace();
            if (!_mediaPanel.IsVisible || _weatherSetup.IsVisible)
                throw new InvalidOperationException("Closing the location picker did not restore the media view.");
        }
        finally
        {
            EndTyping();
            _settings.Weather = weatherEnabled;
            _settings.MediaControls = mediaEnabled;
            _info = info;
            EndChoosingPlace();
            RefreshMediaUi();
            ShowWeather();
            ApplyState(animate: false);
        }
    }

    static void CheckNotificationRestart()
    {
        var now = DateTimeOffset.UtcNow;
        var yesterday = new SystemNotification(1, "Discord", "Discord", new[] { "Example sender", "Yesterday's message" }, now.AddDays(-1));
        var another = yesterday with { Id = 2 };
        var settings = new WinNotch.AppSettings();
        string? saved = null;
        var inbox = new Inbox(_ => true, settings.ReadToasts, () => saved = System.Text.Json.JsonSerializer.Serialize(settings));
        inbox.StartWatching(now);
        ChatMessage? popped = null;
        inbox.Changed += message => popped = message;
        inbox.Apply(new NotificationSnapshot(null, new Dictionary<string, string>(), null));
        inbox.Apply(new NotificationSnapshot(new[] { yesterday }, null, null));
        if (popped != null || inbox.UnreadCount != 1)
            throw new InvalidOperationException("A late startup toast should count silently.");
        inbox.MarkRead("discord");
        if (saved == null || saved.Contains("Example sender") || saved.Contains("Yesterday's message"))
            throw new InvalidOperationException("The read receipt did not save correctly.");

        var restarted = new Inbox(_ => true, System.Text.Json.JsonSerializer.Deserialize<WinNotch.AppSettings>(saved)!.ReadToasts);
        restarted.StartWatching(now.AddSeconds(1));
        restarted.Changed += message => popped = message;
        restarted.Apply(new NotificationSnapshot(new[] { yesterday, another }, null, null));
        if (popped != null || restarted.UnreadCount != 1 || restarted.Messages[0].Id != another.Id)
            throw new InvalidOperationException("Restarting should keep read Discord notifications dismissed.");

        var incoming = yesterday with { Time = now.AddSeconds(2), Lines = new[] { "Example sender", "New message" } };
        restarted.Apply(new NotificationSnapshot(new[] { incoming, another }, null, null));
        if (popped?.Text != "New message" || restarted.UnreadCount != 2)
            throw new InvalidOperationException("A new Discord notification should pop out when its ID is reused.");
    }

    static string DescribeMotionFrames(List<long> frames)
    {
        if (frames.Count < 2) return "opening frame callbacks: no sample";
        double fps = (frames.Count - 1) / Stopwatch.GetElapsedTime(frames[0], frames[^1]).TotalSeconds;
        var intervals = frames.Zip(frames.Skip(1), (first, next) => Stopwatch.GetElapsedTime(first, next).TotalMilliseconds)
            .OrderBy(ms => ms).ToArray();
        double p90 = intervals[(int)Math.Ceiling(intervals.Length * 0.9) - 1];
        return $"opening frame callbacks: {fps:0.#}/s, 90th percentile interval {p90:0.#} ms ({frames.Count} frames)";
    }

    async Task CheckWideSpringFrameAsync()
    {
        double restingWidth = Width;
        // Put the width spring past its target, as it can be when Jelly opens the notch.
        _wSpring.Snap(_shapeW * 1.25);
        _wSpring.Retarget(_shapeW, 0.34, 0.63);
        _motionRunning = true;
        _motionLastTick = System.Diagnostics.Stopwatch.GetTimestamp();
        StepShape(Stopwatch.GetTimestamp());
        _motionRunning = false;
        // X11 reports the new client size asynchronously after the resize request.
        await Task.Delay(200);
        UpdateLayout();
        CheckNotchWidth();

        double reservedWidth = Width, reservedHeight = Height;
        OnShapeSettled();
        if (Width != reservedWidth || Height != reservedHeight)
            throw new InvalidOperationException("The animated overlay resized at the end of opening.");

        // Drive a complete close without real pointer events interrupting it.
        _hovered = false;
        ApplyState(animate: true);
        var closingSurface = (Width, Height, Position);
        for (int frame = 0; _motionRunning && frame < 240; frame++)
            StepShape(_motionLastTick + Stopwatch.Frequency / 60);
        if (_motionRunning || _shapeAnimating || _state == NotchState.Expanded)
            throw new InvalidOperationException("The closing notch did not settle.");
        if ((Width, Height, Position) != closingSurface)
            throw new InvalidOperationException("The animated overlay resized or moved at the end of closing.");
        if (_hitRegionHeight != _shapeH * _shapeScale * 1.15 + 2 || _hitRegionHeight >= Height)
            throw new InvalidOperationException("The closed notch did not release its reserved input area.");

        _hovered = true;
        double displayWidth = _screenWidth;
        _screenWidth = 400;
        ApplyState(animate: false);
        await Task.Delay(200);
        UpdateLayout();
        CheckNotchWidth();
        if (Width > _screenWidth)
            throw new InvalidOperationException("The notch window is wider than its display.");

        _screenWidth = displayWidth;
        ApplyState(animate: false);
        if (Width != restingWidth)
            throw new InvalidOperationException("The notch window did not return to its resting width.");
    }

    /// <summary>The open notch's content stays put while the shape grows around it: rounding it twice moved it a pixel to
    /// and fro each frame, and the opening notch shook.</summary>
    void CheckOpeningContentSteady()
    {
        _hovered = false;
        ApplyState(animate: false);
        _hovered = true;
        ApplyState(animate: true);
        var centres = new List<double>();
        for (int frame = 0; _motionRunning && frame < 240; frame++)
        {
            StepShape(_motionLastTick + Stopwatch.Frequency / 60);
            UpdateLayout();
            // The view's centre: its own opening scale (top centre) doesn't move it.
            if (_expandedView.TranslatePoint(new Point(_expandedView.Bounds.Width / 2, 0), this) is { } centre)
                centres.Add(centre.X);
        }
        if (centres.Count == 0 || centres.Max() - centres.Min() > 0.01)
            throw new InvalidOperationException(
                $"The open notch's content moved sideways while opening: {string.Join(", ", centres.Distinct().Select(x => x.ToString("0.###")))}.");
        ApplyState(animate: false);
    }

    /// <summary>Verity in the open notch keeps to the free stretches of her row: never on a button. Her row is the tool row,
    /// or with music (the clock, the tool words and the weather then fill the tool row) her seat beside the song's title.</summary>
    async Task CheckVerityPlacementAsync(string folder)
    {
        bool was = _settings.ShowVerity, mediaEnabled = _settings.MediaControls;
        var info = _info;
        _settings.ShowVerity = true;
        try
        {
            _info = null;
            RefreshMediaUi();
            CheckVerityOffButtons("without music");
            UpdateLayout();
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "verity-open.png"));

            _settings.MediaControls = true;
            _info = new MediaInfo { Title = "Playing for Verity", IsPlaying = true };
            RefreshMediaUi();
            CheckVerityOffButtons("with music");
            await Task.Delay(800); // the player fades in
            _hovered = true; // an actual pointer may have left meanwhile
            ApplyState(animate: false);
            UpdateLayout();
            FreezeForPicture();
            Snapshot(Path.Combine(folder, "verity-open-music.png"));
        }
        finally
        {
            _settings.ShowVerity = was;
            _settings.MediaControls = mediaEnabled;
            _info = info;
            RefreshMediaUi();
            ApplyState(animate: false);
        }
    }

    void CheckVerityOffButtons(string when)
    {
        _hovered = true;
        ApplyState(animate: false);
        UpdateLayout();
        var row = VerityFloor;
        if (!_verityLane.Active || _verityLane.Parent != row)
            throw new InvalidOperationException($"Verity isn't on her floor {when}.");
        for (int frame = 0; frame < 30 * 20; frame++)
        {
            _verityLane.Tick(1 / 30.0);
            if (frame < 90 || !_verityLane.Face.IsVisible) continue; // time to hop off a button she started on
            var face = new Rect(_verityLane.Bounds.Left + Canvas.GetLeft(_verityLane.Face), _verityLane.Bounds.Top + Canvas.GetTop(_verityLane.Face),
                _verityLane.Face.Width, _verityLane.Face.Height).Deflate(1);
            foreach (var other in row.Children)
                if (other != _verityLane && other.IsVisible && other.Bounds.Width > 0 && face.Intersects(other.Bounds))
                    throw new InvalidOperationException($"Verity stood on {other.GetType().Name} {when} (her {face}, it {other.Bounds}; row {string.Join(" | ", row.Children.Select(c => $"{c.GetType().Name} {c.IsVisible} {c.Bounds}"))}).");
        }
    }

    void CheckNotchWidth()
    {
        var left = _backdrop.TranslatePoint(new Point(0, 0), this);
        var right = _backdrop.TranslatePoint(new Point(_backdrop.Width, 0), this);
        if (left is null || right is null || left.Value.X < 0 || right.Value.X > ClientSize.Width ||
            left.Value.X < (Width - _hitRegionWidth) / 2 || right.Value.X > (Width + _hitRegionWidth) / 2)
            throw new InvalidOperationException(
                $"Notch edges at {left?.X:0.##} and {right?.X:0.##}, client width {ClientSize.Width:0.##}, " +
                $"requested width {Width:0.##}, hit region width {_hitRegionWidth:0.##}.");

    }

    void CheckExpandedVolumeFeedback()
    {
        bool statusShown = _statusSection.IsVisible;
        _statusSection.IsVisible = false;
        OnVolumeChanged(0.6f, muted: false);
        if (!_expandedVolumeView.IsVisible || _state != NotchState.Expanded)
            throw new InvalidOperationException("The open notch did not show the volume with Status hidden.");
        _volumeTimer.Stop();
        _volumePeek = false;
        _statusSection.IsVisible = statusShown;
        ApplyState(animate: false);
    }

    void CheckPhoneActionsFit()
    {
        _phoneView.IsVisible = true;
        _phoneStatus.Text = $"Receiving {new string('a', 240)}.jpg · 20%";
        var size = new Size(420, 160);
        foreach (var actions in new[] { _phoneButtons, _phoneAskButtons })
        {
            _phoneButtons.IsVisible = actions == _phoneButtons;
            _phoneAskButtons.IsVisible = actions == _phoneAskButtons;
            // Measure the new set of visible buttons immediately, before the normal layout pass.
            (actions.GetVisualParent() as Grid)?.InvalidateMeasure();
            _phoneView.InvalidateMeasure();
            _phoneView.Measure(size);
            _phoneView.Arrange(new Rect(size));
            var bottom = actions.TranslatePoint(new Point(0, actions.Bounds.Height), _phoneView);
            if (bottom is null || actions.Bounds.Height < 25 || bottom.Value.Y > _phoneView.Bounds.Height + 0.5)
                throw new InvalidOperationException(
                    $"Phone {(actions == _phoneButtons ? "Done/Copy link" : "Accept/Decline")} actions have bounds {actions.Bounds} " +
                    $"and end at {bottom} inside a card with bounds {_phoneView.Bounds}.");
        }
        _phoneStatus.Text = "";
        _phoneButtons.IsVisible = true;
        _phoneAskButtons.IsVisible = false;
        _phoneView.IsVisible = false;
    }

    static void CheckMediaButtonAlignment()
    {
        foreach (var (geometry, width, height) in new[] { (PrevGeo, 21, 15), (NextGeo, 21, 15), (PlayGeo, 18, 20), (PauseGeo, 18, 20) })
        {
            var icon = IconPath(geometry, width, height);
            var button = new Controls.NotchButton(icon);
            var size = new Size(42, 42);
            button.Measure(size);
            button.Arrange(new Rect(size));
            var drawn = icon.RenderedGeometry?.Bounds ?? throw new InvalidOperationException("Media icon geometry is missing.");
            var center = icon.Bounds.Position + (Vector)drawn.Center;
            if (Math.Abs(center.X - size.Width / 2) > 0.1 || Math.Abs(center.Y - size.Height / 2) > 0.1)
                throw new InvalidOperationException($"Media icon is drawn at {center} within a {size} button.");
        }
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

    void SnapshotNotificationBadges(string folder)
    {
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        foreach (var app in WinNotch.Notify.Messengers.All)
            strip.Children.Add(AppLogo(app.Key, BadgeLogoSize));
        strip.Children.Add(UpdateNotificationIcon(BadgeLogoSize));
        foreach (int percent in new[] { 0, 50, 100 })
        {
            var badge = UpdateNotificationIcon(BadgeLogoSize);
            badge.SetProgress(percent);
            strip.Children.Add(badge);
        }
        SnapshotIconStrip(strip, folder, "notification-badges.png");
    }

    static void SnapshotIconStrip(StackPanel strip, string folder, string file)
    {
        strip.Measure(Avalonia.Size.Infinity);
        strip.Arrange(new Rect(strip.DesiredSize));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)strip.Bounds.Width * 3, (int)strip.Bounds.Height * 3), new Vector(288, 288));
        bitmap.Render(strip);
        bitmap.Save(Path.Combine(folder, file), new PngBitmapEncoderOptions());
    }

    void FreezeForPicture()
    {
        _minute.Stop();
        _idleClock.Text = _clockBig.Text = "09:41";
        // A minute that turned over just before would leave the big clock mid-roll (faded, shifted) in the picture.
        StopTween(_clockBig, "roll");
        StopTween(_clockBig, "roll-fade");
        _clockBig.RenderTransform = null;
        _clockBig.Opacity = 1;
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

    /// <summary>With HOLM_SELFTEST_HUB=page also a picture of the Holm window on that page (hub.png), to see a design.</summary>
    async Task SnapshotHubAsync(string folder)
    {
        if (Environment.GetEnvironmentVariable("HOLM_SELFTEST_HUB") is not { Length: > 0 } page) return;
        ShowHub(page);
        await Task.Delay(1500);
        if (_settingsWindow is not { } hub) return;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)hub.Bounds.Width, (int)hub.Bounds.Height));
        bitmap.Render(hub);
        bitmap.Save(Path.Combine(folder, "hub.png"), new PngBitmapEncoderOptions());
    }
}
