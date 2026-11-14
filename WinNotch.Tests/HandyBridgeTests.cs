using System.IO;
using System.Net.Http;

namespace WinNotch.Tests;

public class HandyBridgeTests
{
    [Theory]
    [InlineData("\"C:\\Users\\x\\AppData\\Local\\Handy\"", null, @"C:\Users\x\AppData\Local\Handy\handy.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\Handy", null, @"C:\Users\x\AppData\Local\Handy\handy.exe")]
    [InlineData(null, "\"C:\\Apps\\Handy\\handy.exe\"", @"C:\Apps\Handy\handy.exe")]
    [InlineData("", "\"C:\\Apps\\Handy\\handy.exe\",0", @"C:\Apps\Handy\handy.exe")]
    [InlineData("  ", @"C:\Apps\Handy\handy.exe", @"C:\Apps\Handy\handy.exe")]
    public void Finds_handy_exe_from_the_uninstall_entry(string? location, string? icon, string expected) =>
        Assert.Equal(expected, HandyBridge.ParseInstallLocation(location, icon));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "\"\"")]
    [InlineData(null, @"C:\Apps\Handy\icon.ico")]
    public void No_install_when_the_entry_says_nothing_usable(string? location, string? icon) =>
        Assert.Null(HandyBridge.ParseInstallLocation(location, icon));

    [Theory]
    [InlineData("{\"settings\":{\"overlay_style\":\"minimal\",\"bindings\":{}}}", "minimal")]
    [InlineData("{\"settings\":{\"overlay_style\":\"none\"}}", "none")]
    [InlineData("{\"settings\":{\"overlay_style\":\"live\"}}", "live")]
    [InlineData("{\"settings\":{\"push_to_talk\":true}}", null)]
    [InlineData("{\"overlay_style\":\"none\"}", null)] // only the "settings" object counts
    [InlineData("{\"settings\":{\"overlay_style\":3}}", null)]
    [InlineData("{\"settings\":[]}", null)]
    [InlineData("[]", null)]
    [InlineData("not json {", null)]
    [InlineData("", null)]
    public void Reads_the_overlay_style(string json, string? expected) =>
        Assert.Equal(expected, HandyBridge.OverlayStyle(json));

    [Fact]
    public void Settings_live_in_handys_app_data_folder() =>
        Assert.EndsWith(Path.Combine("com.pais.handy", "settings_store.json"), HandyBridge.SettingsPath);

    const string Download = "https://github.com/cjpais/Handy/releases/download/v0.10.1/";

    static string Release(params (string Name, string Url)[] assets) =>
        "{\"tag_name\":\"v0.10.1\",\"draft\":false,\"assets\":["
        + string.Join(",", assets.Select(a => $"{{\"name\":\"{a.Name}\",\"state\":\"uploaded\",\"size\":17399128,\"browser_download_url\":\"{a.Url}\"}}"))
        + "]}";

    [Fact]
    public void Picks_only_the_x64_setup_exe()
    {
        string[] names =
        [
            "Handy_0.10.1_arm64-setup.exe", "Handy_0.10.1_arm64-setup.exe.sig", "Handy_0.10.1_arm64_en-US.msi",
            "Handy_0.10.1_x64_en-US.msi", "Handy_0.10.1_x64_en-US.msi.sig", "Handy_0.10.1_x64-setup.exe.sig",
            "Handy_0.10.1_x64-setup.exe", "Handy_0.10.1_x64.dmg", "Handy-0.10.1-1.aarch64.rpm",
        ];
        string json = Release(names.Select(n => (n, Download + n)).ToArray());
        Assert.Equal(Download + "Handy_0.10.1_x64-setup.exe", HandyBridge.PickInstaller(json));
    }

    [Theory]
    [InlineData("http://github.com/cjpais/Handy/releases/download/v0.10.1/Handy_0.10.1_x64-setup.exe")]
    [InlineData("https://github.com/someone/Handy/releases/download/v0.10.1/Handy_0.10.1_x64-setup.exe")]
    [InlineData("https://github.com.evil.example/cjpais/Handy/releases/download/v0.10.1/Handy_0.10.1_x64-setup.exe")]
    [InlineData("https://github.com:8443/cjpais/Handy/releases/download/v0.10.1/Handy_0.10.1_x64-setup.exe")]
    [InlineData("https://github.com/cjpais/Handy/releases/download/v0.10.1/Handy_0.10.1_x64-setup.exe.sig")]
    [InlineData("file:///C:/Handy_0.10.1_x64-setup.exe")]
    [InlineData("not a url")]
    public void Rejects_an_installer_from_anywhere_else(string url)
    {
        Assert.Null(HandyBridge.PickInstaller(Release(("Handy_0.10.1_x64-setup.exe", url))));
        // ...and skips it for a trusted one listed after it.
        string good = Download + "Handy_0.10.1_x64-setup.exe";
        Assert.Equal(good, HandyBridge.PickInstaller(Release(("Handy_0.10.1_x64-setup.exe", url), ("Handy_0.10.1_x64-setup.exe", good))));
    }

    [Theory]
    [InlineData("{\"assets\":[]}")]
    [InlineData("{\"message\":\"Not Found\"}")]
    [InlineData("{\"assets\":{}}")]
    [InlineData("[]")]
    [InlineData("<html>")]
    public void No_installer_in_an_empty_or_odd_release(string json) => Assert.Null(HandyBridge.PickInstaller(json));

    [Fact]
    public async Task Refuses_to_download_from_an_untrusted_url_without_asking_the_network()
    {
        using var http = new HttpClient(new NoNetwork());
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            HandyBridge.DownloadInstallerAsync(http, "https://example.com/Handy_0.10.1_x64-setup.exe", null, CancellationToken.None));
    }

    [Fact]
    public void Reads_the_digest_github_lists_for_the_installer()
    {
        string url = Download + "Handy_0.10.1_x64-setup.exe";
        string json = "{\"assets\":[{\"name\":\"Handy_0.10.1_x64-setup.exe\",\"browser_download_url\":\"" + url
                      + "\",\"digest\":\"sha256:3D0C05CFE2717370E64CA71E6CB36DD575D8B0F28424F48E00EFE18565A04EDF\"}]}";
        Assert.Equal("3d0c05cfe2717370e64ca71e6cb36dd575d8b0f28424f48e00efe18565a04edf", HandyBridge.PickDigest(json, url));
        Assert.Null(HandyBridge.PickDigest(Release(("Handy_0.10.1_x64-setup.exe", url)), url)); // none listed
    }

    [Fact]
    public async Task An_installer_that_doesnt_match_its_digest_is_refused_and_deleted()
    {
        using var http = new HttpClient(new Serves(new byte[] { 1, 2, 3 }));
        string url = Download + "Handy_0.10.1_x64-setup.exe";
        await Assert.ThrowsAsync<InvalidDataException>(() => HandyBridge.DownloadInstallerAsync(http, url, new string('0', 64), CancellationToken.None));
        string good = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
        string path = await HandyBridge.DownloadInstallerAsync(http, url, good, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    sealed class Serves(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Tests must not touch the network.");
    }
}
