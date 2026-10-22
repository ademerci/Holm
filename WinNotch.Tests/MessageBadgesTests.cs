using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using WinNotch.Notify;

namespace WinNotch.Tests;

public class MessageBadgesTests
{
    const string TeamsId = "MSTeams_8wekyb3d8bbwe!MSTeams";
    const string WhatsAppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";

    [Theory]
    [InlineData(TeamsId, "teams")]
    [InlineData(WhatsAppId, "whatsapp")]
    [InlineData("com.squirrel.Discord.Discord", "discord")]
    public void Existing_numeric_badges_are_visible_without_a_popup(string appId, string app)
    {
        WithService(service =>
        {
            ChatMessage? popup = null;
            service.Changed += message => popup = message;
            Read(service, new() { [appId] = "3" });

            var message = Assert.Single(service.Messages);
            Assert.Equal(app, message.App);
            Assert.True(message.CountOnly);
            Assert.Equal(3, service.UnreadCount);
            Assert.Null(popup);
        });
    }

    [Fact]
    public void A_WhatsApp_badge_increase_generates_a_popup_without_any_toast()
    {
        WithService(service =>
        {
            Read(service, new() { [WhatsAppId] = "0" });
            ChatMessage? popup = null;
            service.Changed += message => popup = message;
            Read(service, new() { [WhatsAppId] = "1" });
            Invoke(service, "Publish", null, true); // the short wait for a richer toast elapsed

            Assert.NotNull(popup);
            Assert.Equal("whatsapp", popup.App);
            Assert.Equal("WhatsApp", popup.Title);
            Assert.Equal(1, service.UnreadCount);
        });
    }

    [Fact]
    public void Reading_one_app_preserves_the_other_apps_unread_count()
    {
        WithService(service =>
        {
            Read(service, new() { [TeamsId] = "2", [WhatsAppId] = "4" });
            service.MarkRead("whatsapp");
            Assert.Equal("teams", Assert.Single(service.Messages).App);
            Assert.Equal(2, service.UnreadCount);

            Read(service, new() { [TeamsId] = "2", [WhatsAppId] = "5" });
            Assert.Equal(3, service.UnreadCount);
            Assert.Equal(1, service.Messages.Single(m => m.App == "whatsapp").Count);
        });
    }

    [Fact]
    public void Removing_a_badge_clears_its_previous_unread_count()
    {
        WithService(service =>
        {
            Read(service, new() { [WhatsAppId] = "3" });
            Read(service, new());
            Assert.Empty(service.Messages);
        });
    }

    [Fact]
    public void A_toast_and_its_badge_do_not_count_the_same_message_twice()
    {
        WithService(service =>
        {
            var toast = new ChatMessage { Id = 8, App = "whatsapp", Title = "Test sender", Time = DateTimeOffset.Now };
            Field<List<ChatMessage>>(service, "_toasts").Add(toast);
            Read(service, new() { [WhatsAppId] = "1" });
            Assert.Same(toast, Assert.Single(service.Messages));
            Assert.Equal(1, service.UnreadCount);
        });
    }

    [Fact]
    public void An_unchanged_zero_badge_does_not_hide_a_new_toast()
    {
        WithService(service =>
        {
            Read(service, new() { [TeamsId] = "0" });
            var toast = new ChatMessage { Id = 9, App = "teams", Title = "Test sender", Time = DateTimeOffset.Now };
            Field<List<ChatMessage>>(service, "_toasts").Add(toast);
            Read(service, new() { [TeamsId] = "0" });

            Assert.Same(toast, Assert.Single(service.Messages));
            Assert.Equal(1, service.UnreadCount);
        });
    }

    [Fact]
    public void Switching_a_messenger_off_hides_its_badge()
    {
        bool showWhatsApp = true;
        WithService(service =>
        {
            Read(service, new() { [WhatsAppId] = "3" });
            showWhatsApp = false;
            Invoke(service, "Publish", null, false);
            Assert.Empty(service.Messages);
        }, key => key != "whatsapp" || showWhatsApp);
    }

    static void Read(MessagesService service, Dictionary<string, string> badges)
    {
        Invoke(service, "ReadBadges", badges, null);
        Invoke(service, "Publish", null, false);
    }

    static void WithService(Action<MessagesService> test, Func<string, bool>? shown = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var service = new MessagesService(Dispatcher.CurrentDispatcher, shown ?? (_ => true));
            // No shell icon lookup or real notification service is needed for badge processing.
            var ids = Field<Dictionary<string, string>>(service, "_appIds");
            foreach (var app in Messengers.All) ids[app.Key] = app.Key;
            try { test(service); }
            catch (Exception ex) { failure = ex; }
            finally { service.Stop(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The badge processing test did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(MessagesService service, string name) =>
        (T)typeof(MessagesService).GetField(name, Private)!.GetValue(service)!;
    static void Invoke(MessagesService service, string method, params object?[] args) =>
        typeof(MessagesService).GetMethod(method, Private)!.Invoke(service, args);
}
