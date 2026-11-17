using System.Net;
using System.Security.Cryptography;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>Two notches on "the same network": two services on this machine that announce to each other over loopback.</summary>
public class NearbyServiceTests
{
    static (NearbyService A, NearbyService B) Pair(string nameA, string nameB)
    {
        int portA = Random.Shared.Next(48000, 49000), portB = portA + 1;
        var a = new NearbyService(nameA, portA, new[] { new IPEndPoint(IPAddress.Loopback, portB) });
        var b = new NearbyService(nameB, portB, new[] { new IPEndPoint(IPAddress.Loopback, portA) });
        return (a, b);
    }

    static async Task<NearbyPeer> WaitForPeerAsync(NearbyService service, string name)
    {
        for (int i = 0; i < 100; i++)
        {
            var peer = service.Peers.FirstOrDefault(p => p.Name == name);
            if (peer != null) return peer;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{name} was never discovered.");
    }

    [Fact]
    public async Task Services_find_each_other_and_forget_on_stop()
    {
        var (a, b) = Pair("Noah", "Lena");
        using (a)
        using (b)
        {
            a.Start();
            b.Start();
            var lena = await WaitForPeerAsync(a, "Lena");
            var noah = await WaitForPeerAsync(b, "Noah");

            Assert.Equal(b.Id, lena.Id);
            Assert.Equal(a.Id, noah.Id);
            Assert.DoesNotContain(a.Peers, p => p.Id == a.Id); // never lists itself

            b.Stop(); // says goodbye
            for (int i = 0; i < 50 && a.Peers.Any(p => p.Id == lena.Id); i++) await Task.Delay(100);
            Assert.DoesNotContain(a.Peers, p => p.Id == lena.Id);
        }
    }

    [Fact]
    public async Task A_file_goes_from_one_notch_to_the_other_encrypted()
    {
        using var source = new TempFolder();
        using var downloads = new TempFolder();
        var content = RandomNumberGenerator.GetBytes(200_000);
        string path = source.File("Holiday.zip", content);
        var (a, b) = Pair("Noah", "Lena");
        var received = new TaskCompletionSource<IReadOnlyList<string>?>();
        b.Incoming = async (channel, from) =>
        {
            try { received.TrySetResult(await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(true), downloads.Path, null, Net.Timeout())); }
            catch (Exception ex) { received.TrySetException(ex); }
        };

        using (a)
        using (b)
        {
            a.Start();
            b.Start();
            var lena = await WaitForPeerAsync(a, "Lena");

            await using var channel = await NearbyService.ConnectAsync(lena, Net.Timeout());
            bool accepted = await FileTransfer.SendAsync(channel, "Noah", new[] { path }, null, Net.Timeout());
            var saved = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(accepted);
            Assert.Equal(content, File.ReadAllBytes(saved!.Single()));
        }
    }

    [Fact]
    public async Task Only_one_incoming_transfer_at_a_time()
    {
        var (a, b) = Pair("Noah", "Lena");
        var firstArrived = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        b.Incoming = async (channel, from) => { firstArrived.TrySetResult(); await release.Task; };

        using (a)
        using (b)
        {
            a.Start();
            b.Start();
            var lena = await WaitForPeerAsync(a, "Lena");

            await using var first = await NearbyService.ConnectAsync(lena, Net.Timeout());
            await firstArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // A second sender gets hung up on while the first one is being handled.
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var second = await NearbyService.ConnectAsync(lena, Net.Timeout(10));
                await second.ReceiveAsync(Net.Timeout(10));
            });
            release.SetResult();
        }
    }
}
