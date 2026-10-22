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
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    readonly CancellationTokenSource _updateLifetime = new();
    DispatcherTimer _updateTimer = null!;
    UpdateService? _updates;
    UpdateRelease? _availableUpdate;
    bool _checkingUpdates, _installingUpdate, _updatesClosing;
    string? _updateStatus;

    void InitUpdates()
    {
        _updateTimer = MakeTimer(20_000, () =>
        {
            _updateTimer.Interval = UpdateCheckInterval;
            _ = CheckForUpdatesAsync(manual: false);
        });
        RefreshUpdateMenu();
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

    void RefreshUpdateMenu()
    {
        MenuAutoUpdates.IsChecked = _settings.CheckForUpdatesAutomatically;
        MenuUpdates.Header = _installingUpdate ? "Updating…"
            : _availableUpdate is { } available ? $"Updates · {available.Version.ToString(3)} available" : "Updates";
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
        MenuInstallUpdate.ToolTip = unavailable ?? (activity ? "Finish or dismiss the current activity before restarting." : "Downloads the update and restarts WinNotch. Your settings stay.");
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
            if (!_updatesClosing) RefreshUpdateMenu();
        }
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
        string staging = Path.Combine(Path.GetTempPath(), "WinNotch-update-" + Guid.NewGuid().ToString("N"));
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
