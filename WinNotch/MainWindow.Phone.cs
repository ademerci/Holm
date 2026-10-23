using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Beam to a phone: drop files on "Phone" and the notch shows a QR code. The phone's camera opens a page with the
/// files, served from this PC over the Wi-Fi – no app, no cable, no cloud. The link ends when you click Done, ten
/// minutes after it opened, or shortly after everything has reached the phone.
/// </summary>
public partial class MainWindow
{
    static readonly Brush PhoneIndigo = Frozen(Color.FromRgb(0x5E, 0x5C, 0xE6));
    static readonly TimeSpan PhoneLinkLife = TimeSpan.FromMinutes(10);

    PhoneShare? _phone;
    bool _demoPhone = false; // README screenshots only (set in Debug builds)
    DispatcherTimer _phoneExpire = null!, _phoneFinished = null!;
    DateTime _lastPhoneUi;

    bool PhoneShowing => _phone != null || _demoPhone;

    void InitPhone()
    {
        _phoneExpire = MakeTimer((int)PhoneLinkLife.TotalMilliseconds, () => StopPhone(expired: true), oneShot: true);
        // The phone may ask once more (a second tap, a preview): don't pull the link away right after the last byte.
        _phoneFinished = MakeTimer(15_000, () => StopPhone(), oneShot: true);
    }

    void BeamToPhone(List<string> paths)
    {
        StopPhone();
        if (LanAddress.Find() is not { } address)
        {
            ShowPeek("", "Connect to Wi-Fi first", "Your phone needs the same network", null, false, DimBrush, seconds: 4);
            return;
        }
        PhoneShare share;
        try { share = PhoneShare.Start(paths, address); }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Phone link: " + ex.Message);
            ShowPeek("", "Couldn't open a link for your phone", "", null, false, RedBrush);
            return;
        }
        _phone = share;
        share.Visited += () => Dispatcher.InvokeAsync(() => { if (_phone == share) PhoneStatus.Text = "Your phone is connected"; });
        share.Sending += (file, sent) => Dispatcher.InvokeAsync(() => OnPhoneSending(share, file, sent));
        share.Delivered += file => Dispatcher.InvokeAsync(() => OnPhoneDelivered(share, file));

        ShowPhoneCode(share.Url, Summary(paths));
        _phoneExpire.Start();
        Diag.Log($"Phone link open for {paths.Count} file(s)");
    }

    void ShowPhoneCode(string url, string summary)
    {
        PhoneQrHost.Child = QrCode.Image(url, 132);
        PhoneSubtitle.Text = summary;
        PhoneStatus.Text = "Your phone has to be on the same Wi-Fi as this PC.";
        if (_taskVisible) HideTask();
        SetExpanded(false);
        ApplyState();
    }

    void OnPhoneSending(PhoneShare share, int file, long sent)
    {
        if (_phone != share) return;
        var now = DateTime.UtcNow;
        if (now - _lastPhoneUi < TimeSpan.FromMilliseconds(200)) return;
        _lastPhoneUi = now;
        var info = share.Files[file];
        int percent = info.Length > 0 ? (int)Math.Min(100, sent * 100 / info.Length) : 100;
        PhoneStatus.Text = $"Sending {info.Name} · {percent}%";
    }

    void OnPhoneDelivered(PhoneShare share, int file)
    {
        if (_phone != share) return;
        if (share.AllDelivered)
        {
            PhoneStatus.Text = share.Files.Count == 1 ? "Sent to your phone" : "All files are on your phone";
            _phoneFinished.Stop();
            _phoneFinished.Start();
        }
        else PhoneStatus.Text = $"Sent {share.Files[file].Name}";
    }

    void StopPhone(bool expired = false)
    {
        _phoneExpire.Stop();
        _phoneFinished.Stop();
        if (_phone is not { } share) return;
        _phone = null;
        bool delivered = share.AllDelivered;
        share.Dispose();
        PhoneQrHost.Child = null;
        ApplyState();
        Diag.Log(expired ? "Phone link expired" : "Phone link closed");
        if (expired && !delivered) ShowPeek("", "The link for your phone expired", "", null, false, null);
    }

    void PhoneDone_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_demoPhone) return;
        StopPhone();
    }

    void PhoneCopy_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_phone == null) return;
        PhoneStatus.Text = TrySetClipboard(_phone.Url) ? "Link copied – it works on this Wi-Fi only" : "Clipboard busy, try again";
    }
}
