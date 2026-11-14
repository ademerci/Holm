using System.Net;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinNotch.Tests;

public class NetworkInfoTests
{
    static readonly NetworkDetails Home = new("Intel(R) Wi-Fi 7 BE201 320MHz", "A4-5E-60-12-AB-CD",
        new[] { IPAddress.Parse("192.168.1.42"), IPAddress.Parse("2001:db8::42"), IPAddress.Parse("2001:db8::abcd") },
        new[] { IPAddress.Parse("fe80::1%12"), IPAddress.Parse("192.168.1.1") },
        new[] { IPAddress.Parse("192.168.1.1"), IPAddress.Parse("1.1.1.1") }, true, "home");

    [Fact]
    public void Shows_what_matters_about_a_wifi_connection()
    {
        var rows = NetworkInfo.Rows(new NetworkLink(true, "Home", 4, 866_700_000), Home).ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Wi-Fi", rows["Connection"]);
        Assert.Equal("Home", rows["Network"]);
        Assert.Equal("4 of 5 bars", rows["Signal"]);
        Assert.Equal("866 Mbps", rows["Link speed"]);
        Assert.Equal("192.168.1.42", rows["IPv4 address"]);
        Assert.Equal("2001:db8::42", rows["IPv6 address"]);   // the first one: privacy addresses come last
        Assert.Equal("fe80::1, 192.168.1.1", rows["Gateway"]); // no "%12"
        Assert.Equal("192.168.1.1, 1.1.1.1", rows["DNS servers"]);
        Assert.Equal("On", rows["DHCP"]);
        Assert.Equal("A4-5E-60-12-AB-CD", rows["MAC address"]);
    }

    [Fact]
    public void A_cable_has_no_network_name_or_signal()
    {
        var labels = NetworkInfo.Rows(new NetworkLink(false, null, null, 1_000_000_000), Home).Select(r => r.Label).ToList();
        Assert.DoesNotContain("Network", labels);
        Assert.DoesNotContain("Signal", labels);
        Assert.Contains("Link speed", labels);
    }

    [Fact]
    public void Says_why_the_wifi_name_is_missing()
        => Assert.Contains("location", NetworkInfo.Rows(new NetworkLink(true, null, 3, 0), null).Single(r => r.Label == "Network").Value);

    [Fact]
    public void Nothing_known_means_no_rows() => Assert.Empty(NetworkInfo.Rows(null, null));

    [Fact]
    public void Unknown_values_are_left_out()
    {
        var bare = new NetworkDetails(null, null, Array.Empty<IPAddress>(), Array.Empty<IPAddress>(), Array.Empty<IPAddress>(), null, null);
        Assert.Equal(new[] { "Connection" }, NetworkInfo.Rows(new NetworkLink(false, null, null, 0), bare).Select(r => r.Label));
    }

    [Theory]
    [InlineData(new byte[] { 0xA4, 0x5E, 0x60, 0x12, 0xAB, 0xCD }, "A4-5E-60-12-AB-CD")]
    [InlineData(new byte[0], null)]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0 }, null)]
    public void Formats_mac_addresses(byte[] bytes, string? expected) => Assert.Equal(expected, NetworkInfo.FormatMac(bytes));

    [Theory]
    [InlineData("a5a57e1c9a8759fd-GVA", "GVA")]
    [InlineData("8f2b1c-ZRH", "ZRH")]
    [InlineData("8f2b1c", null)]
    [InlineData("8f2b1c-", null)]
    [InlineData("8f2b1c-<script>", null)]
    [InlineData(null, null)]
    public void Reads_the_cloudflare_location(string? ray, string? expected) => Assert.Equal(expected, SpeedTest.ServerOf(ray));

    [Fact]
    public void The_wifi_page_opens_in_the_holm_window()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var page = new HubPage("network", "", "Wi-Fi", "Your connection and how fast it is", () => new NetworkPage(demo: true));
                var window = new SettingsWindow(new[] { new SettingsGroup("general", "General", "", Colors.Gray, "", Array.Empty<SettingsSwitch>()) },
                    "test", false, () => { }, tools: new[] { page });
                window.Navigate("network");
                Assert.Equal("network", window.Page);
                var shown = Assert.IsType<NetworkPage>(window.ShownView);
                window.Show();
                window.UpdateLayout();
                if (Environment.GetEnvironmentVariable("HOLM_RENDER_TO") is { Length: > 0 } path)
                {
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render((Visual)window.Content);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(path);
                    png.Save(file);
                }
                window.Close(); // detaches the page
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
