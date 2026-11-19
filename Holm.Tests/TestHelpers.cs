using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>A temporary folder that is deleted again after the test.</summary>
sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinNotchTests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name, byte[] content)
    {
        string path = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

static class Net
{
    /// <summary>Two ends of a real TCP connection on this machine.</summary>
    public static async Task<(NetworkStream A, NetworkStream B)> LoopbackPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var server = await accept;
            return (new NetworkStream(client.Client, ownsSocket: true), new NetworkStream(server.Client, ownsSocket: true));
        }
        finally { listener.Stop(); }
    }

    /// <summary>A sender and a receiver channel sharing a fresh key, over loopback TCP.</summary>
    public static async Task<(SecureChannel Sender, SecureChannel Receiver)> ChannelPairAsync()
    {
        var (a, b) = await LoopbackPairAsync();
        var key = RandomNumberGenerator.GetBytes(32);
        return (new SecureChannel(a, key, initiator: true), new SecureChannel(b, key, initiator: false));
    }

    public static CancellationToken Timeout(int seconds = 30) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;
}
