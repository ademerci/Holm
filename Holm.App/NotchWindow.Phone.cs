using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
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
/// reached the phone.
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
        Grid.SetColumn(words, 1);
        _phoneView.Children.Add(words);
        _phoneView.Opacity = 0;
        _phoneView.IsHitTestVisible = false;
        _islandContent.Children.Add(_phoneView);
    }

    void BeamToPhone(List<string> paths)
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
            return;
        }
        PhoneShare share;
        try { share = PhoneShare.Start(paths, address); }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Phone link: " + ex.Message);
            ShowPeek("", "Couldn't open a link for your phone", "", RedBrush);
            return;
        }
        _phone = share;
        share.Visited += () => Dispatcher.UIThread.Post(() => { if (_phone == share) _phoneStatus.Text = "Your phone is connected"; });
        share.Sending += (file, sent) => Dispatcher.UIThread.Post(() => OnPhoneSending(share, file, sent));
        share.Delivered += file => Dispatcher.UIThread.Post(() => OnPhoneDelivered(share, file));

        _phoneQrHost.Child = QrImage(share.Url, 132);
        _phoneSubtitle.Text = Summary(paths);
        _phoneStatus.Text = "Your phone has to be on the same Wi-Fi as this computer. The link isn't encrypted, so avoid public Wi-Fi.";
        if (_taskVisible) HideTask();
        SetExpanded(false);
        ApplyState(animate: true);
        _phoneExpire.Start();
        Diag.Log($"Phone link open for {paths.Count} file(s)");
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
        FolderPack.Forget(_phoneZips, FolderPack.TempFolder);
        _phoneZips.Clear();
        bool delivered = share.AllDelivered;
        share.Dispose();
        _phoneQrHost.Child = null;
        ApplyState(animate: true);
        Diag.Log(expired ? "Phone link expired" : "Phone link closed");
        if (expired && !delivered) ShowPeek("", "The link for your phone expired", "", null);
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
