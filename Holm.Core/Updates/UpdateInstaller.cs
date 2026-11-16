using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Updates;

/// <summary>Replaces the portable executable after this process exits, keeping the same path and settings.</summary>
internal static class UpdateInstaller
{
    static string PowerShellPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty assembly location identifies the supported single-file deployment.")]
    public static string? UnavailableReason
    {
        get
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                return "Automatic updates are available for the Windows x64 version.";
            if (Assembly.GetExecutingAssembly().Location.Length != 0 || Environment.ProcessPath is not { } path
                || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return "Run the published Holm executable to install updates automatically.";
            if (!File.Exists(PowerShellPath))
                return "Windows PowerShell is unavailable. Download the update from the release page.";
            return null;
        }
    }

    /// <summary>Returns only after the helper has staged and verified the replacement. The caller may then shut down.</summary>
    public static async Task BeginInstallAsync(string stagedExecutable, Version expectedVersion, CancellationToken cancellationToken = default)
    {
        if (UnavailableReason is { } reason) throw new InvalidOperationException(reason);
        cancellationToken.ThrowIfCancellationRequested();
        string target = Environment.ProcessPath!;
        if (Normalize(expectedVersion) <= Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)))
            throw new InvalidOperationException("The update must be newer than the running version.");

        string work = Path.Combine(Path.GetTempPath(), "Holm", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        using var parent = Process.GetCurrentProcess();
        InstallPlan plan;
        try
        {
            plan = await Task.Run(() => CreatePlan(target, stagedExecutable, expectedVersion, parent, work), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            try { Directory.Delete(work, recursive: false); } catch { }
            throw;
        }

        Process helper;
        try { helper = StartHelper(plan); }
        catch
        {
            // A failed process launch has no helper to clean up its script.
            try { File.Delete(Path.Combine(work, "install.ps1")); Directory.Delete(work, recursive: false); } catch { }
            throw;
        }
        using var helperLifetime = helper;
        try
        {
            await WaitUntilReadyAsync(plan, helper, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The helper observes this before replacing anything; the original app remains running.
            try { File.WriteAllText(plan.CancelPath, "cancel"); } catch { }
            throw;
        }
    }

    internal static InstallPlan CreatePlan(string target, string stagedExecutable, Version expectedVersion, Process parent,
        string workDirectory, bool showErrors = true)
    {
        target = Path.GetFullPath(target);
        stagedExecutable = Path.GetFullPath(stagedExecutable);
        workDirectory = Path.GetFullPath(workDirectory);
        if (string.Equals(target, stagedExecutable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The downloaded update must be separate from the running app.");
        if (!File.Exists(target) || !File.Exists(stagedExecutable)) throw new FileNotFoundException("The app or downloaded update is missing.");
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(stagedExecutable) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Automatic updates cannot replace a linked executable.");
        if (ExecutableVersion(stagedExecutable) != Normalize(expectedVersion))
            throw new InvalidDataException("The downloaded executable does not match the release version.");

        string directory = Path.GetDirectoryName(target)!;
        // Check permission before the app exits. The helper performs the real copy before acknowledging readiness.
        string probe = Path.Combine(directory, ".winnotch-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("Holm cannot update this folder. Move the app to a folder you can write to, then try again.", ex);
        }
        finally { if (File.Exists(probe)) File.Delete(probe); }

        string suffix = Guid.NewGuid().ToString("N");
        return new InstallPlan
        {
            TargetPath = target,
            SourcePath = stagedExecutable,
            TargetHash = HashFile(target),
            SourceHash = HashFile(stagedExecutable),
            ExpectedVersion = Normalize(expectedVersion).ToString(),
            PendingPath = Path.Combine(directory, ".winnotch-update-" + suffix + ".tmp"),
            BackupPath = Path.Combine(directory, ".winnotch-previous-" + suffix + ".exe"),
            FailedPath = Path.Combine(directory, ".winnotch-failed-" + suffix + ".exe"),
            ParentId = parent.Id,
            ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks,
            WorkDirectory = workDirectory,
            DownloadCleanupRoot = DownloadCleanupRoot(stagedExecutable),
            ShowErrors = showErrors,
        };
    }

    internal static Process StartHelper(InstallPlan plan)
    {
        Directory.CreateDirectory(plan.WorkDirectory);
        string scriptPath = Path.Combine(plan.WorkDirectory, "install.ps1");
        File.WriteAllText(scriptPath, HelperScript, new UTF8Encoding(false));
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)));
        var start = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = plan.WorkDirectory,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath, "-PlanBase64", payload })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("The update helper could not be started.");
    }

    internal static async Task WaitUntilReadyAsync(InstallPlan plan, Process helper, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(plan.ErrorPath)) throw new IOException(await File.ReadAllTextAsync(plan.ErrorPath, cancellationToken).ConfigureAwait(false));
            if (File.Exists(plan.ReadyPath)) return;
            if (helper.HasExited) throw new IOException("The update helper stopped before it was ready. Holm is still running.");
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new IOException("The update helper did not become ready in time. Holm is still running.");
    }

    internal static Version ExecutableVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    // Only the download service's dedicated temporary layout is owned by the installer.
    // Never recursively delete a caller-supplied directory.
    internal static string? DownloadCleanupRoot(string source)
    {
        var directory = new FileInfo(Path.GetFullPath(source)).Directory;
        var root = directory?.Parent;
        if (!string.Equals(Path.GetFileName(source), "Holm.exe", StringComparison.OrdinalIgnoreCase)
            || directory == null || !Guid.TryParseExact(directory.Name, "N", out _)
            || root == null || !root.Name.StartsWith("Holm-update-", StringComparison.Ordinal)
            || !Guid.TryParseExact(root.Name.Substring("Holm-update-".Length), "N", out _)
            || !string.Equals(root.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return null;
        return root.FullName;
    }

    static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal sealed record InstallPlan
    {
        public string TargetPath { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public string TargetHash { get; init; } = "";
        public string SourceHash { get; init; } = "";
        public string ExpectedVersion { get; init; } = "";
        public string PendingPath { get; init; } = "";
        public string BackupPath { get; init; } = "";
        public string FailedPath { get; init; } = "";
        public int ParentId { get; init; }
        public long ParentStartTicks { get; init; }
        public string WorkDirectory { get; init; } = "";
        public string? DownloadCleanupRoot { get; init; }
        public bool ShowErrors { get; init; } = true;
        public int ExitTimeoutSeconds { get; init; } = 90;
        public int StartupGraceSeconds { get; init; } = 5;
        public string ReadyPath => Path.Combine(WorkDirectory, "ready");
        public string CancelPath => Path.Combine(WorkDirectory, "cancel");
        public string ErrorPath => Path.Combine(WorkDirectory, "error.txt");
    }

    // Paths are data in a base64 JSON argument, never interpolated into PowerShell code.
    // Only exact files created by this plan are removed; the app's settings and other directory contents are untouched.
    internal const string HelperScript = """
        param([Parameter(Mandatory=$true)][string]$PlanBase64)
        $ErrorActionPreference = 'Stop'
        $plan = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($PlanBase64)) | ConvertFrom-Json
        $replaced = $false
        $pendingCreated = $false
        $ready = $false
        $success = $false

        function Assert-FileHash([string]$path, [string]$expected) {
            if (-not [IO.File]::Exists($path)) {
                throw 'An update file changed unexpectedly. Nothing else will be replaced.'
            }
            $stream = [IO.File]::OpenRead($path)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -ne $expected) { throw 'An update file changed unexpectedly. Nothing else will be replaced.' }
        }
        function Parent-IsRunning {
            try { $parent = [Diagnostics.Process]::GetProcessById([int]$plan.ParentId) } catch { return $false }
            try { return -not $parent.HasExited -and $parent.StartTime.ToUniversalTime().Ticks -eq [long]$plan.ParentStartTicks }
            finally { $parent.Dispose() }
        }
        function Check-Cancel {
            if ([IO.File]::Exists($plan.CancelPath)) { throw 'The update was cancelled. The original app was kept.' }
        }
        function Start-App {
            $start = New-Object Diagnostics.ProcessStartInfo
            $start.FileName = $plan.TargetPath
            $start.WorkingDirectory = [IO.Path]::GetDirectoryName($plan.TargetPath)
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            return [Diagnostics.Process]::Start($start)
        }
        try {
            $targetDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($plan.TargetPath))
            foreach ($path in @($plan.PendingPath, $plan.BackupPath, $plan.FailedPath)) {
                if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $targetDirectory -or [IO.File]::Exists($path)) {
                    throw 'The update paths are not safe to use.'
                }
            }
            if (-not (Parent-IsRunning)) { throw 'Holm closed before the update was ready.' }
            Check-Cancel
            Assert-FileHash $plan.TargetPath $plan.TargetHash
            Assert-FileHash $plan.SourcePath $plan.SourceHash
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($plan.SourcePath)
            $actualVersion = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
            if ($actualVersion -ne $plan.ExpectedVersion) { throw 'The downloaded executable has the wrong version.' }
            $sourceStream = [IO.File]::OpenRead($plan.SourcePath)
            $pendingStream = $null
            try {
                $pendingStream = [IO.File]::Open($plan.PendingPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                $pendingCreated = $true
                $sourceStream.CopyTo($pendingStream)
                $pendingStream.Flush($true)
            } finally {
                if ($null -ne $pendingStream) { $pendingStream.Dispose() }
                $sourceStream.Dispose()
            }
            Assert-FileHash $plan.PendingPath $plan.SourceHash
            Check-Cancel
            [IO.File]::WriteAllText($plan.ReadyPath, 'ready')
            $ready = $true

            $wait = [Diagnostics.Stopwatch]::StartNew()
            while (Parent-IsRunning) {
                Check-Cancel
                if ($wait.Elapsed.TotalSeconds -ge $plan.ExitTimeoutSeconds) { throw 'Holm did not close in time. The original app was kept.' }
                Start-Sleep -Milliseconds 100
            }
            Check-Cancel
            Assert-FileHash $plan.TargetPath $plan.TargetHash
            Assert-FileHash $plan.PendingPath $plan.SourceHash
            $retry = [Diagnostics.Stopwatch]::StartNew()
            while (-not $replaced) {
                Check-Cancel
                try {
                    [IO.File]::Replace($plan.PendingPath, $plan.TargetPath, $plan.BackupPath, $true)
                    $replaced = $true
                } catch [IO.IOException] {
                    if ($retry.Elapsed.TotalSeconds -ge 10) { throw }
                    Start-Sleep -Milliseconds 250
                    Assert-FileHash $plan.TargetPath $plan.TargetHash
                }
            }
            Assert-FileHash $plan.TargetPath $plan.SourceHash
            $newApp = Start-App
            if ($null -eq $newApp) { throw 'The new version could not be started.' }
            try {
                if ($newApp.WaitForExit([int]($plan.StartupGraceSeconds * 1000))) { throw 'The new version closed during startup.' }
            } finally { $newApp.Dispose() }
            $success = $true
            try { [IO.File]::Delete($plan.BackupPath) } catch { }
        } catch {
            $message = $_.Exception.Message
            if ($replaced -and [IO.File]::Exists($plan.BackupPath)) {
                try {
                    [IO.File]::Replace($plan.BackupPath, $plan.TargetPath, $plan.FailedPath, $true)
                    $replaced = $false
                    $message += ' The previous version was restored.'
                    try {
                        $oldApp = Start-App
                        if ($null -ne $oldApp) { $oldApp.Dispose() }
                    } catch { $message += ' Please start Holm again from its usual location.' }
                    try { [IO.File]::Delete($plan.FailedPath) } catch { }
                } catch {
                    $message += ' The previous version is available at: ' + $plan.BackupPath
                }
            } elseif ($ready -and -not (Parent-IsRunning) -and [IO.File]::Exists($plan.TargetPath)) {
                try {
                    Assert-FileHash $plan.TargetPath $plan.TargetHash
                    $oldApp = Start-App
                    if ($null -ne $oldApp) { $oldApp.Dispose() }
                } catch { }
            }
            try { [IO.File]::WriteAllText($plan.ErrorPath, $message) } catch { }
            if ($plan.ShowErrors -and $ready -and -not (Parent-IsRunning)) {
                try {
                    Add-Type -AssemblyName PresentationFramework
                    [void][Windows.MessageBox]::Show($message, 'Holm update', 'OK', 'Error')
                } catch { }
            }
        } finally {
            if ($pendingCreated -and -not $replaced -and [IO.File]::Exists($plan.PendingPath)) {
                try { [IO.File]::Delete($plan.PendingPath) } catch { }
            }
            # The helper is the only process that could still be reading the source. By finally,
            # its streams are closed; the generated layout and SHA-256 guard prove ownership.
            if ($plan.DownloadCleanupRoot) {
                try {
                    $sourceDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($plan.SourcePath))
                    $cleanupRoot = [IO.Path]::GetFullPath($plan.DownloadCleanupRoot)
                    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
                    if ([IO.Path]::GetDirectoryName($sourceDirectory) -eq $cleanupRoot -and
                        [IO.Path]::GetDirectoryName($cleanupRoot) -eq $tempRoot -and
                        [IO.Path]::GetFileName($cleanupRoot) -cmatch '^Holm-update-[0-9a-f]{32}$' -and
                        [IO.Path]::GetFileName($sourceDirectory) -cmatch '^[0-9a-f]{32}$' -and
                        [IO.Path]::GetFileName($plan.SourcePath) -eq 'Holm.exe') {
                        Assert-FileHash $plan.SourcePath $plan.SourceHash
                        [IO.File]::Delete($plan.SourcePath)
                        try { [IO.Directory]::Delete($sourceDirectory, $false) } catch { }
                        try { [IO.Directory]::Delete($cleanupRoot, $false) } catch { }
                    }
                } catch { }
            }
            if ($success) {
                foreach ($name in @('ready', 'cancel', 'install.ps1')) {
                    try { [IO.File]::Delete([IO.Path]::Combine($plan.WorkDirectory, $name)) } catch { }
                }
                try { [IO.Directory]::Delete($plan.WorkDirectory, $false) } catch { }
            }
        }
        """;
}
