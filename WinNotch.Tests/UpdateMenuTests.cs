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
            var surface = (Border)typeof(SettingsWindow).GetField("_surface", Private)!.GetValue(settings)!;
            Assert.IsType<System.Windows.Media.Effects.BlurEffect>(surface.Effect);
            Assert.Same(body, surface.Child);
            Assert.Null(dialog.Effect);
            typeof(SettingsWindow).GetMethod("HideDialog", Private)!.Invoke(settings, null);
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
            Assert.True(body.IsEnabled);
            Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(surface.Effect);
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

    [Fact]
    public void Weather_city_setup_is_visible_with_media_and_failed_forecasts_allow_recovery()
        => WithWindow(window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.Weather = true;
            settings.MediaControls = true;
            settings.WeatherLatitude = 47;
            settings.WeatherLongitude = 8;
            Set(window, "_info", new MediaInfo { Title = "Song", App = "Spotify", IsPlaying = true });
            typeof(MainWindow).GetMethod("RefreshMediaUi", Private)!.Invoke(window, null);
            typeof(MainWindow).GetMethod("ChoosePlace", Private)!.Invoke(window, null);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("HomePanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ClockPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WeatherSetup")).Visibility);
            Set(window, "_choosingPlace", false);
            typeof(MainWindow).GetMethod("RefreshMediaUi", Private)!.Invoke(window, null);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("MediaPanel")).Visibility);
            typeof(MainWindow).GetMethod("ShowWeather", Private)!.Invoke(window, null);
            var add = (TextBlock)window.FindName("WeatherAdd");
            Assert.Equal(Visibility.Visible, add.Visibility);
            Assert.Contains("unavailable", add.Text);
            return Task.CompletedTask;
        });

    [Fact]
    public void A_new_call_in_the_same_app_uses_the_new_start_time()
        => WithWindow(window =>
        {
            Field<AppSettings>(window, "_settings").Calls = true;
            var since = DateTime.UtcNow.AddMinutes(-1);
            Set(window, "_call", new SensorUse(Sensor.Microphone, "Discord", "Discord", since.AddHours(-19)));
            Set(window, "_demoSensors", new[] { new SensorUse(Sensor.Microphone, "Discord", "Discord", since) });
            typeof(MainWindow).GetMethod("ShowCall", Private)!.Invoke(window, null);
            Assert.Equal(since, Field<SensorUse>(window, "_call").Since);
            return Task.CompletedTask;
        });

    [Fact]
    public void Disabled_media_controls_hide_playback_and_can_be_enabled_without_waiting_for_a_track_change()
        => WithWindow(window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            Assert.True(new AppSettings().MediaControls);
            settings.MediaControls = false;
            settings.Animations = false;
            Set(window, "_expanded", true);
            var track = new MediaInfo { Title = "Song", Artist = "Artist", App = "Spotify", IsPlaying = true };
            typeof(MainWindow).GetMethod("OnMediaChanged", Private)!.Invoke(window, new object[] { track, true });
            Assert.Null(typeof(MainWindow).GetField("_info", Private)!.GetValue(window));
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("MediaPanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ClockPanel")).Visibility);
            bool readOnly = AppSettings.ReadOnly;
            AppSettings.ReadOnly = true;
            try
            {
                var menu = Item(window, "MenuMediaControls");
                foreach (bool enabled in new[] { true, false })
                {
                    menu.IsChecked = enabled;
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menu));
                    Assert.Equal(enabled, settings.MediaControls);
                    Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)window.FindName("MediaPanel")).Visibility);
                    Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)window.FindName("SleepBtn")).Visibility);
                }
            }
            finally { AppSettings.ReadOnly = readOnly; }
            return Task.CompletedTask;
        });

    [Fact]
    public void Weather_location_is_found_once_and_saved_locations_are_preserved()
        => WithWindow(async window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.Weather = settings.WeatherAutoLocation = true;
            settings.WeatherLatitude = settings.WeatherLongitude = null;
            int lookups = 0;
            Set(window, "_locateWeather", new Func<CancellationToken, Task<WeatherPlace?>>(_ =>
            {
                lookups++;
                return Task.FromResult<WeatherPlace?>(WeatherLocation.Approximate(47.3769, 8.5417));
            }));
            Set(window, "_fetchWeather", new Func<double, double, CancellationToken, Task<WeatherNow?>>((lat, lon, _) =>
            {
                Assert.Equal(47.4, lat);
                Assert.Equal(8.5, lon);
                return Task.FromResult<WeatherNow?>(new WeatherNow(15, 0, true, null, 0));
            }));
            bool readOnly = AppSettings.ReadOnly;
            AppSettings.ReadOnly = true;
            try
            {
                var locate = typeof(MainWindow).GetMethod("LocateWeatherAsync", Private)!;
                await (Task)locate.Invoke(window, null)!;
                await (Task)locate.Invoke(window, null)!;
                Assert.Equal(1, lookups);
                Assert.Equal(47.4, settings.WeatherLatitude);
                settings.WeatherPlace = "Saved city";
                Set(window, "_locationAttempted", false);
                await (Task)locate.Invoke(window, null)!;
                Assert.Equal("Saved city", settings.WeatherPlace);
                Assert.Equal(1, lookups);
            }
            finally { AppSettings.ReadOnly = readOnly; }
        });

    [Fact]
    public void Disabled_or_unavailable_location_does_not_retry_or_block_manual_city_selection()
        => WithWindow(async window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.WeatherLatitude = settings.WeatherLongitude = null;
            int lookups = 0;
            Set(window, "_locateWeather", new Func<CancellationToken, Task<WeatherPlace?>>(_ =>
            {
                lookups++;
                throw new UnauthorizedAccessException();
            }));
            var locate = typeof(MainWindow).GetMethod("LocateWeatherAsync", Private)!;
            settings.Weather = false;
            settings.WeatherAutoLocation = true;
            await (Task)locate.Invoke(window, null)!;
            settings.Weather = true;
            settings.WeatherAutoLocation = false;
            await (Task)locate.Invoke(window, null)!;
            Assert.Equal(0, lookups);
            settings.WeatherAutoLocation = true;
            await (Task)locate.Invoke(window, null)!;
            await (Task)locate.Invoke(window, null)!;
            Assert.Equal(1, lookups);
            Assert.Null(settings.WeatherLatitude);
            typeof(MainWindow).GetMethod("ChoosePlace", Private)!.Invoke(window, null);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WeatherSetup")).Visibility);
        });

    [Fact]
    public void Choosing_a_city_cancels_pending_automatic_location()
        => WithWindow(async window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.Weather = settings.WeatherAutoLocation = true;
            settings.WeatherLatitude = settings.WeatherLongitude = null;
            var pending = new TaskCompletionSource<WeatherPlace?>();
            Set(window, "_locateWeather", new Func<CancellationToken, Task<WeatherPlace?>>(_ => pending.Task));
            var locating = (Task)typeof(MainWindow).GetMethod("LocateWeatherAsync", Private)!.Invoke(window, null)!;
            typeof(MainWindow).GetMethod("ChoosePlace", Private)!.Invoke(window, null);
            pending.SetResult(WeatherLocation.Approximate(47.3, 8.5));
            await locating;
            Assert.Null(settings.WeatherLatitude);
            Assert.True(Field<bool>(window, "_choosingPlace"));
        });

    [Fact]
    public void Media_header_keeps_a_small_clock_and_date_clear_of_the_toolbar()
        => WithWindow(window =>
        {
            var settings = Field<AppSettings>(window, "_settings");
            settings.MediaControls = true;
            settings.Animations = false;
            Set(window, "_expanded", true);
            typeof(MainWindow).GetMethod("OnMediaChanged", Private)!.Invoke(window,
                new object[] { new MediaInfo { Title = "Song", App = "Spotify", IsPlaying = true }, true });
            var view = (FrameworkElement)window.FindName("ExpandedView");
            view.Measure(new Size(480, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, 480, view.DesiredSize.Height));
            var clock = (FrameworkElement)window.FindName("MediaClockHeader");
            var toolbar = (FrameworkElement)window.FindName("ToolBar");
            Assert.Equal(Visibility.Visible, clock.Visibility);
            Assert.Equal(((TextBlock)window.FindName("ClockBig")).Text, ((TextBlock)window.FindName("MediaClock")).Text);
            Assert.NotEmpty(((TextBlock)window.FindName("MediaDate")).Text);
            Assert.True(clock.TranslatePoint(new Point(clock.ActualWidth, 0), view).X <= toolbar.TranslatePoint(new Point(), view).X);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("OutputBtn", "_outputMenu", "Output_Click")]
    [InlineData("BluetoothBtn", "_bluetoothMenu", "Bluetooth_Click")]
    public void Clicking_a_menu_button_after_popup_mouse_down_dismissal_does_not_reopen_it(string buttonName, string menuField, string handler)
        => WithWindow(window =>
        {
            var button = (Button)window.FindName(buttonName);
            var closed = new ContextMenu { PlacementTarget = button };
            Set(window, menuField, closed);
            // WPF has dismissed the popup on mouse-down; Click follows on mouse-up.
            Set(window, "_dismissedMenuButton", button);
            typeof(MainWindow).GetMethod(handler, Private)!.Invoke(window, new object[] { button, new RoutedEventArgs() });
            Assert.Same(closed, typeof(MainWindow).GetField(menuField, Private)!.GetValue(window));
            Assert.False(closed.IsOpen);
            Assert.Null(typeof(MainWindow).GetField("_dismissedMenuButton", Private)!.GetValue(window));
            Assert.False((bool)typeof(MainWindow).GetMethod("CloseButtonMenu", Private)!.Invoke(window, new object[] { button, closed })!);
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
