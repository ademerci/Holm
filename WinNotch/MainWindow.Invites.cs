using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Sharing;

namespace WinNotch;

// Adding friends with invite codes: the code you give out, the code you type in, and what the notch says when someone
// joins with yours or answers a request you sent a while ago.
public partial class MainWindow
{
    string? _lastOfferedInvite;

    void InitInvites()
    {
        _friends.InviteAccepted += card =>
        {
            FriendsChanged();
            ShowPeek("", $"{card.Name} is now your friend", "They used your invite code", null, false, null, seconds: 5);
        };
        _friends.LateAnswer += (card, yes) =>
        {
            FriendsChanged();
            if (yes) ShowPeek("", $"You and {card.Name} are now friends", "They accepted your request", null, false, null, seconds: 5);
            else ShowPeek("", $"{card.Name} declined your request", "", null, false, RedBrush, seconds: 4);
        };
        _friends.InvitesChanged += () => _social?.Raise();
        _friends.FriendRequested += (_, _) => _social?.Raise();
        _friends.FriendRequestCancelled += (_, _) => _social?.Raise();
    }

    /// <summary>The friends list changed: the menu, the share targets and the Holm window follow.</summary>
    void FriendsChanged()
    {
        FillFriendsMenu();
        if (_sharing) { RefreshShareTargets(); ApplyState(); }
        _social?.Raise();
    }

    async Task<(OwnInvite? Invite, string? Error)> GetInviteAsync(bool fresh)
    {
        if (!_settings.Beam) return (null, "Turn on Beam in Settings to add friends");
        try { return (await _friends.InviteAsync(fresh, CancellationToken.None), null); }
        catch (IOException ex) { return (null, ex.Message); }
    }

    /// <summary>Adds the friend behind a typed or pasted code: an invite code, or an old long friend code.</summary>
    async Task<(bool Ok, string Message)> AddFriendByCodeAsync(string text)
    {
        if (!_settings.Beam) return (false, "Turn on Beam in Settings to add friends.");
        try
        {
            if (FriendInvite.Find(text) is { } code)
            {
                var (result, card) = await _friends.UseInviteAsync(code, TimeSpan.FromSeconds(25), CancellationToken.None);
                FriendsChanged();
                return result switch
                {
                    InviteResult.Added => (true, $"You and {card!.Name} are now friends."),
                    InviteResult.Sent => (true, $"Sent to {card!.Name}. You'll be friends as soon as their Holm is on."),
                    InviteResult.AlreadyFriends => (true, $"{card!.Name} is already your friend."),
                    InviteResult.OwnCode => (false, "That's your own code: send it to a friend."),
                    _ => (false, "That code doesn't work. It may have run out: ask your friend for a new one."),
                };
            }
            if (FriendIdentity.ParseFriendCode(text) is { } longCard)
            {
                if (_friends.IsMe(longCard)) return (false, "That's your own friend code.");
                if (_friends.Find(longCard.Key) != null) return (true, $"{longCard.Name} is already your friend.");
                var answer = await _friends.RequestAddAsync(longCard, TimeSpan.FromSeconds(25), CancellationToken.None);
                FriendsChanged();
                return answer switch
                {
                    FriendRequestResult.Accepted => (true, $"You and {longCard.Name} are now friends."),
                    FriendRequestResult.Declined => (false, $"{longCard.Name} declined."),
                    _ => (true, $"Request sent to {longCard.Name}. You'll be friends once they accept it in their notch."),
                };
            }
        }
        catch (IOException ex) { return (false, ex.Message + ". Check your connection and try again."); }
        return (false, "That isn't a Holm code. Codes look like HOLM-7KQ2X-9XF4M.");
    }

    async Task AnswerFromHubAsync(Friend card, string requestId, bool accept)
    {
        await AnswerFriendRequestAsync(card, requestId, accept);
        FriendsChanged();
    }

    async Task WithdrawAsync(PendingFriendRequest pending)
    {
        await _friends.CancelRequestAsync(pending);
        _social?.Raise();
    }

    /// <summary>An invite code was copied (from a chat, say): offer to add the friend behind it.</summary>
    bool OfferCopiedInvite(string? text)
    {
        if (FriendInvite.FindInMessage(text) is not { } code) return false;
        if (_friends.Invites.Any(i => i.Code == code) || code == _lastOfferedInvite) return true; // ours, or asked already
        _lastOfferedInvite = code;
        ShowTask("", ShareBlue, "Holm invite code copied", "Add the friend who sent it?", null,
                 ("Add", () => _ = AddCopiedInviteAsync(code)), ("Not now", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(20);
        _taskHide.Start();
        return true;
    }

    async Task AddCopiedInviteAsync(string code)
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, "Adding your friend…", "Looking up the invite", null, null, ("Cancel", CancelTask));
        var (ok, message) = await AddFriendByCodeAsync(code);
        if (cts.IsCancellationRequested) return;
        if (ok) EndTask("", ShareBlue, message.Split(". ")[0].TrimEnd('.'), message.Contains(". ") ? message[(message.IndexOf(". ") + 2)..] : "");
        else EndTask("", RedBrush, "Couldn't add the friend", message);
    }
}
