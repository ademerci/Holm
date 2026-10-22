using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>
/// Sends a file to a real, running WinNotch through the public relay. Runs only when
/// WINNOTCH_E2E_CODEFILE is set: the test writes its code there, someone copies it (the notch
/// offers to receive) and clicks Accept. Without the variable it does nothing.
/// </summary>
[Trait("Category", "Manual")]
public class EndToEndTests
{
    [Fact]
    public async Task Send_to_a_running_notch_with_a_code()
    {
        string? codeFile = Environment.GetEnvironmentVariable("WINNOTCH_E2E_CODEFILE");
        if (string.IsNullOrEmpty(codeFile)) return;

        using var source = new TempFolder();
        string path = source.File("WinNotch end-to-end test.txt", Encoding.UTF8.GetBytes("Hello from the WinNotch end-to-end test – AirDrop works!"));
        var secret = ShareCode.NewSecret();
        File.WriteAllText(codeFile, ShareCode.Format(secret));

        var ct = Net.Timeout(240);
        await using var stream = await RelayLink.ConnectWithRetryAsync(RelayLink.DefaultHost, RelayLink.DefaultPort,
            ShareCode.RelayToken(secret), TimeSpan.FromMinutes(4), ct);
        await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
        Assert.True(await FileTransfer.SendAsync(channel, "WinNotch test", new[] { path }, null, ct));
    }

    /// <summary>
    /// The other direction: a running notch sends (a file was dropped on "Code"), this test receives.
    /// Runs only when WINNOTCH_E2E_RECEIVECODE (the copied code) and WINNOTCH_E2E_EXPECTED (the dropped file) are set.
    /// </summary>
    [Fact]
    public async Task Receive_from_a_running_notch_with_a_code()
    {
        string? code = Environment.GetEnvironmentVariable("WINNOTCH_E2E_RECEIVECODE");
        string? expected = Environment.GetEnvironmentVariable("WINNOTCH_E2E_EXPECTED");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(expected)) return;
        Assert.True(ShareCode.TryFind(code, out var secret, out _));

        using var downloads = new TempFolder();
        var ct = Net.Timeout(120);
        await using var stream = await RelayLink.ConnectWithRetryAsync(RelayLink.DefaultHost, RelayLink.DefaultPort,
            ShareCode.RelayToken(secret), TimeSpan.FromMinutes(2), ct);
        await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
        ShareOffer? offer = null;
        var saved = await FileTransfer.ReceiveAsync(channel, o => { offer = o; return Task.FromResult(true); }, downloads.Path, null, ct);

        Assert.Equal(Path.GetFileName(expected), offer!.Files.Single().Name);
        Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(saved!.Single()));
    }
}
