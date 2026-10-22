using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinNotch.Updates;

namespace WinNotch.Tests;

public class UpdateServiceTests
{
    const string Repository = "https://github.com/ademerci/Holm";
    static readonly Version Current = typeof(UpdateService).Assembly.GetName().Version!;

    [Fact]
    public async Task Checks_the_public_latest_endpoint_with_GitHub_headers()
    {
        using var client = Client(request =>
        {
            Assert.Equal(UpdateService.LatestReleaseUrl, request.RequestUri!.AbsoluteUri);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/vnd.github+json");
            Assert.Null(request.Headers.Authorization);
            return Json(ReleaseJson("v1.10.0"));
        });
        using var service = new UpdateService(client);

        var update = await service.CheckAsync(new Version(1, 9, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 10, 0), update.Version);
        Assert.Equal($"{Repository}/releases/tag/v1.10.0", update.ReleaseUrl.AbsoluteUri);
        Assert.Equal($"{Repository}/releases/download/v1.10.0/Holm-1.10.0-win-x64.zip", update.DownloadUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("v1.4.0")]
    [InlineData("v1.4.0.0")]
    [InlineData("v1.3.9")]
    [InlineData("v1.5.0-beta.1")]
    [InlineData("v1.5.0+preview")]
    [InlineData("latest")]
    [InlineData("v1.5")]
    [InlineData("v1.5.0\n")]
    [InlineData("v999999999999999.0.0")]
    public async Task Ignores_current_older_and_nonstable_tags(string tag)
    {
        using var client = Client(_ => Json(ReleaseJson(tag)));
        using var service = new UpdateService(client);

        Assert.Null(await service.CheckAsync(new Version(1, 4, 0, 0)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ignores_drafts_and_prereleases(bool draft, bool prerelease)
    {
        using var client = Client(_ => Json(ReleaseJson("v9.0.0", draft: draft, prerelease: prerelease)));
        using var service = new UpdateService(client);

        Assert.Null(await service.CheckAsync(Current));
    }

    [Fact]
    public async Task No_published_release_is_not_an_error()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = new UpdateService(client);
        Assert.Null(await service.CheckAsync(Current));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Rate_limiting_is_reported_instead_of_claiming_up_to_date(int status)
    {
        using var client = Client(_ => new HttpResponseMessage((HttpStatusCode)status));
        using var service = new UpdateService(client);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync(Current));
        Assert.Contains("limiting", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:abc")]
    [InlineData("md5:00000000000000000000000000000000")]
    public async Task Refuses_a_release_without_a_full_SHA256_digest(string? digest)
    {
        using var client = Client(_ => Json(ReleaseJson("v9.0.0", digest: digest)));
        using var service = new UpdateService(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Current));
    }

    [Theory]
    [InlineData("http://github.com/ademerci/Holm/releases/download/v9.0.0/Holm-9.0.0-win-x64.zip")]
    [InlineData("https://github.com.evil.test/ademerci/Holm/releases/download/v9.0.0/Holm-9.0.0-win-x64.zip")]
    [InlineData("https://github.com/someone/Holm/releases/download/v9.0.0/Holm-9.0.0-win-x64.zip")]
    [InlineData("https://github.com/ademerci/Holm/releases/download/v8.0.0/Holm-9.0.0-win-x64.zip")]
    [InlineData("https://github.com/ademerci/Holm/releases/download/v9.0.0/Holm-9.0.0-win-x64.zip?redirect=elsewhere")]
    public async Task Refuses_asset_URLs_outside_the_exact_repository_release(string url)
    {
        using var client = Client(_ => Json(ReleaseJson("v9.0.0", url: url)));
        using var service = new UpdateService(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Current));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(UpdateService.MaxDownloadBytes + 1)]
    public async Task Refuses_invalid_download_sizes(long size)
    {
        using var client = Client(_ => Json(ReleaseJson("v9.0.0", size: size)));
        using var service = new UpdateService(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Current));
    }

    [Fact]
    public async Task Refuses_framework_dependent_or_wrong_architecture_assets()
    {
        string json = ReleaseJson("v9.0.0").Replace("Holm-9.0.0-win-x64.zip", "Holm-9.0.0-win-arm64.zip");
        using var client = Client(_ => Json(json));
        using var service = new UpdateService(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Current));
    }

    [Fact]
    public async Task Refuses_unbounded_release_metadata()
    {
        using var client = Client(_ => Json(new string(' ', 1024 * 1024 + 1)));
        using var service = new UpdateService(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Current));
    }

