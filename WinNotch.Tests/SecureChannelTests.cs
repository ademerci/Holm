using System.Security.Cryptography;
using System.Text;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class SecureChannelTests
{
    [Fact]
    public async Task Messages_arrive_in_both_directions()
    {
        var (sender, receiver) = await Net.ChannelPairAsync();
        await using (sender)
        await using (receiver)
        {
            var ct = Net.Timeout();
            await sender.SendAsync(Encoding.UTF8.GetBytes("hello"), ct);
            await sender.SendAsync(new byte[SecureChannel.MaxPlaintext], ct);
            await receiver.SendAsync(Encoding.UTF8.GetBytes("hi back"), ct);

            Assert.Equal("hello", Encoding.UTF8.GetString(await receiver.ReceiveAsync(ct)));
            Assert.Equal(SecureChannel.MaxPlaintext, (await receiver.ReceiveAsync(ct)).Length);
            Assert.Equal("hi back", Encoding.UTF8.GetString(await sender.ReceiveAsync(ct)));
        }
    }

    [Fact]
    public async Task Frames_are_encrypted_on_the_wire()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wire = new MemoryStream();
        var secret = Encoding.UTF8.GetBytes("my secret holiday photo bytes");
        await new SecureChannel(wire, key, initiator: true).SendAsync(secret, CancellationToken.None);

        Assert.DoesNotContain("holiday", Encoding.UTF8.GetString(wire.ToArray()));
    }

    [Fact]
    public async Task A_changed_byte_is_detected()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wire = new MemoryStream();
        await new SecureChannel(wire, key, initiator: true).SendAsync(new byte[100], CancellationToken.None);

        var bytes = wire.ToArray();
        bytes[20] ^= 0x01;
        var receiver = new SecureChannel(new MemoryStream(bytes), key, initiator: false);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => receiver.ReceiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_wrong_key_is_rejected()
    {
        var wire = new MemoryStream();
        await new SecureChannel(wire, RandomNumberGenerator.GetBytes(32), initiator: true).SendAsync(new byte[10], CancellationToken.None);
        wire.Position = 0;
        var receiver = new SecureChannel(wire, RandomNumberGenerator.GetBytes(32), initiator: false);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => receiver.ReceiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_replayed_frame_is_rejected()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wire = new MemoryStream();
        await new SecureChannel(wire, key, initiator: true).SendAsync(new byte[10], CancellationToken.None);
        var frame = wire.ToArray();

        var replay = new MemoryStream(frame.Concat(frame).ToArray()); // the same frame twice
        var receiver = new SecureChannel(replay, key, initiator: false);
        await receiver.ReceiveAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => receiver.ReceiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_frame_cannot_be_reflected_back_to_its_sender()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wire = new MemoryStream();
        await new SecureChannel(wire, key, initiator: true).SendAsync(new byte[10], CancellationToken.None);
        wire.Position = 0;
        var sameSide = new SecureChannel(wire, key, initiator: true); // reads with the other direction's nonces

        await Assert.ThrowsAnyAsync<CryptographicException>(() => sameSide.ReceiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_huge_length_prefix_is_refused_before_allocating()
    {
        var bytes = new byte[] { 0x7F, 0xFF, 0xFF, 0xFF };
        var receiver = new SecureChannel(new MemoryStream(bytes), RandomNumberGenerator.GetBytes(32), initiator: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.ReceiveAsync(CancellationToken.None));
    }
}
