using System.Diagnostics;
using WinNotch.Updates;

namespace WinNotch.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public void Development_build_cannot_replace_its_apphost()
    {
        Assert.Contains("published", UpdateInstaller.UnavailableReason);
    }

    [Fact]
    public void A_version_mismatch_is_rejected_before_handoff()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.CreatePlan(files.Target, files.Source, new Version(99, 0), parent, files.Work));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
        Assert.Empty(Directory.GetFiles(files.Directory, ".winnotch-*"));
    }

    [Fact]
    public void The_running_file_cannot_be_used_as_its_own_update()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        Assert.Throws<InvalidOperationException>(() => UpdateInstaller.CreatePlan(files.Target, files.Target,
            UpdateInstaller.ExecutableVersion(files.Target), parent, files.Work));
    }

    [Fact]
    public async Task A_changed_download_is_rejected_without_touching_the_app()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        var plan = files.Plan(parent);
        File.AppendAllText(files.Source, "corrupted after verification");
        using var helper = UpdateInstaller.StartHelper(plan);
        await Finished(helper);

        Assert.Contains("changed unexpectedly", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.False(File.Exists(plan.ReadyPath));
        Assert.False(File.Exists(plan.PendingPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
    }

    [Fact]
    public async Task A_reused_process_id_does_not_allow_replacement()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        var plan = files.Plan(parent) with { ParentStartTicks = 1 };
        using var helper = UpdateInstaller.StartHelper(plan);
        await Finished(helper);

        Assert.Contains("closed before", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.False(File.Exists(plan.ReadyPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
    }

    [Fact]
    public async Task An_existing_pending_file_is_never_overwritten_or_deleted()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        var plan = files.Plan(parent);
        File.WriteAllText(plan.PendingPath, "unrelated existing file");
        using var helper = UpdateInstaller.StartHelper(plan);
        await Finished(helper);

        Assert.Contains("not safe", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.Equal("unrelated existing file", await File.ReadAllTextAsync(plan.PendingPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
    }

    [Fact]
    public async Task Waiting_for_the_app_to_exit_is_bounded_and_preserves_it()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        var plan = files.Plan(parent) with { ExitTimeoutSeconds = 1 };
        using var helper = UpdateInstaller.StartHelper(plan);
        await Finished(helper);

        Assert.True(File.Exists(plan.ReadyPath));
        Assert.Contains("did not close in time", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.False(File.Exists(plan.PendingPath));
        Assert.False(File.Exists(plan.BackupPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
    }

    [Fact]
    public async Task Cancellation_after_readiness_keeps_the_original_executable()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        var plan = files.Plan(parent);
        using var helper = UpdateInstaller.StartHelper(plan);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await UpdateInstaller.WaitUntilReadyAsync(plan, helper, timeout.Token);
        await File.WriteAllTextAsync(plan.CancelPath, "cancel");
        await Finished(helper);

        Assert.Contains("cancelled", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.False(File.Exists(plan.PendingPath));
        Assert.False(File.Exists(plan.BackupPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
    }

    [Fact]
    public async Task Cancellation_before_readiness_cleans_the_owned_download()
    {
        using var files = new Fixture();
        using var parent = Process.GetCurrentProcess();
        string downloadRoot = Path.Combine(Path.GetTempPath(), "Holm-update-" + Guid.NewGuid().ToString("N"));
        string downloadDirectory = Path.Combine(downloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadDirectory);
        string downloadedExe = Path.Combine(downloadDirectory, "Holm.exe");
        File.Copy(files.Source, downloadedExe);
        try
        {
            var plan = UpdateInstaller.CreatePlan(files.Target, downloadedExe,
                UpdateInstaller.ExecutableVersion(downloadedExe), parent, files.Work, showErrors: false);
            await File.WriteAllTextAsync(plan.CancelPath, "cancel");
            using var helper = UpdateInstaller.StartHelper(plan);
            await Finished(helper);

            Assert.Contains("cancelled", await File.ReadAllTextAsync(plan.ErrorPath));
            Assert.False(File.Exists(plan.ReadyPath));
            Assert.False(File.Exists(plan.PendingPath));
            Assert.False(Directory.Exists(downloadRoot));
            Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
        }
        finally
        {
            if (File.Exists(downloadedExe)) File.Delete(downloadedExe);
            if (Directory.Exists(downloadDirectory)) Directory.Delete(downloadDirectory);
            if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot);
        }
    }

    [Fact]
    public async Task An_update_that_cannot_start_restores_the_original_file()
    {
        using var files = new Fixture();
        using var parent = StartExitingParent();
        var plan = files.Plan(parent);
        using var helper = UpdateInstaller.StartHelper(plan);
        await Finished(helper);

        // The staged where.exe exits immediately with no arguments, triggering the rollback.
        Assert.Contains("previous version was restored", await File.ReadAllTextAsync(plan.ErrorPath));
        Assert.Equal(files.TargetHash, UpdateInstaller.HashFile(files.Target));
        Assert.False(File.Exists(plan.BackupPath));
        Assert.False(File.Exists(plan.FailedPath));
        await parent.WaitForExitAsync();
    }

    [Fact]
    public async Task A_valid_update_replaces_restarts_and_cleans_its_download()
    {
        using var files = new Fixture();
        string downloadRoot = Path.Combine(Path.GetTempPath(), "Holm-update-" + Guid.NewGuid().ToString("N"));
        string downloadDirectory = Path.Combine(downloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadDirectory);
        string downloadedExe = Path.Combine(downloadDirectory, "Holm.exe");
        try
        {
            // Use Microsoft's signed PowerShell host as a hidden, long-running replacement.
            // Application Control may block unsigned test executables even though it permits Holm.
            string windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            File.Copy(windowsPowerShell, downloadedExe);

            string downloadedHash = UpdateInstaller.HashFile(downloadedExe);
            using var parent = StartExitingParent();
            var expectedVersion = UpdateInstaller.ExecutableVersion(downloadedExe);
            var plan = UpdateInstaller.CreatePlan(files.Target, downloadedExe, expectedVersion, parent, files.Work, showErrors: false)
                with { StartupGraceSeconds = 1 };
            Assert.Equal(downloadRoot, plan.DownloadCleanupRoot);
            using var helper = UpdateInstaller.StartHelper(plan);
            await Finished(helper);

            Assert.False(File.Exists(plan.ErrorPath), File.Exists(plan.ErrorPath) ? await File.ReadAllTextAsync(plan.ErrorPath) : null);
            Assert.Equal(downloadedHash, UpdateInstaller.HashFile(files.Target));
            Assert.False(File.Exists(plan.BackupPath));
            Assert.False(File.Exists(plan.PendingPath));
            if (Directory.Exists(downloadRoot))
                Assert.Fail("The helper left: " + string.Join(", ", Directory.GetFileSystemEntries(downloadRoot).Select(Path.GetFileName)));
            var restarted = Process.GetProcessesByName("Holm").FirstOrDefault(process =>
            {
                try { return string.Equals(Path.GetFullPath(process.MainModule!.FileName!), files.Target, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
            Assert.NotNull(restarted);
            try
            {
                if (!restarted!.HasExited) restarted.Kill();
                await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { restarted!.Dispose(); }
            await parent.WaitForExitAsync();
        }
        finally
        {
            if (File.Exists(downloadedExe)) File.Delete(downloadedExe);
            if (Directory.Exists(downloadDirectory)) Directory.Delete(downloadDirectory);
            if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot);
        }
    }

    [Fact]
    public void Cleanup_only_owns_the_generated_download_layout()
    {
        using var files = new Fixture();
        Assert.Null(UpdateInstaller.DownloadCleanupRoot(files.Source));
        Assert.Null(UpdateInstaller.DownloadCleanupRoot(Path.Combine(Path.GetTempPath(), "Holm-update-important", Guid.NewGuid().ToString("N"), "Holm.exe")));
        Assert.Null(UpdateInstaller.DownloadCleanupRoot(Path.Combine(Path.GetTempPath(), "Holm-update-" + Guid.NewGuid().ToString("N"), "user files", "Holm.exe")));
    }

    // A separate, harmless process simulates the app exiting after it receives readiness.
    static Process StartExitingParent()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 6" }) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    static async Task Finished(Process process)
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(0, process.ExitCode);
    }

    sealed class Fixture : IDisposable
    {
        readonly TempFolder _folder = new();
        public string Directory { get; }
        public string Target { get; }
        public string Source { get; }
        public string Work { get; }
        public string TargetHash { get; }

        public Fixture()
        {
            // Apostrophes, spaces and shell characters must remain literal path data.
            Directory = Path.Combine(_folder.Path, "User's app & files [test]");
            System.IO.Directory.CreateDirectory(Directory);
            Work = Path.Combine(_folder.Path, "helper's files");
            System.IO.Directory.CreateDirectory(Work);
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Target = Path.Combine(Directory, "Holm.exe");
            Source = Path.Combine(_folder.Path, "new Holm.exe");
            File.Copy(Path.Combine(system, "find.exe"), Target);
            File.Copy(Path.Combine(system, "where.exe"), Source);
            TargetHash = UpdateInstaller.HashFile(Target);
        }

        public UpdateInstaller.InstallPlan Plan(Process parent) => UpdateInstaller.CreatePlan(Target, Source,
            UpdateInstaller.ExecutableVersion(Source), parent, Work, showErrors: false);

        public void Dispose() => _folder.Dispose();
    }
}
