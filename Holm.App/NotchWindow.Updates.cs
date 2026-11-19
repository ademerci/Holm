using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;
using WinNotch.Updates;

namespace Holm.App;

/// <summary>
/// Updates (the WPF app's MainWindow.Updates.cs; UpdateService and UpdateInstaller linked): a look at GitHub's latest
/// release shortly after start and every two hours, a pop-out and a row in the open notch when one is out, and the
/// Updates page in the Holm window. On Windows the update replaces this file and Holm restarts, as the WPF app does;
/// on Linux, where there is no Holm.exe to replace, the button opens the release page.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly Version RunningVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
    readonly CancellationTokenSource _updateLifetime = new();
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    UpdateService? _updates;
    UpdateRelease? _availableUpdate;
    bool _checkingUpdates, _installingUpdate, _updatesClosing, _updateNotification, _trimmedAfterFirstCheck;
    string? _updateStatus;
    Version? _notifiedUpdateVersion, _dismissedUpdateVersion;
    TextBlock? _updatesStatusText;
    Button? _checkUpdatesButton, _installUpdateButton;
    int _updatePercent;
    UpdateBadge? _closedUpdateBadge, _mediaUpdateBadge;
    TextBlock? _updateRowStatus;

    bool HasUpdateNotification => _availableUpdate is { } update && update.Version != _dismissedUpdateVersion;

    /// <summary>Where the update goes without the Windows installer: the release's own page.</summary>
    static bool InstallsItself => OperatingSystem.IsWindows();

    void InitUpdates()
    {
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(2);
            _ = CheckForUpdatesAsync(manual: false);
        };
        // A click on the "is available" pop-out updates, as in the WPF app.
        _messageView.PointerReleased += (_, _) =>
        {
            if (!_updateNotification || !_peekActive) return;
            _peekActive = false;
            ApplyState(animate: true);
            _ = InstallUpdateAsync();
        };
    }

    void StartUpdates() => _ = CheckForUpdatesAsync(manual: false);

    void SetAutomaticUpdateChecks(bool enabled)
    {
        _settings.CheckForUpdatesAutomatically = enabled;
        _settings.Save();
        _updateTimer.Stop();
        if (enabled) StartUpdates();
    }

    void StopUpdates()
    {
        _updatesClosing = true;
        _updateTimer.Stop();
        _updateLifetime.Cancel();
        _updates?.Dispose();
    }

    /// <summary>The Updates page of the Holm window.</summary>
    HubView UpdatesPage()
    {
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = $"Holm {RunningVersion.ToString(3)}", FontSize = 17, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "Check for a new release and update Holm when you're ready.", Foreground = HubTheme.Dim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        var automatic = new CheckBox { Content = "Check for updates automatically", IsChecked = _settings.CheckForUpdatesAutomatically, Foreground = Look.Ink, Margin = new Thickness(0, 18, 0, 0) };
        ToolTip.SetTip(automatic, "Checks shortly after Holm starts and every two hours. Updates are installed only when you choose.");
        automatic.IsCheckedChanged += (_, _) => SetAutomaticUpdateChecks(automatic.IsChecked == true);
        content.Children.Add(automatic);
        _updatesStatusText = new TextBlock { Margin = new Thickness(0, 22, 0, 16), TextWrapping = TextWrapping.Wrap };
        content.Children.Add(_updatesStatusText);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _checkUpdatesButton = HubTheme.Action("Check for updates", primary: true);
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        actions.Children.Add(_checkUpdatesButton);
        _installUpdateButton = HubTheme.Action("Update and restart");
        _installUpdateButton.Margin = new Thickness(10, 0, 0, 0);
        _installUpdateButton.Click += async (_, _) => await InstallUpdateAsync();
        actions.Children.Add(_installUpdateButton);
        content.Children.Add(actions);
        var page = new UpdatesView { Child = new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Child = content } };
        RefreshUpdateMenu(refreshNotification: false);
        return page;
    }

    sealed class UpdatesView : HubView { }

    void RefreshUpdateMenu(bool refreshNotification = true)
    {
        if (refreshNotification)
        {
            RefreshMessages();
            ApplyState(animate: true);
        }
        if (_updatesStatusText == null || _checkUpdatesButton == null || _installUpdateButton == null) return;
        _updatesStatusText.Text = _updateStatus ?? "Preview the closed-notch update badge.";
        _checkUpdatesButton.Content = "Show preview notification";
        _checkUpdatesButton.IsEnabled = !_installingUpdate;
        _installUpdateButton.Content = _installingUpdate ? "Preview running…" : "Test download ring";
        _installUpdateButton.IsEnabled = !_installingUpdate;
    }

    Task CheckForUpdatesAsync(bool manual)
    {
        if (_updatesClosing || _installingUpdate) return Task.CompletedTask;
        var placeholder = new Uri("https://example.invalid/");
        _availableUpdate = new UpdateRelease(RunningVersion, "preview", placeholder, placeholder, 0, "");
        _dismissedUpdateVersion = null;
        _notifiedUpdateVersion = null;
        _updateStatus = "Ready to preview";
        RefreshUpdateMenu();
        NotifyAvailableUpdate();
        return Task.CompletedTask;
    }

    void NotifyAvailableUpdate()
    {
        if (_availableUpdate is not { } release || _notifiedUpdateVersion == release.Version
            || _hiddenForFullscreen || _hovered || _busy || _sharing || _taskVisible
            || _peekActive || _installingUpdate || _updatesClosing) return;
        _messageLogoHost.Child = UpdateNotificationIcon(28);
        _messageTitle.Text = "Update badge preview";
        _messageText.Text = "Click to test the download ring";
        _messageText.IsVisible = true;
        _updateNotification = true;
        _notifiedUpdateVersion = release.Version;
        _peekActive = true;
        _peekMessage = true;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(8);
        _peekTimer.Start();
        ApplyState(animate: true);
    }

    sealed class UpdateBadge : Control
    {
        static readonly IBrush Blue = Brush("#FF0A84FF");
        static readonly Pen Track = new(Brush("#FF49494D"), 1.8);
        static readonly Pen Green = new(UsageRing.Ample, 1.8, lineCap: PenLineCap.Round);
        static readonly Pen Arrow = new(Brushes.White, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        static readonly Geometry Download = Geometry.Parse("M10,4.5 V12.5 M6.5,9 L10,12.5 L13.5,9 M5.5,15.5 H14.5");
        Geometry? _arc;
        bool _showRing;

        public UpdateBadge(double size) { Width = Height = size; }

        public void SetProgress(int percent)
        {
            _showRing = true;
            _arc = UsageRing.Arc(10, 9, percent);
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            using var scale = dc.PushTransform(Matrix.CreateScale(Bounds.Width / 20, Bounds.Height / 20));
            var center = new Point(10, 10);
            double radius = _showRing ? 7.4 : 10;
            dc.DrawEllipse(Blue, null, center, radius, radius);
            dc.DrawGeometry(null, Arrow, Download);
            if (!_showRing) return;
            dc.DrawEllipse(null, Track, center, 9, 9);
            if (_arc != null) dc.DrawGeometry(null, Green, _arc);
        }
    }

    static UpdateBadge UpdateNotificationIcon(double size) => new(size);

    /// <summary>The update as a notification: a logo on the badges and a row in the open notch.</summary>
    void AddUpdateNotification()
    {
        if (!HasUpdateNotification || _availableUpdate is not { } release) return;
        if (!Look.Instrument || _installingUpdate)
        {
            var logo = _closedUpdateBadge = UpdateNotificationIcon(BadgeLogoSize);
            logo.Margin = new Thickness(0, 0, 3, 0);
            var small = _mediaUpdateBadge = UpdateNotificationIcon(MediaBadgeLogoSize);
            small.Margin = new Thickness(0, 0, 3, 0);
            if (_installingUpdate)
            {
                logo.SetProgress(_updatePercent);
                small.SetProgress(_updatePercent);
                _badgeLogoList.Children.Insert(0, logo);
                _mediaBadgeLogos.Children.Insert(0, small);
            }
            else
            {
                _badgeLogoList.Children.Add(logo);
                _mediaBadgeLogos.Children.Add(small);
            }
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(UpdateNotificationIcon(28));
        var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        text.Children.Add(new TextBlock { Text = "Update badge preview", FontSize = 13, FontWeight = FontWeight.SemiBold });
        _updateRowStatus = new TextBlock { Text = _installingUpdate ? _updateStatus : "Click to test the download ring", FontSize = 12, Foreground = Dim };
        text.Children.Add(_updateRowStatus);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var button = NotchButton.Row(row);
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.IsEnabled = !_installingUpdate;
        ToolTip.SetTip(button, "Test the download ring");
        button.Click += async () =>
        {
            SetExpanded(false);
            await InstallUpdateAsync();
            if (!_updatesClosing && !_installingUpdate && _updateStatus != null) ShowPeek("î¢–", _updateStatus, "", null, seconds: 8);
        };
        _messageRows.Children.Add(DismissibleNotification(button, () =>
        {
            _dismissedUpdateVersion = release.Version;
            _peekActive = false;
            RefreshMessages();
            ApplyState(animate: true);
        }));
    }

    async Task InstallUpdateAsync()
    {
        if (_updatesClosing || _checkingUpdates || _installingUpdate || _availableUpdate is not { } release) return;
        _installingUpdate = true;
        _dismissedUpdateVersion = null;
        _updatePercent = 0;
        _updateStatus = "Preview download… 0%";
        RefreshUpdateMenu();
        try
        {
            for (int percent = 1; percent <= 100; percent++)
            {
                await Task.Delay(120, _updateLifetime.Token);
                _updatePercent = percent;
                _closedUpdateBadge?.SetProgress(percent);
                _mediaUpdateBadge?.SetProgress(percent);
                _updateStatus = $"Preview download… {percent}%";
                if (_updateRowStatus != null) _updateRowStatus.Text = _updateStatus;
                RefreshUpdateMenu(refreshNotification: false);
            }
            await Task.Delay(1800, _updateLifetime.Token);
            _updateStatus = "Preview complete. Click to replay.";
        }
        catch (OperationCanceledException) when (_updatesClosing) { }
        finally
        {
            _installingUpdate = false;
            if (!_updatesClosing) RefreshUpdateMenu();
        }
    }
}
