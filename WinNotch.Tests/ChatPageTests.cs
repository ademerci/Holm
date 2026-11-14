using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>
/// The chat page stays quick however busy the conversation: changes in a row are caught up with once, after input, and
/// bubbles that didn't change are the ones already built.
/// </summary>
public class ChatPageTests
{
    [Fact]
    public void Changes_in_a_row_are_caught_up_with_once_and_unchanged_bubbles_are_kept()
        => OnUiThread(async () =>
        {
            var chats = new ChatStore(null);
            var friend = new Friend { Name = "Ana", Key = "ana-key", Topic = "t" };
            var page = new ChatPage(new SocialContext
            {
                Friends = () => new[] { friend }, Chats = chats, Face = _ => ("A", Brushes.Gray), IsOnline = _ => true,
                Send = (_, _) => { }, Retry = (_, _) => { },
            }, friend);
            var window = new Window { Content = page };
            var messages = (StackPanel)typeof(ChatPage).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;

            chats.Add(friend.Key, new FriendMessage { Id = "1", Text = "hello", Sent = 100 }, unread: false);
            await Settle();
            var first = messages.Children[^1];

            // Sending: added, then sent, then a reply. Nothing is rebuilt on the spot.
            int before = messages.Children.Count;
            chats.Add(friend.Key, Message("2", "how are you", 5000, ChatState.Sending), unread: false);
            chats.SetState(friend.Key, "2", ChatState.Sent);
            chats.Add(friend.Key, new FriendMessage { Id = "3", Text = "good", Sent = 9000, Mine = false }, unread: true);
            Assert.Equal(before, messages.Children.Count);

            await Settle();
            Assert.True(messages.Children.Count >= before + 2);
            Assert.Contains(first, messages.Children.Cast<UIElement>()); // "hello" is the same bubble, not built again
            window.Close();
        });

    [Theory]
    [InlineData("hello there", false)]
    [InlineData("Größe: 12 €", false)]
    [InlineData("nice 😂", true)]
    [InlineData("❤", true)]
    [InlineData("1️⃣", true)]
    public void Only_text_with_emoji_needs_the_color_emoji_text_block(string text, bool emoji)
        => Assert.Equal(emoji, ChatPage.HasEmoji(text));

    [Fact]
    public void A_bubble_looks_different_when_its_state_reaction_or_place_changes()
    {
        var message = Message("1", "hi", 1, ChatState.Sending);
        string sending = ChatPage.BubbleLook(message, false, false, true);
        Assert.Equal(sending, ChatPage.BubbleLook(message, false, false, true));
        message.State = ChatState.Sent;
        Assert.NotEqual(sending, ChatPage.BubbleLook(message, false, false, true));
        string sent = ChatPage.BubbleLook(message, false, false, true);
        Assert.NotEqual(sent, ChatPage.BubbleLook(message, false, false, false)); // no longer the last one
        message.MyReaction = "❤️";
        Assert.NotEqual(sent, ChatPage.BubbleLook(message, false, false, true));
    }

    static FriendMessage Message(string id, string text, long sent, ChatState state)
        => new() { Id = id, Text = text, Sent = sent, Mine = true, State = state };

    /// <summary>Lets the dispatcher run everything waiting, background work included.</summary>
    static async Task Settle()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    static void OnUiThread(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { await test(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The chat page test did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
