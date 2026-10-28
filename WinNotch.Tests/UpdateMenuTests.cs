using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinNotch.Updates;

namespace WinNotch.Tests;

public class UpdateMenuTests
{
    [Fact]
    public void Manual_check_works_with_automatic_checks_disabled_and_exposes_new_release()
        => WithWindow(async window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.CheckForUpdatesAutomatically = false;
            using var http = new HttpClient(new ResponseHandler(ReleaseJson("99.0.0")));
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: true);
            Assert.Equal(Visibility.Visible, Item(window, "MenuInstallUpdate").Visibility);
            Assert.Contains("99.0.0", Item(window, "MenuInstallUpdate").Header.ToString());
            Assert.Contains("available", ((TextBlock)((StackPanel)Item(window, "MenuUpdates").Header).Children[0]).Text);
            Assert.Equal(Visibility.Visible, ((Border)((StackPanel)Item(window, "MenuUpdates").Header).Children[1]).Visibility);
            Assert.False(Item(window, "MenuAutoUpdates").IsChecked);
        });

    [Fact]
    public void Update_notification_uses_message_layout_and_does_not_repeat_the_same_version()
        => WithWindow(async window =>
        {
            using var http = new HttpClient(new ResponseHandler(ReleaseJson("99.0.0")));
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: true);
            Assert.True(Field<bool>(window, "_updateNotification"));
            Assert.True(Field<bool>(window, "_peekMessage"));
            Assert.Contains("99.0.0", ((TextBlock)window.FindName("MessageTitle")).Text);
            Assert.Contains("Click to update", ((TextBlock)window.FindName("MessageText")).Text);
            Set(window, "_peekActive", false);
            Set(window, "_updateNotification", false);
            await Check(window, manual: true);
            Assert.False(Field<bool>(window, "_updateNotification"));
        });

    [Fact]
    public void Available_update_stays_in_notification_rows_and_closed_notch_badges()
        => WithWindow(async window =>
        {
            using var http = new HttpClient(new ResponseHandler(ReleaseJson("99.0.0")));
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: true);
            Set(window, "_peekActive", false);
            typeof(MainWindow).GetMethod("RefreshMessages", Private)!.Invoke(window, null);
            var rows = (StackPanel)window.FindName("MessageRows");
            Assert.Single(rows.Children.Cast<FrameworkElement>());
            foreach (string name in new[] { "BadgeLogoList", "MediaBadgeLogos" })
            {
                var icon = Assert.IsType<Border>(((StackPanel)window.FindName(name)).Children[0]);
                Assert.Equal("\uE896", Assert.IsType<TextBlock>(icon.Child).Text);
            }
            Set(window, "_availableUpdate", null!);
            typeof(MainWindow).GetMethod("RefreshMessages", Private)!.Invoke(window, null);
            Assert.Empty(rows.Children.Cast<FrameworkElement>());
            Assert.Empty(((StackPanel)window.FindName("BadgeLogoList")).Children.Cast<UIElement>());
        });

    [Fact]
    public void Automatic_check_does_not_contact_GitHub_when_disabled()
        => WithWindow(async window =>
        {
            Field<AppSettings>(window, "_settings").CheckForUpdatesAutomatically = false;
            var handler = new ResponseHandler(ReleaseJson("99.0.0"));
            using var http = new HttpClient(handler);
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: false);
            Assert.Equal(0, handler.Requests);
            Assert.Equal(Visibility.Collapsed, Item(window, "MenuInstallUpdate").Visibility);
            Assert.Equal(Visibility.Collapsed, ((Border)((StackPanel)Item(window, "MenuUpdates").Header).Children[1]).Visibility);
        });

    [Fact]
    public void Current_release_shows_up_to_date_without_install_option()
        => WithWindow(async window =>
        {
            using var http = new HttpClient(new ResponseHandler(ReleaseJson("1.0.0")));
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: true);
            Assert.Contains("up to date", Item(window, "MenuUpdateStatus").Header.ToString());
            Assert.Equal(Visibility.Collapsed, Item(window, "MenuInstallUpdate").Visibility);
        });

    [Fact]
    public void Failed_check_keeps_known_update_and_reenables_retry()
        => WithWindow(async window =>
        {
            var known = new UpdateRelease(new Version(99, 0, 0), "v99.0.0",
                new Uri("https://github.com/ademerci/Holm/releases/tag/v99.0.0"),
                new Uri("https://github.com/ademerci/Holm/releases/download/v99.0.0/Holm-99.0.0-win-x64.zip"), 100, new string('a', 64));
            Set(window, "_availableUpdate", known);
            using var http = new HttpClient(new ResponseHandler("", HttpStatusCode.ServiceUnavailable));
            Set(window, "_updates", new UpdateService(http));
            await Check(window, manual: true);
            Assert.Same(known, Field<UpdateRelease>(window, "_availableUpdate"));
            Assert.Contains("Couldn't check", Item(window, "MenuUpdateStatus").Header.ToString());
            Assert.True(Item(window, "MenuCheckUpdates").IsEnabled);
        });

    [Fact]
    public void Checking_does_not_start_while_an_update_is_being_installed()
        => WithWindow(async window =>
        {
            var handler = new ResponseHandler(ReleaseJson("99.0.0"));
            using var http = new HttpClient(handler);
            Set(window, "_updates", new UpdateService(http));
            Set(window, "_installingUpdate", true);
            await Check(window, manual: true);
            Assert.Equal(0, handler.Requests);
        });

    [Fact]
    public void Settings_changes_wait_for_apply_and_reset_is_only_a_draft()
        => WithWindow(window =>
        {
            var item = new MenuItem { Name = "MenuAnim", Header = "Animations", IsChecked = true };
            bool applied = true;
            int clicks = 0;
            item.Click += (_, _) => { applied = item.IsChecked; clicks++; };
            var settings = new SettingsWindow(new[] { new SettingsGroup("General", new[] { item }) }, "test", true,
                () => item.IsChecked = applied);
            var apply = (Button)typeof(SettingsWindow).GetField("_apply", Private)!.GetValue(settings)!;
            Assert.False(apply.IsEnabled);
            var rows = (System.Collections.IEnumerable)typeof(SettingsWindow).GetField("_rows", Private)!.GetValue(settings)!;
            var entry = rows.Cast<object>().Single();
            var toggle = (ToggleSwitch)entry.GetType().GetField("Item2")!.GetValue(entry)!;
            typeof(SettingsWindow).GetMethod("Flip", Private)!.Invoke(settings, new object[] { item, toggle });
            Assert.False(toggle.IsOn);
            Assert.True(apply.IsEnabled);
            Assert.True(applied);
            Assert.Equal(0, clicks);
            Assert.False((bool)typeof(SettingsWindow).GetField("_animate", Private)!.GetValue(settings)!);
            typeof(SettingsWindow).GetMethod("ApplyChanges", Private)!.Invoke(settings, null);
            Assert.False(applied);
            Assert.False(apply.IsEnabled);
            Assert.Equal(1, clicks);
            typeof(SettingsWindow).GetMethod("ResetDefaults", Private)!.Invoke(settings, null);
            Assert.True(toggle.IsOn);
            Assert.True(apply.IsEnabled);
            var choices = new (string, Action)[] { ("Cancel", () => { }) };
            typeof(SettingsWindow).GetMethod("ShowDialog", Private)!.Invoke(settings, new object[] { "Reset settings?", "Confirmation", choices });
            var dialog = (Border)typeof(SettingsWindow).GetField("_dialog", Private)!.GetValue(settings)!;
            var body = (DockPanel)typeof(SettingsWindow).GetField("_body", Private)!.GetValue(settings)!;
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.False(body.IsEnabled);
            Assert.IsType<System.Windows.Media.Effects.BlurEffect>(body.Effect);
            typeof(SettingsWindow).GetMethod("HideDialog", Private)!.Invoke(settings, null);
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
            Assert.True(body.IsEnabled);
            Assert.Null(body.Effect);
            Assert.False(applied);
            typeof(SettingsWindow).GetMethod("ApplyChanges", Private)!.Invoke(settings, null);
            Assert.True(applied);
            settings.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public void Disabling_animations_snaps_open_and_closed_shapes_and_opacity()
        => WithWindow(window =>
        {
            Field<AppSettings>(window, "_settings").Animations = false;
            var apply = typeof(MainWindow).GetMethod("ApplyState", Private)!;
            var island = (Border)window.FindName("Island");
            var expanded = (System.Windows.Controls.Grid)window.FindName("ExpandedView");
            foreach (bool open in new[] { true, false })
            {
                Set(window, "_expanded", open);
                apply.Invoke(window, new object[] { true });
                Assert.False(Field<bool>(window, "_shapeAnimating"));
                Assert.False(island.HasAnimatedProperties);
                Assert.False(expanded.HasAnimatedProperties);
                Assert.Equal(open ? 1.0 : 0.0, expanded.Opacity);
            }
            return Task.CompletedTask;
        });

    static string ReleaseJson(string version) => JsonSerializer.Serialize(new
    {
        tag_name = "v" + version, draft = false, prerelease = false,
        assets = new[] { new { name = $"Holm-{version}-win-x64.zip", state = "uploaded", size = 100,
            digest = "sha256:" + new string('a', 64),
            browser_download_url = $"https://github.com/ademerci/Holm/releases/download/v{version}/Holm-{version}-win-x64.zip" } }
    });

    sealed class ResponseHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    static MenuItem Item(MainWindow window, string name) => (MenuItem)window.FindName(name);
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    static Task Check(MainWindow window, bool manual) => (Task)typeof(MainWindow).GetMethod("CheckForUpdatesAsync", Private)!.Invoke(window, new object[] { manual })!;

    static void WithWindow(Func<MainWindow, Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                MainWindow? window = null;
                try { window = new MainWindow(); await test(window); }
                catch (Exception ex) { failure = ex; }
                finally { window?.Close(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The hidden update menu check did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
