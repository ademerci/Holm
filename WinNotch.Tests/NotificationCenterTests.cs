using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch.Notify;

namespace WinNotch.Tests;

/// <summary>
/// Real notifications through the real notification center. Each test leaves a silent notification (no pop-up)
/// in an app's name, reads it back exactly the way the notch does, and removes it again. Runs only with
/// WINNOTCH_E2E_TOASTS=1: it touches this PC's notification center, so quit a running notch first or it shows it.
/// </summary>
[Trait("Category", "Manual")]
public class NotificationCenterTests
{
    static bool Enabled => Environment.GetEnvironmentVariable("WINNOTCH_E2E_TOASTS") == "1";

    static async Task<(ChatMessage? Message, string? AppId)> RoundTripAsync(string appId, params string[] lines)
    {
        Assert.Equal(UserNotificationListenerAccessStatus.Allowed, UserNotificationListener.Current.GetAccessStatus());
        string tag = "wn" + Guid.NewGuid().ToString("N")[..10];
        const string group = "winnotch-test";
        var xml = new XmlDocument();
        var texts = string.Concat(lines.Select(l => $"<text>{System.Security.SecurityElement.Escape(l.Replace("{tag}", tag))}</text>"));
        xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\">{texts}</binding></visual></toast>");
        ToastNotificationManager.CreateToastNotifier(appId).Show(new ToastNotification(xml) { Tag = tag, Group = group, SuppressPopup = true });
        try
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                foreach (var notification in await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast))
                {
                    var lineTexts = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (lineTexts == null || !lineTexts.Any(t => t.Text.Contains(tag))) continue;
                    var message = MessagesService.Read(notification, out string? id);
                    return (message, id);
                }
                await Task.Delay(200);
            }
            Assert.Fail("The test notification never showed up in the notification center.");
            return default;
        }
        finally { ToastNotificationManager.History.Remove(tag, group, appId); }
    }

    [Fact]
    public async Task Reads_a_Teams_message()
    {
        if (!Enabled) return;
        const string teams = "MSTeams_8wekyb3d8bbwe!MSTeams";
        var (message, appId) = await RoundTripAsync(teams, "Lena Hoffmann", "Test {tag}: are you coming to the 3 pm call?");
        Assert.NotNull(message);
        Assert.Equal("teams", message!.App);
        Assert.Equal("Lena Hoffmann", message.Title);
        Assert.StartsWith("Test wn", message.Text);
        Assert.Equal(teams, appId); // the notch opens Teams with it
    }

    [Fact]
    public async Task Ignores_a_notification_from_another_app()
    {
        if (!Enabled) return;
        // PowerShell's id is registered on every Windows, so any PC can post with it.
        const string powershell = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";
        var (message, appId) = await RoundTripAsync(powershell, "Backup finished", "Test {tag}");
        Assert.Null(message);
        Assert.Null(appId);
    }
}
