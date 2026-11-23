using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The volume of each app, in the open notch: a slider per app that plays sound, the icon mutes it. It follows the
/// system's events only while the page is open (the WPF app's MainWindow.Mixer.cs). Where the system lets no
/// app change another's volume, the page says so.
/// </summary>
public sealed partial class NotchWindow
{
    const string MixerGlyph = "";

    readonly StackPanel _mixerPage = new() { IsVisible = false };
    readonly StackPanel _mixerRows = new();
    readonly TextBlock _mixerEmpty = new() { FontSize = 13, Foreground = Faint, Margin = new Thickness(2, 0, 2, 8), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly Dictionary<string, Bitmap?> _mixerIcons = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _mixerSoon = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer _mixerVolumeSoon = new() { Interval = TimeSpan.FromMilliseconds(35) };
    readonly Dictionary<string, float> _mixerVolumes = new(StringComparer.OrdinalIgnoreCase);
    NotchButton _mixerToolBtn = null!;
    IAppVolumePlatform _appVolumes = null!;
    Task _mixerWork = Task.CompletedTask;
    bool _mixerOpen, _mixerDragging;
    int _mixerRevision;

    void InitMixer(IPlatformServices platform)
    {
        _appVolumes = platform.AppVolumes;
        _appVolumes.Changed += () => Dispatcher.UIThread.Post(MixerChanged);
        _mixerSoon.Tick += (_, _) => { _mixerSoon.Stop(); _ = ReadMixerAsync(); };
        _mixerVolumeSoon.Tick += (_, _) => FlushMixerVolumes();
    }

    Control BuildMixerPage()
    {
        _mixerPage.Children.Add(new TextBlock { Text = "Volume per app", FontSize = 11, Foreground = Dim, Margin = new Thickness(2, 0, 0, 8) });
        _mixerPage.Children.Add(_mixerEmpty);
        _mixerPage.Children.Add(_mixerRows);
        return _mixerPage;
    }

    void ShowMixerPage()
    {
        if (!_appVolumes.Supported)
        {
            ShowMixerRows(new List<AppVolumeEntry>(), "This system doesn't let other apps change an app's volume.");
            return;
        }
        if (!_mixerOpen)
        {
            _mixerOpen = true;
            _ = QueueMixer(_appVolumes.Start);
        }
        _ = ReadMixerAsync();
    }

    /// <summary>The page closed: no more events from the system.</summary>
    void HideMixer()
    {
        _mixerSoon.Stop();
        if (!_mixerOpen) return;
        _mixerOpen = false;
        _mixerDragging = false;
        _mixerRevision++;
        FlushMixerVolumes();
        _ = QueueMixer(_appVolumes.Stop);
    }

    // Called on the UI thread. Keep native operations off-thread and in the order the UI requested them.
    Task QueueMixer(Action work)
    {
        Task previous = _mixerWork;
        return _mixerWork = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try { work(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Diag.Log("Mixer: " + ex.GetType().Name);
            }
        });
    }

    void FlushMixerVolumes()
    {
        _mixerVolumeSoon.Stop();
        if (_mixerVolumes.Count == 0) return;
        var volumes = new List<KeyValuePair<string, float>>(_mixerVolumes);
        _mixerVolumes.Clear();
        _ = QueueMixer(() =>
        {
            foreach (var (key, value) in volumes) _appVolumes.SetVolume(key, value);
        });
    }

    void MixerChanged()
    {
        if (!_mixerOpen || _mixerDragging) return;
        _mixerSoon.Stop(); // a burst of changes, read once
        _mixerSoon.Start();
    }

    async Task ReadMixerAsync()
    {
        if (!_mixerOpen || _mixerDragging) return;
        int revision = ++_mixerRevision;
        var knownIcons = new HashSet<string>(_mixerIcons.Keys, _mixerIcons.Comparer);
        List<AppVolumeEntry>? apps = null;
        var icons = new List<(string Key, AppIcon? Icon)>();
        await QueueMixer(() =>
        {
            apps = _appVolumes.Read();
            foreach (var app in apps)
                if (knownIcons.Add(app.Key)) icons.Add((app.Key, _appVolumes.IconOf(app.Key)));
        });
        if (!_mixerOpen || _mixerDragging || revision != _mixerRevision || apps == null) return;
        foreach (var (key, icon) in icons)
        {
            if (!_mixerIcons.ContainsKey(key)) _mixerIcons[key] = BitmapOf(icon);
        }
        ShowMixerRows(apps, "No app is playing sound right now.");
    }

    static Bitmap? BitmapOf(AppIcon? icon)
    {
        try
        {
            if (icon?.File is { } file) return new Bitmap(file);
            if (icon?.Pixels is { } pixels)
            {
                var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using var buffer = bitmap.Lock();
                for (int row = 0; row < pixels.Height; row++)
                    Marshal.Copy(pixels.Bgra, row * pixels.Width * 4, buffer.Address + row * buffer.RowBytes, pixels.Width * 4);
                return bitmap;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        return null;
    }

    void ShowMixerRows(List<AppVolumeEntry> apps, string emptyText)
    {
        _mixerRows.Children.Clear();
        foreach (var app in apps)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            Control icon = _mixerIcons.TryGetValue(app.Key, out var picture) && picture != null
                ? new Image { Source = picture, Width = 22, Height = 22 }
                : new TextBlock { Text = MixerGlyph, FontFamily = IconFont, FontSize = 14, Foreground = Dim };
            icon.Opacity = app.Muted ? 0.35 : 1;
            var mute = new NotchButton(icon) { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(mute, app.Muted ? "Unmute" : "Mute");
            mute.Click += () =>
            {
                if (!_mixerOpen) return;
                _mixerRevision++;
                FlushMixerVolumes();
                _ = QueueMixer(() => _appVolumes.SetMute(app.Key, !app.Muted));
                _ = ReadMixerAsync();
            };
            row.Children.Add(mute);

            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var header = new Grid();
            header.Children.Add(new TextBlock { Text = app.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 50, 0) });
            var level = new TextBlock { Text = AppVolumeList.Describe(app.Volume, app.Muted), FontSize = 11, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Right };
            header.Children.Add(level);
            right.Children.Add(header);
            var slider = new MiniSlider { Value = app.Volume, Margin = new Thickness(0, 4, 0, 0), Fill = app.Muted ? Dim : Look.Ink };
            string key = app.Key;
            slider.ValueChanging += v =>
            {
                if (!_mixerOpen) return;
                _mixerDragging = true;
                _mixerRevision++;
                level.Text = AppVolumeList.Describe((float)v, false);
                _mixerVolumes[key] = (float)v;
                if (!_mixerVolumeSoon.IsEnabled) _mixerVolumeSoon.Start();
            };
            slider.ValueCommitted += _ =>
            {
                if (!_mixerOpen) return;
                _mixerDragging = false;
                FlushMixerVolumes();
                MixerChanged();
            };
            right.Children.Add(slider);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            _mixerRows.Children.Add(row);
        }
        _mixerEmpty.Text = emptyText;
        _mixerEmpty.IsVisible = apps.Count == 0;
        if (_state == NotchState.Expanded) ApplyState(animate: true);
    }
}
