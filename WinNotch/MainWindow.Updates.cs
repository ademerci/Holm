using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinNotch.Updates;

namespace WinNotch;

public partial class MainWindow
{
    static readonly Version RunningVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(2);
    readonly CancellationTokenSource _updateLifetime = new();
    DispatcherTimer _updateTimer = null!;
    UpdateService? _updates;
    UpdateRelease? _availableUpdate;
    bool _checkingUpdates, _installingUpdate, _updatesClosing, _trimmedAfterFirstCheck;
    string? _updateStatus;
    Version? _notifiedUpdateVersion;
    bool _updateNotification;
    Version? _dismissedUpdateVersion;
    bool HasUpdateNotification => _availableUpdate is { } update && update.Version != _dismissedUpdateVersion;

    void InitUpdates()
    {
        _updateTimer = MakeTimer(20_000, () =>
        {
            _updateTimer.Interval = UpdateCheckInterval;
            _ = CheckForUpdatesAsync(manual: false);
        });
        RefreshUpdateMenu(refreshNotification: false);
    }

    void StartUpdates()
    {
        if (_updatesClosing || !_settings.CheckForUpdatesAutomatically) return;
        _updateTimer.Interval = TimeSpan.FromSeconds(20);
        _updateTimer.Start();
    }

    void StopUpdates()
    {
        _updatesClosing = true;
        _updateTimer.Stop();
        _updateLifetime.Cancel();
        _updates?.Dispose();
    }

    void RefreshUpdateMenu(bool refreshNotification = true)
    {
        if (refreshNotification)
        {
            RefreshMessages();
            ApplyState();
        }
        MenuAutoUpdates.IsChecked = _settings.CheckForUpdatesAutomatically;
        MenuUpdatesLabel.Text = _installingUpdate ? "Updating…"
            : _availableUpdate is { } available ? $"Updates · {available.Version.ToString(3)} available" : "Updates";
        MenuUpdateIcon.Visibility = _availableUpdate != null && !_installingUpdate ? Visibility.Visible : Visibility.Collapsed;
        MenuUpdateStatus.Header = _updateStatus ?? $"Current version: {RunningVersion.ToString(3)}";
        MenuCheckUpdates.Header = _checkingUpdates ? "Checking for updates…" : "Check for updates";
        MenuCheckUpdates.IsEnabled = !_checkingUpdates && !_installingUpdate;
        MenuAutoUpdates.IsEnabled = !_installingUpdate;
        MenuInstallUpdate.Visibility = _availableUpdate != null ? Visibility.Visible : Visibility.Collapsed;
        MenuInstallUpdate.Header = _installingUpdate ? "Updating…"
            : _availableUpdate is { } release ? $"Update to {release.Version.ToString(3)} and restart" : "Update and restart";
        string? unavailable = UpdateInstaller.UnavailableReason;
        bool activity = _busy || _sharing || TaskShowing;
        MenuInstallUpdate.IsEnabled = !_checkingUpdates && !_installingUpdate && !activity && unavailable == null;
        MenuInstallUpdate.ToolTip = unavailable ?? (activity ? "Finish or dismiss the current activity before restarting." : "Downloads the update and restarts Holm. Your settings stay.");
    }

    void MenuAutoUpdates_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdatesAutomatically = MenuAutoUpdates.IsChecked;
        _settings.Save();
        _updateTimer.Stop();
        if (_settings.CheckForUpdatesAutomatically) StartUpdates();
        RefreshUpdateMenu();
    }

    async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        => await CheckForUpdatesAsync(manual: true);

    async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updatesClosing || _checkingUpdates || _installingUpdate || (!manual && !_settings.CheckForUpdatesAutomatically)) return;
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
            // The check 20 s after start pulls the network and TLS code back in right after the startup trim.
            if (!manual && !_updatesClosing && !_trimmedAfterFirstCheck)
            {
                _trimmedAfterFirstCheck = true;
                _trimTimer.Start();
            }
        }
    }

    void NotifyAvailableUpdate()
    {
        if (_availableUpdate is not { } release || _notifiedUpdateVersion == release.Version
            || _hiddenForFullscreen || _expanded || _busy || _sharing || TaskShowing
            || _peekActive || _installingUpdate || _updatesClosing) return;
        MessageLogoHost.Child = UpdateNotificationIcon(28);
        MessageTitle.Text = $"Holm {release.Version.ToString(3)} is available";
        MessageText.Text = "Click to update and restart Holm";
        MessageText.Visibility = Visibility.Visible;
        _updateNotification = true;
        _notifiedUpdateVersion = release.Version;
        _peekActive = true;
        _peekMessage = true;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(8);
        _peekTimer.Start();
        ApplyState();
    }

    System.Windows.Controls.Border UpdateNotificationIcon(double size)
        => new()
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 132, 255)),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = "\uE896", FontFamily = IconFont, FontSize = size * 12 / 19,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };

    void AddUpdateNotification()
    {
        if (!HasUpdateNotification || _availableUpdate is not { } release) return;
        BadgeLogoList.Children.Add(UpdateNotificationIcon(14));
        MediaBadgeLogos.Children.Add(UpdateNotificationIcon(12));
        var row = new System.Windows.Controls.Grid();
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        row.Children.Add(UpdateNotificationIcon(28));
        var text = new System.Windows.Controls.StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        text.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"Holm {release.Version.ToString(3)} is available", FontSize = 13,
            FontWeight = FontWeights.SemiBold
        });
        text.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = _installingUpdate ? _updateStatus : "Click to update and restart Holm",
            FontSize = 12, Foreground = DimBrush
        });
        System.Windows.Controls.Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var button = new System.Windows.Controls.Button
        {
            Content = row, Style = (Style)FindResource("RowButton"),
            Margin = new Thickness(0, 0, 0, 8), IsEnabled = !_installingUpdate,
            ToolTip = "Update and restart Holm"
        };
        button.Click += async (_, _) =>
        {
            SetExpanded(false);
            await InstallUpdateAsync();
            if (!_updatesClosing && !_installingUpdate && _updateStatus != null)
                ShowPeek("\uE896", _updateStatus, "", null, false, null, seconds: 8);
        };
        MessageRows.Children.Add(DismissibleNotification(button, () =>
        {
            _dismissedUpdateVersion = release.Version;
            _peekActive = false;
            RefreshMessages();
            ApplyState();
        }));
    }

    async void MenuInstallUpdate_Click(object sender, RoutedEventArgs e)
        => await InstallUpdateAsync();

    async Task InstallUpdateAsync()
    {
        if (_updatesClosing || _checkingUpdates || _installingUpdate || _availableUpdate is not { } release) return;
        if (_busy || _sharing || TaskShowing)
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
        // A running transfer was ruled out above; don't accept another while preparing to restart.
        StopSharing();
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
            // A previously queued sharing event or a download completion may have arrived meanwhile.
            if (_busy || _sharing || TaskShowing)
            {
                _updateStatus = "Finish or dismiss the current activity, then try updating again.";
                return;
            }
            _updateStatus = "Preparing to restart…";
            RefreshUpdateMenu();
            await UpdateInstaller.BeginInstallAsync(executable, release.Version, _updateLifetime.Token);
            handedOff = true;
            // Shutdown() only queues the quit; this code goes on first. Mark the app as closing so the finally
            // below doesn't start Beam and the friends inbox again right before the process is meant to end.
            StopUpdates();
            Application.Current.Shutdown();
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
                // This path was generated here, never read from release metadata.
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
