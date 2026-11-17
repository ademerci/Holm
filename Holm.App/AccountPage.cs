using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WinNotch;
using WinNotch.Account;

namespace Holm.App;

/// <summary>
/// The Holm account in the Holm window: what it keeps and how (the privacy statement), signing up or in, and once
/// signed in the sync's state, Sync now, Sign out and Delete account. Holm works the same without an account.
/// </summary>
internal sealed class AccountPage : HubView
{
    public const string Key = "account";
    public const int MinPassword = 10;

    /// <summary>Shown before anyone signs up: what the account keeps, where, and who can read it.</summary>
    public const string Privacy =
        "A Holm account is optional. It keeps your settings, profile, achievements and friends the same on every computer you sign in on.\n\n" +
        "What goes into it: the settings that mean the same everywhere (switches, clock, quiet hours, quick reactions), your profile " +
        "(name, status, color, picture), your achievements, your friends list, and your friends key and inbox, so friends reach you " +
        "with one friend code on every computer.\n\n" +
        "Encrypted here first: your password never leaves this computer. Holm makes two keys of it; Firebase (Google) gets one to " +
        "check your sign-in, the other opens your data and stays on your computers. The account service stores your email address, " +
        "when you sync, and sealed data it can't read. Firebase sees your IP address, as any web service does.\n\n" +
        "Stays on this computer: notes, reminders, the shelf, chats, files, where the notch sits and on which display, the weather " +
        "place and your AI app.\n\n" +
        "Where: Google Firebase. The data is stored in Zürich, Switzerland (europe-west6); sign-in is run by Firebase Authentication.\n\n" +
        "You can delete the account and everything in it here at any time. A forgotten password can't be reset: the account's data " +
        "is then lost, each computer keeps its own.";

    readonly AccountService? _account;
    readonly Action? _notNow;
    readonly Border _host = new();
    CancellationTokenSource? _busy;

    /// <param name="account">Null when this build has no account service.</param>
    /// <param name="notNow">Shown on the first-start offer: the person would rather not.</param>
    public AccountPage(AccountService? account, Action? notNow = null)
    {
        _account = account;
        _notNow = notNow;
        Child = HubTheme.Scroller(_host);
        if (_account != null) _account.Changed += Render;
        Render();
    }

    public override void Detach()
    {
        if (_account != null) _account.Changed -= Render;
        _busy?.Cancel();
    }

    void Render()
    {
        var content = new StackPanel { Spacing = 14 };
        if (_account == null)
        {
            content.Children.Add(Card(Words("The Holm account isn't part of this build.", dim: true)));
        }
        else if (_account.SignedIn) SignedIn(content, _account);
        else SignedOut(content, _account);
        _host.Child = content;
    }

    // ───────────── Signed out: the privacy statement and the form ─────────────

    void SignedOut(StackPanel content, AccountService account)
    {
        var about = new StackPanel { Spacing = 8 };
        about.Children.Add(new TextBlock { Text = "Your Holm, on every computer", FontSize = 17, FontWeight = FontWeight.SemiBold });
        about.Children.Add(Words(Privacy, dim: true));
        content.Children.Add(Card(about));

        var form = new StackPanel { Spacing = 10 };
        var email = HubTheme.Field("Email");
        var password = HubTheme.Field("Password (at least 10 characters)");
        password.PasswordChar = '•';
        var repeat = HubTheme.Field("Password again (new account only)");
        repeat.PasswordChar = '•';
        var agree = new CheckBox { Content = "I have read what the account keeps and agree", Foreground = Brushes.White };
        var message = Words("", dim: true);
        message.IsVisible = false;
        form.Children.Add(email);
        form.Children.Add(password);
        form.Children.Add(repeat);
        form.Children.Add(agree);
        form.Children.Add(message);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var signUp = HubTheme.Action("Create account", primary: true);
        var signIn = HubTheme.Action("Sign in");
        actions.Children.Add(signUp);
        actions.Children.Add(signIn);
        if (_notNow != null)
        {
            var later = HubTheme.Action("Not now");
            later.Click += (_, _) => _notNow();
            actions.Children.Add(later);
        }
        form.Children.Add(actions);
        content.Children.Add(Card(form));

        void Say(string text, bool problem)
        {
            message.Text = text;
            message.Foreground = problem ? HubTheme.Red : HubTheme.Dim;
            message.IsVisible = text.Length > 0;
        }

        async Task Run(bool create)
        {
            string mail = email.Text?.Trim() ?? "", pass = password.Text ?? "";
            if (!mail.Contains('@') || mail.Length < 5) { Say("Type your email address.", true); return; }
            if (pass.Length < MinPassword) { Say($"The password needs at least {MinPassword} characters.", true); return; }
            if (create && pass != repeat.Text) { Say("The two passwords aren't the same.", true); return; }
            if (agree.IsChecked != true) { Say("Tick the box once you've read what the account keeps.", true); return; }
            _busy?.Cancel();
            var cts = _busy = new CancellationTokenSource();
            signUp.IsEnabled = signIn.IsEnabled = false;
            Say(create ? "Creating your account…" : "Signing in…", false);
            try
            {
                if (create) await account.SignUpAsync(mail, pass, cts.Token);
                else await account.SignInAsync(mail, pass, cts.Token);
                // Changed has drawn the signed-in page already.
            }
            catch (AccountException ex) { Say(ex.Message, true); }
            catch (OperationCanceledException) { }
            finally { signUp.IsEnabled = signIn.IsEnabled = true; }
        }

        signUp.Click += async (_, _) => await Run(create: true);
        signIn.Click += async (_, _) => await Run(create: false);
    }

