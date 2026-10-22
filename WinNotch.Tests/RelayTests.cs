using System.Security.Cryptography;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>Real transfers through the public relay on the internet (needs a connection).</summary>
[Trait("Category", "Online")]
public class RelayTests
{
    [Fact]
    public async Task A_file_travels_through_the_public_relay_with_only_the_code_shared()
    {
        using var source = new TempFolder();
        using var downloads = new TempFolder();
        var content = RandomNumberGenerator.GetBytes(250_000);
        string path = source.File("Report.pdf", content);

        // The sender makes a code; the receiver only ever learns the code (as text).
        var senderSecret = ShareCode.NewSecret();
        string code = ShareCode.Format(senderSecret);
        Assert.True(ShareCode.TryFind($"WinNotch Beam code: {code}", out var receiverSecret, out _));

        var ct = Net.Timeout(90);
        var sending = Task.Run(async () =>
        {
            await using var stream = await RelayLink.ConnectAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(senderSecret), ct);
            await using var channel = new SecureChannel(stream, ShareCode.Key(senderSecret), initiator: true);
            return await FileTransfer.SendAsync(channel, "Noah", new[] { path }, null, ct);
        });
        var receiving = Task.Run(async () =>
        {
            await using var stream = await RelayLink.ConnectAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(receiverSecret), ct);
            await using var channel = new SecureChannel(stream, ShareCode.Key(receiverSecret), initiator: false);
            return await FileTransfer.ReceiveAsync(channel, offer => Task.FromResult(offer.Sender == "Noah"), downloads.Path, null, ct);
        });

        Assert.True(await sending);
        var saved = await receiving;
        Assert.Equal(content, File.ReadAllBytes(saved!.Single()));
    }

    [Fact]
    public async Task A_wrong_code_never_meets_the_sender()
    {
        // Different codes mean different relay channels: the two sides simply never get paired.
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token;
        var one = RelayLink.ConnectAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(ShareCode.NewSecret()), ct);
        var other = RelayLink.ConnectAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(ShareCode.NewSecret()), ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => one);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
    }
}
