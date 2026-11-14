using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The Wi-Fi page of the Holm window: a speed test that runs when you press Start, and the connection's details
/// (network, signal, addresses, gateway, DNS) read from Windows. A value is copied with a click. Leaving the page
/// stops a test that's running.
/// </summary>
internal sealed class NetworkPage : HubView
{
    readonly bool _demo;
    readonly TextBlock _big = new() { Text = "–", FontSize = 40, FontWeight = FontWeights.SemiBold };
    readonly TextBlock _phase = new() { FontSize = 12, Foreground = HubTheme.Dim, Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock _ping = Value(), _down = Value(), _up = Value();
    readonly TextBlock _via = new() { FontSize = 11, Foreground = HubTheme.Faint, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Button _go = HubTheme.Action("Start", primary: true);
    readonly StackPanel _rows = new();
    readonly DispatcherTimer _settle;
    CancellationTokenSource? _cancel;
    SpeedProgress? _last;
    bool _detached;

    /// <param name="demo">For snapshots: a made-up network, so your own never ends up in a picture.</param>
    public NetworkPage(bool demo = false)
    {
        _demo = demo;
        var content = new StackPanel();

        // ── the speed test ──
        var test = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = new StackPanel { Orientation = Orientation.Horizontal };
        number.Children.Add(_big);
        number.Children.Add(new TextBlock { Text = "Mbps", FontSize = 14, Foreground = HubTheme.Dim, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 9) });
        var words = new StackPanel();
        words.Children.Add(number);
        words.Children.Add(_phase);
        top.Children.Add(words);
        _go.Padding = new Thickness(22, 7, 22, 7);
        _go.VerticalAlignment = VerticalAlignment.Center;
        _go.Click += (_, _) => { if (_cancel != null) _cancel.Cancel(); else _ = RunAsync(); };
        Grid.SetColumn(_go, 1);
        top.Children.Add(_go);
        test.Children.Add(top);

        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 18, 0, 0) };
        stats.Children.Add(Stat("Ping", _ping));
        stats.Children.Add(Stat("Download", _down));
        stats.Children.Add(Stat("Upload", _up));
        test.Children.Add(stats);
        test.Children.Add(_via);
        content.Children.Add(Card(test));

        // ── the connection ──
        var caption = HubTheme.Caption("Connection");
        caption.Margin = new Thickness(4, 22, 0, 8);
        content.Children.Add(caption);
        content.Children.Add(Card(_rows, new Thickness(18, 6, 18, 6)));
        content.Children.Add(new TextBlock
        {
            Text = "Read from Windows on this PC. Click a value to copy it.", FontSize = 11, Foreground = HubTheme.Faint,
            Margin = new Thickness(4, 8, 0, 0),
        });
        Child = HubTheme.Scroller(content);

        _settle = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _settle!.Stop(); ShowDetails(); }, Dispatcher);
        _settle.Stop();
        if (demo)
        {
            Show(new SpeedProgress(SpeedPhase.Done, 14.2, 487.3, 92.6, "203.0.113.24", "ZRH"), null);
            _go.Content = "Again";
        }
        else
        {
            Show(null, "Measures ping, download and upload in about 15 seconds.");
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
        }
        ShowDetails();
    }

    public override void Detach()
    {
        _detached = true;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _settle.Stop();
        _cancel?.Cancel();
    }

    void OnAddressChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        if (_detached) return;
        _settle.Stop();
        _settle.Start();
    });

    void ShowDetails()
    {
        NetworkLink? link;
        NetworkDetails? details;
        if (_demo)
        {
            link = new NetworkLink(true, "Home", 4, 1_201_000_000);
            details = new NetworkDetails("Wi-Fi adapter", "A4-5E-60-12-AB-CD",
                new[] { IPAddress.Parse("192.168.1.42"), IPAddress.Parse("2001:db8::42") },
                new[] { IPAddress.Parse("192.168.1.1") }, new[] { IPAddress.Parse("192.168.1.1"), IPAddress.Parse("1.1.1.1") }, true, "home");
        }
        else
        {
            link = SpeedTest.CurrentLink();
            details = NetworkInfo.Read(link?.AdapterId);
        }
        _rows.Children.Clear();
        var rows = NetworkInfo.Rows(link, details);
        if (rows.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = "Not connected to a network.", Foreground = HubTheme.Dim, Margin = new Thickness(0, 10, 0, 10) });
            return;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) _rows.Children.Add(new Border { Height = 1, Background = HubTheme.Line });
            _rows.Children.Add(Row(rows[i].Label, rows[i].Value));
        }
    }

    async Task RunAsync()
    {
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        _go.Content = "Stop";
        var progress = new Progress<SpeedProgress>(p => { if (_cancel == cancel) Show(p, null); });
        try
        {
            _last = await SpeedTest.RunAsync(progress, cancel.Token);
            Show(_last, null);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!_detached) Show(_last, "Stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Diag.Log("Speed test: " + ex.GetType().Name);
            Show(_last, "The test server couldn't be reached. Check the connection and try again.");
        }
        finally
        {
            _cancel = null;
            _go.Content = _last == null ? "Start" : "Again";
        }
    }

    /// <summary>The big number is what's being measured now, or the download once it's done.</summary>
    void Show(SpeedProgress? p, string? note)
    {
        _ping.Text = p?.PingMs is { } ping ? SpeedTest.FormatPing(ping) + " ms" : "–";
        _down.Text = p?.DownMbps is { } down && p.Phase != SpeedPhase.Download ? SpeedTest.FormatMbps(down) + " Mbps" : "–";
        _up.Text = p?.UpMbps is { } up && p.Phase == SpeedPhase.Done ? SpeedTest.FormatMbps(up) + " Mbps" : "–";
        double? big = p?.Phase switch
        {
            SpeedPhase.Download or SpeedPhase.Done => p.DownMbps,
            SpeedPhase.Upload => p.UpMbps,
            _ => null,
        };
        _big.Text = big is { } value ? SpeedTest.FormatMbps(value) : "–";
        _phase.Text = note ?? p?.Phase switch
        {
            SpeedPhase.Ping => "Measuring ping…",
            SpeedPhase.Download => "Measuring download…",
            SpeedPhase.Upload => "Measuring upload…",
            SpeedPhase.Done => "Download",
            _ => "",
        };
        _via.Text = p?.PublicIp == null && p?.Server == null ? "Runs against Cloudflare's public speed test. Uses up to ~600 MB on a very fast line."
            : "Via Cloudflare" + (p!.Server is { } s ? $" ({s})" : "") + (p.PublicIp is { } ip ? " · public IP " + ip : "");
    }

    static TextBlock Value() => new() { Text = "–", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) };

    static FrameworkElement Stat(string label, TextBlock value)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = HubTheme.Dim });
        stack.Children.Add(value);
        return stack;
    }

    static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = padding ?? new Thickness(18), Child = child,
    };

    /// <summary>A label and its value; clicking the value copies it.</summary>
    static FrameworkElement Row(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to copy" };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBlock { Text = label, Foreground = HubTheme.Dim };
        grid.Children.Add(name);
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        grid.MouseLeftButtonUp += (_, _) =>
        {
            try { Clipboard.SetText(value); }
            catch (COMException) { return; } // the clipboard is busy
            name.Text = "Copied";
            name.Foreground = HubTheme.Green;
            var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            back.Tick += (_, _) => { back.Stop(); name.Text = label; name.Foreground = HubTheme.Dim; };
            back.Start();
        };
        return grid;
    }
}