    // ───────────── Signed in ─────────────

    void SignedIn(StackPanel content, AccountService account)
    {
        var top = new StackPanel { Spacing = 6 };
        top.Children.Add(new TextBlock { Text = account.Email ?? "Signed in", FontSize = 17, FontWeight = FontWeight.SemiBold });
        string state = account.Syncing ? "Syncing…"
            : account.Problem is { } problem ? problem
            : account.LastSync is { } at ? "Synced " + at.LocalDateTime.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)
            : "Not synced yet";
        var status = Words(state, dim: account.Problem == null);
        if (account.Problem != null) status.Foreground = HubTheme.Red;
        top.Children.Add(status);
        top.Children.Add(Words("Settings, profile, achievements and friends stay the same on every computer signed in to this account. " +
                               "Everything is encrypted on your computer before it goes.", dim: true));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        var sync = HubTheme.Action("Sync now", primary: true);
        sync.IsEnabled = !account.Syncing;
        sync.Click += async (_, _) => await account.SyncAsync(CancellationToken.None);
        var signOut = HubTheme.Action("Sign out");
        signOut.Click += (_, _) => Ask("Sign out?",
            "This computer stops syncing. Everything stays here as it is, and in the account for your other computers.",
            ("Cancel", () => { }), ("Sign out", account.SignOut));
        var delete = HubTheme.Action("Delete account…");
        delete.Foreground = HubTheme.Red;
        delete.Click += (_, _) => Ask("Delete your Holm account?",
            "The account and everything in it are deleted for good. Every computer keeps its own settings, friends and achievements.",
            ("Cancel", () => { }), ("Delete", () => _ = DeleteAsync(account)));
        actions.Children.Add(sync);
        actions.Children.Add(signOut);
        actions.Children.Add(delete);
        top.Children.Add(actions);
        content.Children.Add(Card(top));

        var what = new StackPanel { Spacing = 4 };
        what.Children.Add(HubTheme.Caption("What the account keeps"));
        foreach (string line in new[]
                 {
                     "Settings that mean the same on every computer",
                     "Your profile: name, status, color and picture",
                     "Your achievements",
                     "Your friends and your friends key",
                 })
            what.Children.Add(Words("•  " + line, dim: false));
        what.Children.Add(Words("Notes, reminders, the shelf, chats and files stay on each computer.", dim: true));
        content.Children.Add(Card(what));
    }

    async Task DeleteAsync(AccountService account)
    {
        try { await account.DeleteAsync(CancellationToken.None); }
        catch (AccountException ex) { Ask("Couldn't delete the account", ex.Message, ("OK", () => { })); }
    }

    /// <summary>Asks in the Holm window's own dialog.</summary>
    void Ask(string title, string message, params (string Label, Action Run)[] choices)
    {
        if (TopLevel.GetTopLevel(this) is SettingsWindow window) window.ShowDialog(title, message, choices);
    }

    static Border Card(Control child) => new()
    {
        Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = child,
    };

    static TextBlock Words(string text, bool dim) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = dim ? HubTheme.Dim : Brushes.White,
    };
}
