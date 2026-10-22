using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch;

// Beam and size: dropping files on friends, what arrives, and their menus.
public partial class MainWindow
{
    static readonly Brush[] AvatarBrushes =
    {
        Frozen(Color.FromRgb(0x0A, 0x84, 0xFF)), Frozen(Color.FromRgb(0x30, 0xB0, 0x50)), Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A)),
        Frozen(Color.FromRgb(0xFF, 0x37, 0x5F)), Frozen(Color.FromRgb(0xBF, 0x5A, 0xF2)), Frozen(Color.FromRgb(0x5E, 0x5C, 0xE6)),
        Frozen(Color.FromRgb(0x00, 0xA6, 0xB8)), Frozen(Color.FromRgb(0xAC, 0x8E, 0x68)),
    };

    static readonly (string Name, double Scale)[] Sizes = { ("Small", 0.85), ("Normal", 1), ("Large", 1.15), ("Extra large", 1.3) };

    BeamReceived? _lastBeam;

    // ───────────────────────── dragging files onto the notch ─────────────────────────

    static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    void Island_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e) || _hiddenForFullscreen) return;
        _dropLeave.Stop();
        if (!_dropActive) StartDrop(_beam.Friends);
    }

    void Island_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (!_dropActive) return;
        _dropLeave.Stop();
        _dropLeave.Start();
    }

    /// <summary>Only reached when no friend took the drag: there is nothing to drop on here.</summary>
    void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    void Island_Drop(object sender, DragEventArgs e) => EndDrop();

    /// <summary>Opens the notch into a row of friends to drop on.</summary>
    void StartDrop(IReadOnlyList<BeamFriend> friends)
    {
        DropTargets.Children.Clear();
        foreach (var friend in friends) DropTargets.Children.Add(FriendTarget(friend));
        DropTargets.Visibility = friends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DropTitle.Text = friends.Count > 0 ? "Beam to…" : "Add a friend to beam files to them:\nright-click › Beam › Add friend";
        _dropActive = true;
        _peekActive = false;
        _peekTimer.Stop();
        ApplyState();
    }

    void EndDrop()
    {
        _dropLeave.Stop();
        if (!_dropActive) return;
        _dropActive = false;
        ApplyState();
    }

    /// <summary>A friend's round avatar that takes the drop, and grows while files hover over it.</summary>
    FrameworkElement FriendTarget(BeamFriend friend)
    {
        var grow = new ScaleTransform(1, 1);
        var circle = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
            Background = AvatarBrush(friend.Name), BorderBrush = Brushes.White,
            RenderTransform = grow, RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new TextBlock
            {
                Text = Initials(friend.Name), FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var label = new TextBlock
        {
            Text = friend.Name, FontSize = 11, Foreground = DimBrush, MaxWidth = 66, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
        };
        var target = new StackPanel { Width = 74, Margin = new Thickness(2, 0, 2, 4), Background = Brushes.Transparent, AllowDrop = true, Children = { circle, label } };

        void Highlight(bool on)
        {
            circle.BorderThickness = new Thickness(on ? 2.5 : 0);
            grow.ScaleX = grow.ScaleY = on ? 1.1 : 1;
            label.Foreground = on ? Brushes.White : DimBrush;
        }
        target.DragEnter += (_, e) => Highlight(HasFiles(e));
        target.DragLeave += (_, _) => Highlight(false);
        target.DragOver += (_, e) =>
        {
            e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        target.Drop += (_, e) =>
        {
            e.Handled = true;
            Highlight(false);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            EndDrop();
            if (files is { Length: > 0 }) _ = BeamFilesAsync(friend, files);
        };
        return target;
    }

    static Brush AvatarBrush(string name)
    {
        int hash = 17;
        foreach (char c in name) hash = unchecked(hash * 31 + c); // string.GetHashCode changes every run
        return AvatarBrushes[(hash & int.MaxValue) % AvatarBrushes.Length];
    }

    /// <summary>"LH" for Lena Hoffmann, "J" for Jonas; whole letters, so an emoji or accent isn't cut in half.</summary>
    static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string First(string word) => new StringInfo(word).SubstringByTextElements(0, 1).ToUpper(CultureInfo.CurrentCulture);
        return words.Length == 0 ? "?" : words.Length == 1 ? First(words[0]) : First(words[0]) + First(words[^1]);
    }

    /// <summary>"a-very-long-na….pdf": keeps the extension, which says the most.</summary>
    static string Shorten(string name, int max)
    {
        if (name.Length <= max) return name;
        string extension = Path.GetExtension(name);
        if (extension.Length > 6) extension = "";
        return name[..Math.Max(1, max - extension.Length - 1)] + "…" + extension;
    }

    async Task BeamFilesAsync(BeamFriend friend, string[] paths)
    {
        int sent = 0;
        foreach (string path in paths)
        {
            string name = Shorten(Path.GetFileName(path.TrimEnd('\\')), 18);
            ShowPeek("", "Beaming to " + friend.Name, name, null, false, null, seconds: 300);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await _beam.SendAsync(friend, path, timeout.Token);
                sent++;
            }
            catch (BeamException ex)
            {
                ShowPeek("", ex.Message, name, null, false, RedBrush, seconds: 5);
                return;
            }
            catch (Exception ex)
            {
                Diag.Error(ex);
                ShowPeek("", "Beam failed", name, null, false, RedBrush, seconds: 5);
                return;
            }
        }
        string what = sent == 1 ? Shorten(Path.GetFileName(paths[0].TrimEnd('\\')), 18) : $"{sent} files";
        ShowPeek("", "Beamed to " + friend.Name, what, null, false, GreenBrush);
    }

    // ───────────────────────── receiving ─────────────────────────

    void OnBeamReceived(BeamReceived beam)
    {
        _lastBeam = beam;
        BeamRowTitle.Text = Path.GetFileName(beam.Path);
        BeamRowText.Text = "From " + beam.From;
        BeamRow.Visibility = Visibility.Visible;
        ShowPeek("", "From " + beam.From, Shorten(Path.GetFileName(beam.Path), 18), null, false, null, seconds: 5);
        if (_expanded) ApplyState(); // the open notch grows by a row
    }

    void BeamRow_Click(object sender, RoutedEventArgs e)
    {
        var beam = _lastBeam;
        _lastBeam = null;
        BeamRow.Visibility = Visibility.Collapsed;
        SetExpanded(false);
        if (beam == null) return;
        try { Shell.ShowInFolder(beam.Path); }
        catch (Exception ex) { Diag.Error(ex); }
    }

    // ───────────────────────── menus ─────────────────────────

    /// <summary>Beam › Copy my friend code, Add friend…, Remove friend ›, Your name…</summary>
    void FillBeamMenu()
    {
        MenuBeam.Items.Clear();
        MenuBeam.Items.Add(MenuEntry("Copy my friend code", CopyFriendCode, opensWindow: true));
        MenuBeam.Items.Add(MenuEntry("Add friend…", AddFriend, opensWindow: true));
        if (_beam.Friends.Count > 0)
        {
            var remove = new MenuItem { Header = "Remove friend" };
            foreach (var friend in _beam.Friends.ToList())
                remove.Items.Add(MenuEntry(Escape(friend.Name), () => RemoveFriend(friend)));
            MenuBeam.Items.Add(remove);
        }
        if (_beam.Me is { } me)
        {
            MenuBeam.Items.Add(new Separator());
            MenuBeam.Items.Add(MenuEntry("Your name: " + Escape(me.Name) + "…", RenameMe, opensWindow: true));
        }
    }

    void FillSizeMenu()
    {
        MenuSize.Items.Clear();
        foreach (var (name, scale) in Sizes)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Math.Abs(Scale - scale) < 0.01 };
            item.Click += (_, _) =>
            {
                _settings.Scale = scale;
                _settings.Save();
                ApplyScale();
            };
            MenuSize.Items.Add(item);
        }
    }

    /// <summary>Underscores would be read as keyboard shortcuts in a menu.</summary>
    static string Escape(string header) => header.Replace("_", "__");

    MenuItem MenuEntry(string header, Action action, bool opensWindow = false)
    {
        var item = new MenuItem { Header = header };
        // A window opened from inside the click would come up under the closing menu.
        item.Click += (_, _) => { if (opensWindow) Dispatcher.InvokeAsync(action, DispatcherPriority.Background); else action(); };
        return item;
    }

    /// <summary>First use: pick the name your friends will see.</summary>
    BeamIdentity? SetUpBeam()
    {
        string user = Environment.UserName;
        string? name = Prompt.Ask(this, "Set up Beam", "Your friends will see this name on the files you send them.",
                                  user.Length > 0 ? char.ToUpper(user[0], CultureInfo.CurrentCulture) + user[1..] : "", 40);
        return string.IsNullOrWhiteSpace(name) ? null : _beam.CreateIdentity(name);
    }

    void CopyFriendCode()
    {
        if ((_beam.Me ?? SetUpBeam()) is not { } me) return;
        if (TrySetClipboard(me.FriendCode))
            ShowPeek("", "Code copied: send it to a friend", "", null, false, null, seconds: 4);
        else
            ShowPeek("", "Clipboard busy, try again", "", null, false, RedBrush);
    }

    static bool TrySetClipboard(string text)
    {
        // Another program may hold the clipboard for a moment.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(50); }
        }
        return false;
    }

    void AddFriend()
    {
        string? clipboard = null;
        try { if (Clipboard.ContainsText()) clipboard = Clipboard.GetText(); } catch { }
        string? text = Prompt.Ask(this, "Add a friend", "Paste the friend code they sent you. They need yours too: right-click › Beam › Copy my friend code.",
                                  BeamIdentity.FindFriendCode(clipboard) ?? "", 4000);
        if (text == null) return;
        if (BeamIdentity.ParseFriendCode(text) is not { } friend)
        {
            ShowPeek("", "That's not a friend code", "", null, false, RedBrush);
            return;
        }
        if ((_beam.Me ?? SetUpBeam()) is not { } me) return;
        if (friend.Key == me.PublicKey)
        {
            ShowPeek("", "That's your own code", "", null, false, RedBrush);
            return;
        }
        _beam.AddFriend(friend);
        ShowPeek("", friend.Name + " added", "", null, false, null);
    }

    void RemoveFriend(BeamFriend friend)
    {
        _beam.RemoveFriend(friend);
        ShowPeek("", friend.Name + " removed", "", null, false, null);
    }

    void RenameMe()
    {
        if (_beam.Me is not { } me) return;
        string? name = Prompt.Ask(this, "Your name", "Friends who add you from now on see this name.", me.Name, 40);
        if (!string.IsNullOrWhiteSpace(name)) _beam.Rename(name);
    }
}
