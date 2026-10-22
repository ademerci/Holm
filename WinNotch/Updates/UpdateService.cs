using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Updates;

internal sealed record UpdateRelease(Version Version, string Tag, Uri ReleaseUrl, Uri DownloadUrl, long Size, string Sha256);

/// <summary>Reads public stable releases and stages a verified standalone executable without touching the installed app.</summary>
internal sealed class UpdateService : IDisposable
{
    const string RepositoryUrl = "https://github.com/ademerci/Holm";
    internal const string LatestReleaseUrl = "https://api.github.com/repos/ademerci/Holm/releases/latest";
    internal const long MaxDownloadBytes = 256L * 1024 * 1024;
    internal const long MaxExecutableBytes = 512L * 1024 * 1024;
    const int MaxMetadataBytes = 1024 * 1024;
    static readonly Regex StableTag = new(@"\Av?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant);
    static readonly Regex Sha256Digest = new(@"\Asha256:([0-9a-fA-F]{64})\z", RegexOptions.CultureInvariant);
    readonly HttpClient _http;
    readonly bool _ownsClient;

    public UpdateService(HttpClient? client = null)
    {
        _ownsClient = client == null;
        _http = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(new Uri(LatestReleaseUrl));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub is limiting update checks. Try again later.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var metadata = new MemoryStream();
        await CopyBoundedAsync(stream, metadata, MaxMetadataBytes, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(metadata.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("GitHub returned an invalid release.");
        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
        string? tag = Text(root, "tag_name");
        if (!TryVersion(tag, out var version) || Normalize(version!) <= Normalize(currentVersion)) return null;

        string name = AssetName(tag!);
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The new release does not have an automatic-update download yet.");
        var matches = assets.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object && Text(a, "name") == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The new release does not have an automatic-update download yet.");
        var asset = matches[0];
        if (Text(asset, "state") != "uploaded" || !asset.TryGetProperty("size", out var sizeValue)
            || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out long size) || size <= 0 || size > MaxDownloadBytes)
            throw new InvalidDataException("The release download is incomplete or too large.");
        var digest = Sha256Digest.Match(Text(asset, "digest") ?? "");
        if (!digest.Success) throw new InvalidDataException("The release download does not have a SHA-256 checksum yet.");
        var releaseUrl = new Uri($"{RepositoryUrl}/releases/tag/{tag}");
        var downloadUrl = new Uri($"{RepositoryUrl}/releases/download/{tag}/{name}");
        if (!string.Equals(Text(asset, "browser_download_url"), downloadUrl.AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidDataException("The release download does not belong to Holm.");
        return new UpdateRelease(version!, tag!, releaseUrl, downloadUrl, size, digest.Groups[1].Value.ToLowerInvariant());
    }

    public async Task<string> DownloadAsync(UpdateRelease release, string stagingDirectory, IProgress<int>? progress = null,
                                           CancellationToken ct = default)
    {
        ValidateRelease(release);
        string root = Path.GetFullPath(stagingDirectory);
        string operationDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationDirectory);
        string zipPath = Path.Combine(operationDirectory, "download.zip");
        string exePath = Path.Combine(operationDirectory, "Holm.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        try
        {
            progress?.Report(0);
            using var request = Request(release.DownloadUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("The release download size changed. Check for updates again.");
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int lastProgress = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("The download is larger than the release promised.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    int percent = (int)(total * 99 / release.Size);
                    if (percent != lastProgress) { progress?.Report(percent); lastProgress = percent; }
                }
                if (total != release.Size) throw new InvalidDataException("The release download ended early.");
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("The release checksum did not match. The installed app has not been changed.");
            }
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var executable = FindExecutable(archive);
                await using var input = executable.Open();
                await using var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
                long extracted = await CopyBoundedAsync(input, output, MaxExecutableBytes, token).ConfigureAwait(false);
                if (extracted != executable.Length) throw new InvalidDataException("The update archive is incomplete.");
            }
            token.ThrowIfCancellationRequested();
            ValidateExecutable(exePath, release.Version);
            File.Delete(zipPath);
            progress?.Report(100);
            return exePath;
        }
        catch
        {
            // This directory was created for this operation; never remove the caller's staging root.
            try { Directory.Delete(operationDirectory, recursive: true); } catch { }
            throw;
        }
    }

    static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Holm-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    static void ValidateRelease(UpdateRelease release)
    {
        if (!TryVersion(release.Tag, out var version) || Normalize(version!) != Normalize(release.Version)
            || release.Size <= 0 || release.Size > MaxDownloadBytes || !Sha256Digest.IsMatch("sha256:" + release.Sha256)
            || release.DownloadUrl.AbsoluteUri != $"{RepositoryUrl}/releases/download/{release.Tag}/{AssetName(release.Tag)}"
            || release.ReleaseUrl.AbsoluteUri != $"{RepositoryUrl}/releases/tag/{release.Tag}")
            throw new InvalidDataException("The update does not describe a verified Holm release.");
    }

    static ZipArchiveEntry FindExecutable(ZipArchive archive)
    {
        ZipArchiveEntry? executable = null;
        if (archive.Entries.Count > 20) throw new InvalidDataException("The update archive contains too many files.");
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name is "." or ".."
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The update archive contains an unsafe path.");
            if (name == "Holm.exe")
            {
                if (executable != null || entry.Length <= 0 || entry.Length > MaxExecutableBytes)
                    throw new InvalidDataException("The update archive has an invalid executable.");
                executable = entry;
            }
            else
            {
                string extension = Path.GetExtension(name);
                if ((!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
                    && name != "LICENSE") || entry.Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("This release needs more than one executable and cannot be installed automatically.");
            }
        }
        return executable ?? throw new InvalidDataException("The update archive does not contain Holm.exe.");
    }

    internal static void ValidateExecutable(string path, Version expectedVersion)
    {
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64
            || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0
            || pe.PEHeaders.PEHeader?.Subsystem != Subsystem.WindowsGui)
            throw new InvalidDataException("The update is not a Windows x64 application.");
        var file = FileVersionInfo.GetVersionInfo(path);
        var actualVersion = new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart);
        if (Normalize(actualVersion) != Normalize(expectedVersion) || file.ProductName != "Holm")
            throw new InvalidDataException("The executable does not match the advertised Holm version.");
    }

    static bool TryVersion(string? tag, out Version? version)
    {
        version = null;
        return tag != null && StableTag.IsMatch(tag) && Version.TryParse(tag.TrimStart('v'), out version);
    }

    static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    static string AssetName(string tag) => $"Holm-{tag.TrimStart('v')}-win-x64.zip";
    static bool Flag(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("The update response exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return total;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
