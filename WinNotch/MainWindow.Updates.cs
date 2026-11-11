using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Updates;

namespace WinNotch;

public partial class MainWindow
{
    static readonly Version RunningVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
    readonly CancellationTokenSource _updateLifetime = new();
    DispatcherTimer _updateTimer = null!;
    bool _trimmedAfterFirstCheck;
    UpdateService? _updates;
    UpdateRelease? _availableUpdate;
    bool _checkingUpdates, _installingUpdate, _updatesClosing;
    string? _updateStatus;
    Version? _notifiedUpdateVersion;
    bool _updateNotification;
    Version? _dismissedUpdateVersion;
    bool HasUpdateNotification => _availableUpdate is { } update && update.Version != _dismissedUpdateVersion;

    TextBlock? _updatesStatusText;
    Button? _checkUpdatesButton, _installUpdateButton;

    void InitUpdates()
    {
        _updateTimer = MakeTimer(20_000, () =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(2);
            _ = CheckForUpdatesAsync(manual: false);
        });
    }

    void StartUpdates()
    {
        if (_updatesClosing || !_settings.CheckForUpdatesAutomatically) return;
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

    FrameworkElement UpdatesPage()
    {
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = $"Holm {RunningVersion.ToString(3)}", FontSize = 17, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Check for a new release and update Holm when you're ready.", Foreground = HubTheme.Dim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        var automatic = new CheckBox
        {
            Content = "Check for updates automatically", IsChecked = _settings.CheckForUpdatesAutomatically,
            Foreground = Brushes.White, Margin = new Thickness(0, 18, 0, 0),
            ToolTip = "Checks shortly after Holm starts and every two hours. Updates are installed only when you choose."
        };
        automatic.Checked += (_, _) => SetAutomaticUpdateChecks(true);
        automatic.Unchecked += (_, _) => SetAutomaticUpdateChecks(false);
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
        var page = new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Child = content };
        RefreshUpdateMenu(refreshNotification: false);
        return page;
    }

    void RefreshUpdateMenu(bool refreshNotification = true)
    {
        if (refreshNotification)
        {
            RefreshMessages();
            ApplyState();
        }
        if (_updatesStatusText == null || _checkUpdatesButton == null || _installUpdateButton == null) return;
        string? unavailable = UpdateInstaller.UnavailableReason;
        _updatesStatusText.Text = _updateStatus ?? "Check for updates when you want to see if a new version is available.";
        _checkUpdatesButton.Content = _checkingUpdates ? "Checking…" : "Check for updates";
        _checkUpdatesButton.IsEnabled = !_checkingUpdates && !_installingUpdate;
        _installUpdateButton.Visibility = _availableUpdate != null ? Visibility.Visible : Visibility.Collapsed;
        _installUpdateButton.Content = _installingUpdate ? "Updating…" : _availableUpdate is { } release ? $"Update to {release.Version.ToString(3)} and restart" : "Update and restart";
        _installUpdateButton.IsEnabled = !_checkingUpdates && !_installingUpdate && unavailable == null;
        _installUpdateButton.ToolTip = unavailable ?? "Downloads the update and restarts Holm. Your settings stay.";
        if (unavailable != null && _availableUpdate != null) _updatesStatusText.Text += "\n" + unavailable;
    }

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
