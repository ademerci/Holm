using System.Windows.Threading;

namespace WinNotch.Tests;

public class PrivacyMonitorTests
{
    static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    static long Ft(DateTime t) => t.ToFileTimeUtc();

    static List<SensorUse> Active(params SensorEntry[] entries)
        => PrivacyMonitor.Active(Sensor.Microphone, entries, (path, _) => Path.GetFileNameWithoutExtension(path) is "Discord" or "chrome", Now).ToList();

    [Theory]
    [InlineData(@"C:\Discord\app-old\Discord.exe", -30, false)]
    [InlineData(@"C:\Discord\app-current\Discord.exe", -5, false)]
    [InlineData(@"C:\Discord\app-current\Discord.exe", -30, true)]
    [InlineData(@"c:\discord\APP-CURRENT\discord.exe", -30, true)]
    public void Desktop_recording_requires_the_same_executable_and_process_lifetime(string runningPath, int startedMinutesAgo, bool expected)
    {
        var since = Now.AddMinutes(-12);
        var uses = PrivacyMonitor.Active(Sensor.Microphone,
            new[] { new SensorEntry(@"C:#Discord#app-current#Discord.exe", true, Ft(since), 0) },
            (path, start) => PrivacyMonitor.MatchesRecording(path, start, runningPath, Now.AddMinutes(startedMinutesAgo)), Now);
        Assert.Equal(expected, uses.Any());
    }

    [Fact]
    public void An_app_that_started_and_did_not_stop_is_using_it()
    {
        var uses = Active(
            new SensorEntry(@"C:#Users#me#AppData#Local#Discord#app-1.0.9#Discord.exe", true, Ft(Now.AddMinutes(-12)), 0),
            new SensorEntry(@"C:#Program Files#Google#Chrome#Application#chrome.exe", true, Ft(Now.AddHours(-2)), Ft(Now.AddHours(-1))));

        var use = Assert.Single(uses);
        Assert.Equal("Discord", use.Name);
        Assert.Equal("Discord", use.Key);
        Assert.Equal(Now.AddMinutes(-12), use.Since);
        Assert.Equal(Sensor.Microphone, use.Sensor);
    }

    [Fact]
    public void Never_used_entries_are_skipped()
        => Assert.Empty(Active(new SensorEntry("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", false, 0, 0)));

    [Fact]
    public void A_desktop_app_that_is_no_longer_running_is_not_using_it()
        // It crashed while recording, so Windows never wrote the stop time.
        => Assert.Empty(Active(new SensorEntry(@"C:#Program Files#Zoom#bin#Zoom.exe", true, Ft(Now.AddMinutes(-5)), 0)));

    [Fact]
    public void A_store_app_counts_for_twelve_hours_at_most()
    {
        Assert.Equal("WhatsApp", Assert.Single(Active(new SensorEntry("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", false, Ft(Now.AddHours(-1)), 0))).Name);
        Assert.Empty(Active(new SensorEntry("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", false, Ft(Now.AddHours(-13)), 0)));
    }

    [Fact]
    public void Broken_times_are_skipped()
        => Assert.Empty(Active(new SensorEntry("Contoso.App_abc", false, long.MaxValue, 0)));

