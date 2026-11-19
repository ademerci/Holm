using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using WinNotch;
using WinNotch.Account;

namespace Holm.App;

/// <summary>
/// The Holm account (Holm.Core/Account): optional, offered once at the first start with what it keeps, and in the
/// Holm window under Account any time after. Settings that come from the account run what their switch does, as if
/// flipped here; a profile from the account reaches friends and people nearby as an edit here would.
/// </summary>
public sealed partial class NotchWindow
{
    AccountService? _account;
    /// <summary>The Account page is open as the first-start offer, with "Not now".</summary>
    bool _offeringAccount;

    void InitAccount()
    {
        bool test = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
        // Self-tests and the test runner pose with made-up data, which must never reach an account.
        if (!AccountService.Available || test || SelfTestFolder != null) return;
        _account = new AccountService(_settings, _achievements, _friends, action => Dispatcher.UIThread.InvokeAsync(action).GetTask(), Diag.Folder);
        _account.ApplyingSettings = ApplyAccountSettings;
        _account.ProfileApplied += AccountProfileApplied;
        _account.IdentityQuestion += AskAccountIdentity;
    }

    void StartAccount()
    {
        if (_account == null) return;
        _account.Start();
        if (_settings.AccountOffered || _account.SignedIn) return;
        // Once, a moment after the first start with accounts: the offer with what the account keeps. Never again by itself.
        OneShot(4000, () =>
        {
            _settings.AccountOffered = true;
            _settings.Save();
            _offeringAccount = true;
            ShowHub(AccountPage.Key);
        }).Start();
    }

    void StopAccount() => _account?.Dispose();

    HubView BuildAccountPage()
    {
        Action? notNow = _offeringAccount ? () => _settingsWindow?.Close() : null;
        _offeringAccount = false;
        return new AccountPage(_account, _settings, notNow);
    }

    /// <summary>Runs the merge (<paramref name="apply"/>), then what each switch it changed does here.</summary>
    void ApplyAccountSettings(Action apply)
    {
        var switches = SettingsGroups().SelectMany(g => g.Switches).ToList();
        var before = switches.Select(s => s.Get()).ToList();
        bool focumonBefore = _settings.Focumon;
        bool remindersBefore = _settings.Reminders;
        apply();
        for (int i = 0; i < switches.Count; i++)
        {
            bool now = switches[i].Get();
            if (now == before[i]) continue;
            try { switches[i].Set(now); }
            catch (Exception ex) { Diag.Error(ex); } // one switch's trouble mustn't stop the others
        }
        try
        {
            if (_settings.Focumon != focumonBefore) SetFocumon(_settings.Focumon);
            if (_settings.Reminders != remindersBefore) ShowIdleStatus();
        }
        catch (Exception ex) { Diag.Error(ex); }
        ApplyBounce();
        ApplyHomeLayout(); // the start page as arranged on the other computer
        ApplyState(animate: true);
        _settingsWindow?.Refresh();
    }

    void AccountProfileApplied()
    {
        _pictures.Clear();
        _friends.ProfileChanged();
        if (_nearby != null)
        {
            // People nearby see the name the network service started with: start it again with the new one.
            _nearby.Dispose();
            _nearby = null;
            StartSharing();
        }
        _settingsWindow?.Refresh();
    }

    /// <summary>This PC has friends under its own identity and the account has another: the person picks one.</summary>
    void AskAccountIdentity()
    {
        if (_account is not { } account) return;
        ShowHub(AccountPage.Key);
        _settingsWindow?.ShowDialog("Which friend code?",
            "This computer has friends under its own friend code, and your account has another one. With the account's, friends " +
            "reach you the same way on every computer; friends who only know this computer's code get your account's and can add it back. " +
            "Keeping this computer's makes it the account's code on every computer instead.",
            ("Keep this computer's", () => _ = account.ChooseIdentityAsync(false)),
            ("Use the account's", () => _ = account.ChooseIdentityAsync(true)));
    }
}
