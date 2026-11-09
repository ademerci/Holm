using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Your profile (picture, name, status, color), edited at the top of the settings window and sent to your friends;
/// and theirs, shown wherever the notch used to show their initials.
/// </summary>
public partial class MainWindow
{
    Friend? FriendNamed(string name) => (_demoFriends ?? _friends.Friends).FirstOrDefault(f => f.Name == name);

    /// <summary>What goes in someone's circle: nothing over their picture, else their initials.</summary>
    string FaceText(string name) => ProfilePicture.BrushOf(FriendNamed(name)?.Picture) != null ? "" : Initials(name);

    /// <summary>A friend's picture, else the color they chose, else the color their name gives.</summary>
    Brush FaceBrush(string name)
    {
        var friend = FriendNamed(name);
        if (ProfilePicture.BrushOf(friend?.Picture) is { } picture) return picture;
        if (FriendProfile.CleanColor(friend?.Color) is { } color) return Frozen((Color)ColorConverter.ConvertFromString(color));
        return AvatarColor(name);
    }

    ProfileDraft CurrentProfile() => new(_settings.ProfileName, _settings.ProfileStatus, _settings.ProfileColor, _settings.ProfilePicture);

    void ApplyProfile(ProfileDraft profile)
    {
        string oldName = _friends.MyName;
        _settings.ProfileName = FriendProfile.CleanText(profile.Name, FriendProfile.MaxName);
        _settings.ProfileStatus = FriendProfile.CleanText(profile.Status, FriendProfile.MaxStatus);
        _settings.ProfileColor = FriendProfile.CleanColor(profile.Color);
        _settings.ProfilePicture = profile.Picture;
        _settings.Save();
        if (profile.Picture != null) _achievements.Set(AchievementBook.ProfilePicture, 1);
        _friends.ProfileChanged();
        if (_friends.MyName != oldName && _nearby != null)
        {
            // People nearby see the name the network service started with: start it again with the new one.
            _nearby.Dispose();
            _nearby = null;
            StartSharing();
        }
        Diag.Log("Profile saved");
    }

    /// <summary>Your face, name and status for the top of the Holm window's sidebar.</summary>
    FrameworkElement MeCard(ProfileDraft profile)
    {
        string name = FriendProfile.CleanText(profile.Name, FriendProfile.MaxName) ?? Native.DisplayName();
        var picture = ProfilePicture.BrushOf(profile.Picture);
        Brush fill = picture ?? (FriendProfile.CleanColor(profile.Color) is { } color
            ? Frozen((Color)ColorConverter.ConvertFromString(color)) : AvatarColor(name));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(HubTheme.Face(picture != null ? "" : Initials(name), fill, 36));
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new TextBlock
        {
            Text = FriendProfile.CleanText(profile.Status, FriendProfile.MaxStatus) ?? "Set a status", FontSize = 11,
            Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }

    /// <summary>Friends › each friend with their picture and status, and the way to your own profile.</summary>
    void AddFriendProfiles()
    {
        foreach (var friend in _friends.Friends.Take(12).ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Avatar(FaceText(friend.Name), FaceBrush(friend.Name)));
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            words.Children.Add(new TextBlock { Text = friend.Name });
            if (friend.Status != null)
                words.Children.Add(new TextBlock { Text = friend.Status, FontSize = 11, Opacity = 0.6, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
            if (_friends.IsOnline(friend))
                words.Children.Add(new TextBlock { Text = "Online", FontSize = 11, Foreground = GreenBrush });
            row.Children.Add(words);
            // Shown, not chosen: clicking one keeps the menu open.
            MenuFriends.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true });
        }
    }
}
