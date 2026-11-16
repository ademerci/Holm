using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// What the friends pages of the Holm window may know and do: the notch's friends, chats and invites, through
/// functions, so the pages hold no state of their own (the WPF app's SocialContext, with Avalonia's brushes).
/// </summary>
internal sealed class SocialContext
{
    public required Func<IReadOnlyList<Friend>> Friends { get; init; }
    public required ChatStore Chats { get; init; }
    /// <summary>A friend's initials (empty over a picture) and fill, by name.</summary>
    public required Func<string, (string Text, IBrush Fill)> Face { get; init; }
    public required Func<Friend, bool> IsOnline { get; init; }
    /// <summary>Sends a chat text (split into several messages if long); the store shows how it goes.</summary>
    public required Action<Friend, string> Send { get; init; }
    /// <summary>Sends a message that didn't go again.</summary>
    public required Action<Friend, FriendMessage> Retry { get; init; }
    /// <summary>Opens another page of the Holm window, with a friend chosen (the chat with them, say).</summary>
    public Action<string, Friend?>? Open { get; init; }
    /// <summary>What a friend is doing, if they're online and share it.</summary>
    public Func<Friend, FriendActivity?> ActivityOf { get; init; } = _ => null;
    /// <summary>What we're doing, as friends see it (null: nothing, or sharing is off).</summary>
    public Func<FriendActivity?> Mine { get; init; } = () => null;
    /// <summary>Whether we share our activity at all.</summary>
    public Func<bool> Sharing { get; init; } = () => false;
    /// <summary>Asks friends whether their Holm runs; the answers bring what they're doing.</summary>
    public Action Ping { get; init; } = () => { };
    public Action<Friend> Wave { get; init; } = _ => { };
    /// <summary>Invites a friend to watch our screen (null: streaming isn't offered).</summary>
    public Action<Friend>? Stream { get; init; }
    /// <summary>Sends selected files to a friend using Beam.</summary>
    public Action<Friend, string[]> SendFiles { get; init; } = (_, _) => { };
    /// <summary>Reacts to a message with an emoji, or takes our reaction away (null).</summary>
    public Action<Friend, FriendMessage, string?> React { get; init; } = (_, _, _) => { };
    /// <summary>The quick reactions in the reaction bar, in order.</summary>
    public Func<IReadOnlyList<string>> QuickReactions { get; init; } = () => ChatReaction.Defaults;
    /// <summary>How many achievements a friend has, if their notch said.</summary>
    public Func<Friend, int?> TrophiesOf { get; init; } = _ => null;
    /// <summary>Our invite code (a new one with true), or why there's none.</summary>
    public Func<bool, Task<(OwnInvite? Invite, string? Error)>> Invite { get; init; } = _ => Task.FromResult<(OwnInvite?, string?)>((null, "Not available here"));
    /// <summary>Adds the friend behind a code someone typed or pasted (an invite code, or an old long friend code).</summary>
    public Func<string, Task<(bool Ok, string Message)>> AddByCode { get; init; } = _ => Task.FromResult((false, "Not available here"));
    /// <summary>Friend requests waiting for our answer.</summary>
    public Func<IReadOnlyList<(Friend Card, string RequestId)>> Incoming { get; init; } = () => Array.Empty<(Friend, string)>();
    /// <summary>Our requests waiting for theirs.</summary>
    public Func<IReadOnlyList<PendingFriendRequest>> Pending { get; init; } = () => Array.Empty<PendingFriendRequest>();
    public Action<Friend, string, bool> Answer { get; init; } = (_, _, _) => { };
    public Action<PendingFriendRequest> Withdraw { get; init; } = _ => { };
    /// <summary>Our long friend code, for friends on an older Holm.</summary>
    public Func<string?> LongCode { get; init; } = () => null;
    /// <summary>Copies a text; false when the clipboard is busy.</summary>
    public Func<string, Task<bool>> Copy { get; init; } = _ => Task.FromResult(false);

    /// <summary>A friend came online, went quiet, changed their profile or what they're doing.</summary>
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
