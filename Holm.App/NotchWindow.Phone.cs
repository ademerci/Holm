using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using QRCoder;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Beam to a phone (the WPF app's MainWindow.Phone.cs; PhoneShare linked): drop files on "Phone" and the notch shows
/// a QR code. The phone's camera opens a page with the files, served from this computer over the Wi-Fi – no app, no
/// cable, no cloud. The link ends when you click Done, ten minutes after it opened, or shortly after everything has
/// reached the phone. Right-click › Receive from phone… works the other way: the page lets an iPhone or Android phone
/// pick photos and files, which land in Downloads; that link ends with Done or ten minutes after the last file.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly IBrush PhoneIndigo = Brush("#FF5E5CE6");
    static readonly TimeSpan PhoneLinkLife = TimeSpan.FromMinutes(10);

    PhoneShare? _phone;
    readonly List<string> _phoneZips = new();
    readonly DispatcherTimer _phoneExpire = new() { Interval = PhoneLinkLife }, _phoneFinished = new() { Interval = TimeSpan.FromSeconds(15) };
    DateTime _lastPhoneUi;
    readonly Grid _phoneView = new() { Margin = new Thickness(16, 14, 16, 14), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    readonly Border _phoneQrHost = new() { Width = 132, Height = 132, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _phoneSubtitle = new() { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBlock _phoneStatus = new() { FontSize = 12, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

    readonly StackPanel _phoneAskButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
    StackPanel _phoneButtons = null!;
    TaskCompletionSource<bool>? _phoneAnswer;

    bool PhoneShowing => _phone != null;

    void InitPhone()
    {
        _phoneExpire.Tick += (_, _) => { _phoneExpire.Stop(); StopPhone(expired: true); };
        // The phone may ask once more (a second tap, a preview): don't pull the link away right after the last byte.
        _phoneFinished.Tick += (_, _) => { _phoneFinished.Stop(); StopPhone(); };
        _phoneView.Children.Add(_phoneQrHost);
        var words = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "Scan with your phone's camera", FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(_phoneSubtitle);
        words.Children.Add(_phoneStatus);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var done = NotchButton.Pill(ShareBlue);
        done.Label = "Done";
        done.Click += () => StopPhone();
        var copy = NotchButton.Pill();
        copy.Label = "Copy link";
        copy.Margin = new Thickness(6, 0, 0, 0);
        copy.Click += async () =>
        {
            if (_phone == null) return;
            _phoneStatus.Text = await TrySetClipboardAsync(_phone.Url) ? "Link copied – it works on this Wi-Fi only" : "Clipboard busy, try again";
        };
        buttons.Children.Add(done);
        buttons.Children.Add(copy);
        words.Children.Add(buttons);
        _phoneButtons = buttons;
        // A phone asks to send: Accept or Decline, in place of the buttons above.
        var accept = NotchButton.Pill(GreenBrush);
        accept.Label = "Accept";
        accept.Click += () => _phoneAnswer?.TrySetResult(true);
        var decline = NotchButton.Pill();
        decline.Label = "Decline";
        decline.Margin = new Thickness(6, 0, 0, 0);
        decline.Click += () => _phoneAnswer?.TrySetResult(false);
        _phoneAskButtons.Children.Add(accept);
        _phoneAskButtons.Children.Add(decline);
        words.Children.Add(_phoneAskButtons);
        Grid.SetColumn(words, 1);
        _phoneView.Children.Add(words);
        _phoneView.Opacity = 0;
        _phoneView.IsHitTestVisible = false;
        _islandContent.Children.Add(_phoneView);
    }

    void BeamToPhone(List<string> paths)
    {
        if (OpenPhoneLink(address => PhoneShare.Start(paths, address), Summary(paths)) is not { } share) return;
        share.Visited += () => Dispatcher.UIThread.Post(() => { if (_phone == share) _phoneStatus.Text = "Your phone is connected"; });
        share.Sending += (file, sent) => Dispatcher.UIThread.Post(() => OnPhoneSending(share, file, sent));
        share.Delivered += file => Dispatcher.UIThread.Post(() => OnPhoneDelivered(share, file));
        Diag.Log($"Phone link open for {paths.Count} file(s)");
    }

    /// <summary>The other way: the phone's browser sends photos and files into Downloads.</summary>
    void ReceiveFromPhone()
    {
        if (OpenPhoneLink(address => PhoneShare.StartReceiving(address, _shell.DownloadsFolder()), "Send photos and files to this computer") is not { } share) return;
        share.Visited += () => Dispatcher.UIThread.Post(() =>
        {
            if (_phone == share && share.ReceivedFiles.Count == 0) _phoneStatus.Text = "Your phone is connected. Choose photos or files there.";
        });
        share.Ask = request => Dispatcher.UIThread.InvokeAsync(() => AskPhoneAsync(share, request));
        share.Receiving += (name, got, total) => Dispatcher.UIThread.Post(() => OnPhoneReceiving(share, name, got, total));
        share.Received += path => Dispatcher.UIThread.Post(() => OnPhoneReceived(share, path));
        Diag.Log("Phone link open to receive");
    }

    void OnPhoneReceiving(PhoneShare share, string name, long got, long total)
    {
        if (_phone != share) return;
        RestartPhoneExpiry(); // the link lasts while files keep coming
        var now = DateTime.UtcNow;
        if (now - _lastPhoneUi < TimeSpan.FromMilliseconds(200)) return;
        _lastPhoneUi = now;
        _phoneStatus.Text = $"Receiving {name} · {(total > 0 ? Math.Min(100, got * 100 / total) : 100)}%";
    }

    void OnPhoneReceived(PhoneShare share, string path)
    {
        if (_phone != share) return;
        RestartPhoneExpiry();
        int count = share.ReceivedFiles.Count;
        _phoneStatus.Text = count == 1 ? $"Received {System.IO.Path.GetFileName(path)} · in Downloads" : $"Received {count} files · in Downloads";
    }

    /// <summary>
    /// A phone asks to send: nothing is saved before you accept here. Anyone who has the QR code could ask, so it's
    /// your yes that counts; no answer in a minute is no.
    /// </summary>
    async Task<bool> AskPhoneAsync(PhoneShare share, PhoneRequest request)
    {
        if (_phone != share) return false;
        RestartPhoneExpiry();
        var answer = _phoneAnswer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string what = request.Count == 1 ? request.FirstName : $"{request.Count} files";
        _phoneStatus.Text = $"{request.Device} wants to send {what} · {PhoneShare.Size(request.Bytes)}. Accept only if it's yours.";
        _phoneButtons.IsVisible = false;
        _phoneAskButtons.IsVisible = true;
        PlayIncomingFileSound();
        await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        bool yes = answer.Task.IsCompletedSuccessfully && answer.Task.Result;
        if (_phoneAnswer == answer) _phoneAnswer = null;
        _phoneButtons.IsVisible = true;
        _phoneAskButtons.IsVisible = false;
        if (_phone == share) _phoneStatus.Text = yes ? $"Receiving {what}…" : "Declined. Nothing was saved.";
        Diag.Log(yes ? "Phone accepted" : "Phone declined");
        return yes && _phone == share;
    }

    void RestartPhoneExpiry()
    {
        _phoneExpire.Stop();
        _phoneExpire.Start();
    }

    /// <summary>Starts the link on this computer's Wi-Fi address and shows its QR code; null (with a word why) if it can't.</summary>
    PhoneShare? OpenPhoneLink(Func<System.Net.IPAddress, PhoneShare> start, string subtitle)
    {
        StopPhone();
        var address = LanAddress.Find();
#if DEBUG
        // HOLM_DEV_LAN=address (Debug builds): a network address for machines without one, to see the code.
        if (address == null && System.Net.IPAddress.TryParse(Environment.GetEnvironmentVariable("HOLM_DEV_LAN"), out var dev)) address = dev;
#endif
        if (address == null)
        {
            ShowPeek("", "Connect to Wi-Fi first", "Your phone needs the same network", Dim, seconds: 4);
            return null;
        }
        PhoneShare share;
        try { share = start(address); }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Phone link: " + ex.Message);
            ShowPeek("", "Couldn't open a link for your phone", "", RedBrush);
            return null;
        }
        _phone = share;
        _phoneQrHost.Child = QrImage(share.Url, 132);
        _phoneSubtitle.Text = subtitle;
        _phoneStatus.Text = "Your phone has to be on the same Wi-Fi as this computer. The link isn't encrypted, so avoid public Wi-Fi.";
        if (_taskVisible) HideTask();
        SetExpanded(false);
        ApplyState(animate: true);
        _phoneExpire.Start();
        return share;
    }

    void OnPhoneSending(PhoneShare share, int file, long sent)
    {
        if (_phone != share) return;
        var now = DateTime.UtcNow;
        if (now - _lastPhoneUi < TimeSpan.FromMilliseconds(200)) return;
        _lastPhoneUi = now;
        var info = share.Files[file];
        int percent = info.Length > 0 ? (int)Math.Min(100, sent * 100 / info.Length) : 100;
        _phoneStatus.Text = $"Sending {info.Name} · {percent}%";
    }

    void OnPhoneDelivered(PhoneShare share, int file)
    {
        if (_phone != share) return;
        if (share.AllDelivered)
        {
            _phoneStatus.Text = share.Files.Count == 1 ? "Sent to your phone" : "All files are on your phone";
            _phoneFinished.Stop();
            _phoneFinished.Start();
        }
        else _phoneStatus.Text = $"Sent {share.Files[file].Name}";
    }

    void StopPhone(bool expired = false)
    {
        _phoneExpire.Stop();
        _phoneFinished.Stop();
        if (_phone is not { } share) return;
        _phone = null;
        _phoneAnswer?.TrySetResult(false); // a question still open is a no
        FolderPack.Forget(_phoneZips, FolderPack.TempFolder);
        _phoneZips.Clear();
        bool delivered = share.AllDelivered;
        share.Dispose();
        _phoneQrHost.Child = null;
        ApplyState(animate: true);
        Diag.Log(expired ? "Phone link expired" : "Phone link closed");
        var received = share.ReceivedFiles;
        if (share.IsReceiving && received.Count > 0)
        {
            // As a beam from a friend: open the file, or show it in Downloads.
            string first = received[0];
            EndTask("", GreenBrush, received.Count == 1 ? $"Saved {System.IO.Path.GetFileName(first)}" : $"Saved {received.Count} files",
                    "From your phone · in Downloads",
                    ("Open", () => { HideTask(); _shell.Open(first); }),
                    ("Show", () => { HideTask(); _shell.Reveal(first); }));
            _taskHide.Interval = TimeSpan.FromSeconds(8);
            _taskHide.Start();
        }
        else if (expired && !delivered) ShowPeek("", "The link for your phone expired", "", null);
    }

    /// <summary>The QR code drawn black on white as vector shapes, filling <paramref name="size"/>, quiet zone included (the WPF app's QrCode).</summary>
    static Control QrImage(string text, double size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix; // with a quiet zone of 4 modules on every side
        const int quiet = 4;
        int modules = matrix.Count - 2 * quiet;
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            for (int y = 0; y < modules; y++)
            {
                var row = matrix[y + quiet];
                for (int x = 0; x < modules; x++)
                {
                    if (!row[x + quiet]) continue;
                    int start = x;
                    while (x + 1 < modules && row[x + 1 + quiet]) x++; // one rectangle per run of dark modules
                    c.BeginFigure(new Point(start, y), true);
                    c.LineTo(new Point(x + 1, y));
                    c.LineTo(new Point(x + 1, y + 1));
                    c.LineTo(new Point(start, y + 1));
                    c.EndFigure(true);
                }
            }
        }
        double module = size / (modules + 8);
        var path = new Avalonia.Controls.Shapes.Path { Data = geometry, Fill = Brushes.Black, Stretch = Stretch.Fill, Width = module * modules, Height = module * modules };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased); // no seams between neighbouring modules
        return new Border { Width = size, Height = size, Background = Brushes.White, CornerRadius = new CornerRadius(module * 2), Child = path };
    }
}