    [Fact]
    public async Task Stages_only_the_verified_executable_and_leaves_existing_files_untouched()
    {
        byte[] executable = CurrentExecutable();
        byte[] archive = Zip(("Holm.exe", executable), ("README.txt", Encoding.UTF8.GetBytes("Release notes")));
        using var folder = new TempFolder();
        string existing = folder.File("settings.json", "user settings"u8.ToArray());
        var progress = new RecordingProgress();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        string staged = await service.DownloadAsync(Release(archive), folder.Path, progress);

        Assert.Equal(executable, await File.ReadAllBytesAsync(staged));
        Assert.Equal("user settings", await File.ReadAllTextAsync(existing));
        Assert.Equal("Holm.exe", Path.GetFileName(staged));
        Assert.NotEqual(folder.Path, Path.GetDirectoryName(staged));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(staged)!));
        Assert.Equal(0, progress.Values.First());
        Assert.Equal(100, progress.Values.Last());
        Assert.True(progress.Values.SequenceEqual(progress.Values.OrderBy(value => value)));
    }

    [Fact]
    public async Task Tampering_fails_checksum_validation_and_removes_partial_download()
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()));
        var release = Release(archive);
        archive[^1] ^= 1;
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release, folder.Path));

        Assert.Contains("checksum", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Truncated_and_oversized_streams_are_rejected_without_ContentLength(int difference)
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()));
        byte[] body = difference < 0 ? archive[..^1] : archive.Concat(new byte[] { 0 }).ToArray();
        using var folder = new TempFolder();
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(body) });
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive), folder.Path));

        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("..\\outside.exe")]
    [InlineData("subfolder/Holm.exe")]
    [InlineData("C:\\outside.exe")]
    [InlineData("Holm.exe:extra")]
    public async Task Archive_paths_cannot_escape_staging(string path)
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()), (path, new byte[] { 1 }));
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive), folder.Path));

        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Theory]
    [InlineData("another.exe")]
    [InlineData("dependency.dll")]
    [InlineData("Holm.runtimeconfig.json")]
    public async Task Multi_file_applications_are_not_mistaken_for_standalone_updates(string extra)
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()), (extra, new byte[] { 1 }));
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive), folder.Path));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Duplicate_executables_are_rejected()
    {
        byte[] exe = CurrentExecutable();
        byte[] archive = Zip(("Holm.exe", exe), ("Holm.exe", exe));
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive), folder.Path));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Oversized_expanded_executable_is_rejected_before_extraction()
    {
        byte[] archive = Zip(("Holm.exe", new byte[] { 1 }));
        for (int offset = 0; offset < archive.Length - 28; offset++)
        {
            if (BitConverter.ToUInt32(archive, offset) != 0x02014b50) continue;
            BitConverter.GetBytes((uint)(UpdateService.MaxExecutableBytes + 1)).CopyTo(archive, offset + 24);
            break;
        }
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive), folder.Path));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Valid_checksum_does_not_allow_a_mismatched_executable_version()
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()));
        using var folder = new TempFolder();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(archive, new Version(99, 0, 0)), folder.Path));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Cancellation_leaves_no_partial_update()
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()));
        using var folder = new TempFolder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = Client(_ => Bytes(archive));
        using var service = new UpdateService(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(Release(archive), folder.Path, ct: cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task Untrusted_release_objects_are_rejected_before_any_request_or_disk_write()
    {
        byte[] archive = Zip(("Holm.exe", CurrentExecutable()));
        var release = Release(archive) with { DownloadUrl = new Uri("https://evil.test/Holm.exe") };
        using var folder = new TempFolder();
        using var client = Client(_ => throw new InvalidOperationException("No request should be made."));
        using var service = new UpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release, folder.Path));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    }

    static byte[] CurrentExecutable() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Holm.exe"));

    static UpdateRelease Release(byte[] bytes, Version? version = null)
    {
        version ??= Current;
        string number = version.ToString(3);
        string tag = "v" + number;
        return new UpdateRelease(version, tag, new Uri($"{Repository}/releases/tag/{tag}"),
            new Uri($"{Repository}/releases/download/{tag}/Holm-{number}-win-x64.zip"), bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    static string ReleaseJson(string tag, bool draft = false, bool prerelease = false,
                              string? digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                              string? url = null, long size = 100)
    {
        string name = $"Holm-{tag.TrimStart('v')}-win-x64.zip";
        return JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease,
            assets = new[] { new { name, state = "uploaded", size, digest, browser_download_url = url ?? $"{Repository}/releases/download/{tag}/{name}" } },
        });
    }

    static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                using var output = archive.CreateEntry(entry.Name).Open();
                output.Write(entry.Bytes);
            }
        return stream.ToArray();
    }

    static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new FakeHandler(response));
    static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Values { get; } = new();
        public void Report(int value) => Values.Add(value);
    }
}
