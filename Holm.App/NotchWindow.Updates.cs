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

    void StartUpdates()
    {
        if (PackageIdentity.IsPackaged || _updatesClosing || !_settings.CheckForUpdatesAutomatically) return;
        _updateTimer.Interval = TimeSpan.FromSeconds(20);
        _updateTimer.Start();
    }

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
        if (PackageIdentity.IsPackaged)
        {
            // The Store version: Microsoft Store updates it, so the page only says so.
            content.Children.Add(new TextBlock { Text = "Updates are managed by Microsoft Store.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            return new UpdatesView { Child = new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Child = content } };
        }
        content.Children.Add(new TextBlock { Text = "Check for a new release and update Holm when you're ready.", Foreground = HubTheme.Dim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        var automatic = new CheckBox { Content = "Check for updates automatically", IsChecked = _settings.CheckForUpdatesAutomatically, Foreground = Brushes.White, Margin = new Thickness(0, 18, 0, 0) };
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
        string? unavailable = InstallsItself ? UpdateInstaller.UnavailableReason : null;
        _updatesStatusText.Text = _updateStatus ?? "Check for updates when you want to see if a new version is available.";
        _checkUpdatesButton.Content = _checkingUpdates ? "Checking…" : "Check for updates";
        _checkUpdatesButton.IsEnabled = !_checkingUpdates && !_installingUpdate;
        _installUpdateButton.IsVisible = _availableUpdate != null;
        _installUpdateButton.Content = _installingUpdate ? "Updating…"
            : _availableUpdate is { } release ? (InstallsItself ? $"Update to {release.Version.ToString(3)} and restart" : $"Get {release.Version.ToString(3)}")
            : "Update and restart";
        _installUpdateButton.IsEnabled = !_checkingUpdates && !_installingUpdate && unavailable == null;
        ToolTip.SetTip(_installUpdateButton, unavailable ?? (InstallsItself ? "Downloads the update and restarts Holm. Your settings stay." : "Opens the release page to download it."));
        if (unavailable != null && _availableUpdate != null) _updatesStatusText.Text += "\n" + unavailable;
    }

    async Task CheckForUpdatesAsync(bool manual)
    {
        if (PackageIdentity.IsPackaged || _updatesClosing || _checkingUpdates || _installingUpdate || (!manual && !_settings.CheckForUpdatesAutomatically)) return;
        _checkingUpdates = true;
        if (manual) _updateStatus = "Checking GitHub for a new release…";
        RefreshUpdateMenu();
        try
        {
            _updates ??= new UpdateService();
            var release = await _updates.CheckAsync(RunningVersion, _updateLifetime.Token);
            if (_updatesClosing) return;
            _availableUpdate = release;
            _updateStatus = release != null ? $"Version {release.Version.ToString(3)} is ready to download"
                : manual ? $"You're up to date · {RunningVersion.ToString(3)}" : null;
        }
        catch (OperationCanceledException) when (_updatesClosing) { }
        catch (Exception ex)
        {
            Diag.Log("Update check failed: " + ex.Message);
            if (manual) _updateStatus = "Couldn't check for updates. Please try again.";
        }
        finally
        {
            _checkingUpdates = false;
            if (!_updatesClosing)
            {
                RefreshUpdateMenu();
                NotifyAvailableUpdate();
            }
            if (!manual && !_updatesClosing && !_trimmedAfterFirstCheck)
            {
                _trimmedAfterFirstCheck = true; // the first look pulled in the network stack and JSON
                _trimTimer.Start();
            }
        }
    }

    void NotifyAvailableUpdate()
    {
        if (_availableUpdate is not { } release || _notifiedUpdateVersion == release.Version
            || _hiddenForFullscreen || _hovered || _busy || _sharing || _taskVisible
            || _peekActive || _installingUpdate || _updatesClosing) return;
        _messageLogoHost.Child = UpdateNotificationIcon(28);
        _messageTitle.Text = $"Holm {release.Version.ToString(3)} is available";
        _messageText.Text = InstallsItself ? "Click to update and restart Holm" : "Click to get it";
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

    static Border UpdateNotificationIcon(double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brush("#FF0A84FF"),
        Child = new TextBlock
        {
            Text = "", FontFamily = IconFont, FontSize = size * 12 / 19, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>The update as a notification: a logo on the badges and a row in the open notch.</summary>
    void AddUpdateNotification()
    {
        if (!HasUpdateNotification || _availableUpdate is not { } release) return;
        if (!Look.Instrument)
        {
            var logo = UpdateNotificationIcon(BadgeLogoSize);
            logo.Margin = new Thickness(0, 0, 3, 0);
            _badgeLogoList.Children.Add(logo);
            var small = UpdateNotificationIcon(MediaBadgeLogoSize);
            small.Margin = new Thickness(0, 0, 3, 0);
            _mediaBadgeLogos.Children.Add(small);
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(UpdateNotificationIcon(28));
        var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        text.Children.Add(new TextBlock { Text = $"Holm {release.Version.ToString(3)} is available", FontSize = 13, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock { Text = _installingUpdate ? _updateStatus : InstallsItself ? "Click to update and restart Holm" : "Click to get it", FontSize = 12, Foreground = Dim });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var button = NotchButton.Row(row);
        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.IsEnabled = !_installingUpdate;
        ToolTip.SetTip(button, "Update and restart Holm");
        button.Click += async () =>
        {
            SetExpanded(false);
            await InstallUpdateAsync();
            if (!_updatesClosing && !_installingUpdate && _updateStatus != null) ShowPeek("", _updateStatus, "", null, seconds: 8);
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
        if (!InstallsItself)
        {
            try { _shell.Open(release.ReleaseUrl.ToString()); } catch (Exception ex) { Diag.Log("Release page: " + ex.Message); }
            return;
        }
        if (_busy || _sharing || _taskVisible)
        {
            _updateStatus = "Finish or dismiss the current activity, then try again.";
            RefreshUpdateMenu();
            return;
        }
        if (UpdateInstaller.UnavailableReason is { } reason)
        {
            _updateStatus = reason;
            RefreshUpdateMenu();
            return;
        }
        _installingUpdate = true;
        _updateStatus = "Downloading update…";
        RefreshUpdateMenu();
        StopSharing(); // no new transfers while preparing to restart
        string staging = Path.Combine(Path.GetTempPath(), "Holm-update-" + Guid.NewGuid().ToString("N"));
        bool handedOff = false;
        try
        {
            _updates ??= new UpdateService();
            var progress = new Progress<int>(percent =>
            {
                if (_updatesClosing || !_installingUpdate) return;
                _updateStatus = $"Downloading update… {percent}%";
                RefreshUpdateMenu();
            });
            string executable = await _updates.DownloadAsync(release, staging, progress, _updateLifetime.Token);
            if (_updatesClosing) return;
            if (_busy || _sharing || _taskVisible)
            {
                _updateStatus = "Finish or dismiss the current activity, then try updating again.";
                return;
            }
            _updateStatus = "Preparing to restart…";
            RefreshUpdateMenu();
            await UpdateInstaller.BeginInstallAsync(executable, release.Version, _updateLifetime.Token);
            handedOff = true;
            StopUpdates();
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (OperationCanceledException) when (_updatesClosing) { }
        catch (Exception ex)
        {
            Diag.Error(ex);
            _updateStatus = "Update couldn't finish. Your current version is unchanged; try again.";
        }
        finally
        {
            _installingUpdate = false;
            if (!handedOff)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception ex) { Diag.Log("Update staging cleanup: " + ex.Message); }
            }
            if (!_updatesClosing)
            {
                StartSharing();
                RefreshUpdateMenu();
            }
        }
    }
}
