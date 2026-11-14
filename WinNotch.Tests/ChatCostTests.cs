using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch;
using WinNotch.Sharing;
using Xunit.Abstractions;

namespace WinNotch.Tests;

/// <summary>
/// What the chat costs on the UI thread with a long conversation in a transparent window like the Holm window: opening
/// it, and sending one message (added, sent, then the friend's answer). Prints the numbers; run on a desktop.
/// On 1.11.1 (this PC): opening took 23.6 s and a message 11 s (worst 20 s); on 1.12: 1.5 s and 0.13 s.
/// </summary>
[Trait("Category", "Local")]
public class ChatCostTests(ITestOutputHelper output)
{
    [Fact]
    public void Opening_a_long_chat_and_sending_a_message_stay_quick()
    {
        string? report = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { report = await MeasureAsync(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(600)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        output.WriteLine(report);
    }

    static async Task<string> MeasureAsync()
    {
        var chats = new ChatStore(null);
        var friend = new Friend { Name = "Ana", Key = "ana-key", Topic = "t" };
        for (int i = 0; i < 300; i++)
            chats.Add(friend.Key, new FriendMessage { Id = i.ToString(), Mine = i % 3 == 0, Text = i % 7 == 0 ? "haha 😂 that is great" : $"message number {i} with a few words in it", Sent = 1_000_000 + i * 40 }, unread: false);
        var social = new SocialContext
        {
            Friends = () => new[] { friend }, Chats = chats, Face = _ => ("A", Brushes.Gray), IsOnline = _ => true,
            Send = (_, _) => { }, Retry = (_, _) => { },
        };
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Width = 980, Height = 740,
            ShowActivated = false, Left = -2000, Top = 0,
        };
        window.Show();

        var warm = Stopwatch.StartNew();
        await Task.Run(Emoji.Wpf.EmojiData.Load); // as Holm does a little after start, when you have friends
        double warmMs = warm.Elapsed.TotalMilliseconds;
        var emoji = Stopwatch.StartNew();
        var probe = new Emoji.Wpf.TextBlock { Text = "hi 😀" };
        probe.Measure(new Size(200, 50));
        double emojiMs = emoji.Elapsed.TotalMilliseconds;
        var open = Stopwatch.StartNew();
        var page = new ChatPage(social, friend);
        double built = open.Elapsed.TotalMilliseconds;
        window.Content = new System.Windows.Controls.Border { Margin = new Thickness(16), Background = Brushes.Black, Child = page };
        await Idle();
        open.Stop();

        var times = new List<double>();
        for (int round = 0; round < 10; round++)
        {
            var send = Stopwatch.StartNew();
            string id = "new" + round;
            chats.Add(friend.Key, new FriendMessage { Id = id, Mine = true, Text = "on my way", Sent = 2_000_000 + round * 10, State = ChatState.Sending }, unread: false);
            chats.SetState(friend.Key, id, ChatState.Sent);
            chats.Add(friend.Key, new FriendMessage { Id = id + "r", Text = "great", Sent = 2_000_005 + round * 10 }, unread: true);
            chats.MarkRead(friend.Key);
            await Idle();
            times.Add(send.Elapsed.TotalMilliseconds);
        }
        window.Close();
        times.Sort();
        return $"open 300 messages: {open.Elapsed.TotalMilliseconds:F0} ms ({built:F0} ms building, first emoji text {emojiMs:F0} ms before, after {warmMs:F0} ms loading in the background); one message sent and answered: median {times[5]:F0} ms, worst {times[^1]:F0} ms";
    }

    static async Task Idle()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
}