    [Theory]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "WhatsApp")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Camera")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "Teams")]
    [InlineData("Claude_pzs8sxrjxfjjc", "Claude")]
    [InlineData("Contoso.PhotoBooth_1234abcd", "Photo Booth")]
    public void Store_apps_get_readable_names(string package, string expected)
        => Assert.Equal(expected, PrivacyMonitor.NameOfPackage(package));

    [Theory]
    [InlineData("msedge", "Edge")]
    [InlineData("ms-teams", "Teams")]
    [InlineData("obs64", "OBS Studio")]
    [InlineData("SomethingElse", "SomethingElse")]
    public void Known_desktop_apps_get_their_usual_names(string process, string expected)
        => Assert.Equal(expected, PrivacyMonitor.NameOfProcess(process));

    /// <summary>
    /// The whole way, in a test copy of the record (HKCU\Software\Holm.Tests, removed afterwards): an app starts
    /// recording, Windows tells the monitor, the monitor reports it; then it stops.
    /// </summary>
    [Fact, Trait("Category", "Local")]
    public void Notices_an_app_starting_and_stopping_to_record()
    {
        string store = @"Software\Holm.Tests\ConsentStore" + Guid.NewGuid().ToString("N");
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        string process = currentProcess.ProcessName;
        string executable = currentProcess.MainModule!.FileName.Replace('\\', '#');
        string app = $@"{store}\microphone\NonPackaged\{executable}";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(app))
        {
            key.SetValue("LastUsedTimeStart", 0L, Microsoft.Win32.RegistryValueKind.QWord);
            key.SetValue("LastUsedTimeStop", 0L, Microsoft.Win32.RegistryValueKind.QWord);
        }
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{store}\webcam").Dispose();

        var changes = new System.Collections.Concurrent.BlockingCollection<IReadOnlyList<SensorUse>>();
        PrivacyMonitor? monitor = null;
        Dispatcher? ui = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            ui = Dispatcher.CurrentDispatcher;
            monitor = new PrivacyMonitor(ui, store);
            monitor.Changed += () => changes.Add(monitor.InUse);
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(monitor!.InUse);

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(app, writable: true)!)
                key.SetValue("LastUsedTimeStart", DateTime.UtcNow.ToFileTimeUtc(), Microsoft.Win32.RegistryValueKind.QWord);
            Assert.True(changes.TryTake(out var started, TimeSpan.FromSeconds(5)), "No change reported after an app started recording.");
            Assert.Equal(process, Assert.Single(started!).Key);

            var restartedAt = DateTime.UtcNow;
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(app, writable: true)!)
                key.SetValue("LastUsedTimeStart", restartedAt.ToFileTimeUtc(), Microsoft.Win32.RegistryValueKind.QWord);
            Assert.True(changes.TryTake(out var restarted, TimeSpan.FromSeconds(5)), "No change reported when the same app started a new recording.");
            Assert.Equal(restartedAt, Assert.Single(restarted!).Since);

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(app, writable: true)!)
                key.SetValue("LastUsedTimeStop", DateTime.UtcNow.ToFileTimeUtc(), Microsoft.Win32.RegistryValueKind.QWord);
            Assert.True(changes.TryTake(out var stopped, TimeSpan.FromSeconds(5)), "No change reported after it stopped.");
            Assert.Empty(stopped!);
        }
        finally
        {
            ui?.Invoke(() => monitor?.Dispose());
            ui?.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5));
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(store, throwOnMissingSubKey: false);
            using (var tests = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Holm.Tests"))
                if (tests is { SubKeyCount: 0 }) Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\Holm.Tests", false);
        }
    }

    [Fact, Trait("Category", "Local")]
    public void Rechecks_active_camera_when_its_stop_notification_is_missed()
    {
        string store = @"Software\Holm.Tests\ConsentStore" + Guid.NewGuid().ToString("N");
        string app = $@"{store}\webcam\Microsoft.WindowsCamera_8wekyb3d8bbwe";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(app))
        {
            key.SetValue("LastUsedTimeStart", DateTime.UtcNow.ToFileTimeUtc(), Microsoft.Win32.RegistryValueKind.QWord);
            key.SetValue("LastUsedTimeStop", 0L, Microsoft.Win32.RegistryValueKind.QWord);
        }
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            PrivacyMonitor? monitor = null;
            try
            {
                var ui = Dispatcher.CurrentDispatcher;
                monitor = new PrivacyMonitor(ui, store);
                Assert.Equal("Camera", Assert.Single(monitor.InUse).Name);
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                // Simulate a lost Windows notification by unregistering only the event callbacks.
                var watches = (System.Collections.IEnumerable)typeof(PrivacyMonitor).GetField("_watches", flags)!.GetValue(monitor)!;
                foreach (var watch in watches)
                    ((RegisteredWaitHandle?)watch.GetType().GetField("Wait")!.GetValue(watch))?.Unregister(null);
                var verify = (DispatcherTimer)typeof(PrivacyMonitor).GetField("_verify", flags)!.GetValue(monitor)!;
                verify.Interval = TimeSpan.FromMilliseconds(50);
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(app, writable: true)!)
                    key.SetValue("LastUsedTimeStop", DateTime.UtcNow.ToFileTimeUtc(), Microsoft.Win32.RegistryValueKind.QWord);
                var frame = new DispatcherFrame();
                int changes = 0;
                monitor.Changed += () => { changes++; frame.Continue = false; };
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timeout.Tick += (_, _) => frame.Continue = false;
                timeout.Start();
                Dispatcher.PushFrame(frame);
                timeout.Stop();
                Assert.Equal(1, changes);
                Assert.Empty(monitor.InUse);
                Assert.False(verify.IsEnabled);
                monitor.Dispose();
                Assert.False(verify.IsEnabled);
            }
            catch (Exception ex) { failure = ex; }
            finally { monitor?.Dispose(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(store, throwOnMissingSubKey: false); }
    }

    /// <summary>Reads this PC's record without changing anything.</summary>
    [Fact, Trait("Category", "Local")]
    public void Reads_this_PCs_record_and_stops_cleanly()
    {
        var monitor = new PrivacyMonitor(Dispatcher.CurrentDispatcher);
        Assert.All(monitor.InUse, u => Assert.False(string.IsNullOrWhiteSpace(u.Name)));
        monitor.Dispose();
        monitor.Dispose();
    }
}
