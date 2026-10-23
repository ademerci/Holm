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
