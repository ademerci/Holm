using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch;

/// <summary>
/// Claude's real usage limits, the same numbers Claude Code's own /usage shows. Ported from Codenotch
/// (github.com/vinzdg/codenotch, MIT, © 2026 Vinz): it asks GET https://api.anthropic.com/api/oauth/usage with the
/// sign-in Claude Code keeps in ~/.claude/.credentials.json, and follows its rules:
/// <list type="bullet">
/// <item>the sign-in is only read, never written, logged or sent anywhere but Anthropic;</item>
/// <item>an expired sign-in is never sent (the endpoint answers it with a long 429), so the last reading stays, marked old;</item>
/// <item>at most one request every five minutes; 401 reads the file again once (Claude Code may just have renewed it);</item>
/// <item>429 waits 60 s × 2ⁿ up to 15 minutes, and a longer Retry-After wins;</item>
/// <item>a failure never invents a percentage: the last reading stays, marked stale.</item>
/// </list>
/// </summary>
internal sealed class ClaudeLimits
{
    const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    static readonly string[] CredentialNames = { ".credentials.json", "credentials.json" };
    internal static readonly TimeSpan Floor = TimeSpan.FromMinutes(5);
    static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("Holm", typeof(ClaudeLimits).Assembly.GetName().Version?.ToString(3) ?? "1") } },
    };

    readonly string _dir;
    DateTime _lastAttempt = DateTime.MinValue, _waitUntil = DateTime.MinValue;
    int _rateLimited;

    public ClaudeLimits(string claudeDir) => _dir = claudeDir;

    /// <summary>The last good reading, kept through failures.</summary>
    public IReadOnlyList<AiUsageLimit> Limits { get; private set; } = Array.Empty<AiUsageLimit>();
    public DateTime? FetchedAt { get; private set; }
    /// <summary>Why the last attempt gave no fresh reading, in a few words; null after a good one.</summary>
    public string? Problem { get; private set; }

    /// <summary>A reading older than two floors (the PC slept, the network is gone, the sign-in ran out) is shown as old.</summary>
    public bool IsStale(DateTime nowUtc) => FetchedAt is not { } at || nowUtc - at > Floor + Floor;

    /// <summary>Asks for fresh limits if the floor and any back-off allow it; otherwise keeps the last reading.</summary>
    public async Task RefreshAsync(DateTime nowUtc, CancellationToken cancel = default)
    {
        if (nowUtc - _lastAttempt < Floor || nowUtc < _waitUntil) return;
        _lastAttempt = nowUtc;
        if (ReadCredential() is not { } credential) { Problem = "Not signed in to Claude Code"; return; }
        if (credential.Expired(nowUtc)) { Problem = "Sign-in expired · run claude once to renew it"; return; }

        var (status, body, retryAfter) = await GetAsync(credential.Token, cancel);
        if (status == HttpStatusCode.Unauthorized && ReadCredential() is { } again && again.Token != credential.Token && !again.Expired(nowUtc))
            (status, body, retryAfter) = await GetAsync(again.Token, cancel);

        switch (status)
        {
            case HttpStatusCode.OK when body != null:
                using (var json = JsonDocument.Parse(body))
                    Limits = Parse(json.RootElement);
                FetchedAt = nowUtc;
                Problem = null;
                _rateLimited = 0;
                break;
            case HttpStatusCode.Unauthorized:
                Problem = "Sign-in no longer valid · run claude once";
                break;
            case HttpStatusCode.TooManyRequests:
                var backoff = TimeSpan.FromSeconds(Math.Min(60 * Math.Pow(2, _rateLimited++), MaxBackoff.TotalSeconds));
                if (retryAfter > backoff) backoff = retryAfter.Value; // only ever raises it, even past the cap
                _waitUntil = nowUtc + backoff;
                Problem = "Asked too often · trying again later";
                break;
            default:
                Problem = status == 0 ? "Offline" : $"Claude answered {(int)status}";
                break;
        }
    }

    static async Task<(HttpStatusCode Status, string? Body, TimeSpan? RetryAfter)> GetAsync(string token, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
            TimeSpan? retry = response.Headers.RetryAfter is { } r ? r.Delta ?? (r.Date - DateTimeOffset.UtcNow) : null;
            string? body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false) : null;
            return (response.StatusCode, body, retry);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return (0, null, null);
        }
    }

    sealed record Credential(string Token, DateTime? ExpiresAt)
    {
        public bool Expired(DateTime nowUtc) => ExpiresAt is { } at && at <= nowUtc.AddMinutes(1);
    }

    Credential? ReadCredential()
    {
        foreach (var name in CredentialNames)
        {
            try
            {
                string path = Path.Combine(_dir, name);
                if (!File.Exists(path)) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var oauth = json.RootElement.TryGetProperty("claudeAiOauth", out var o) ? o : json.RootElement;
                if (!oauth.TryGetProperty("accessToken", out var t) || t.GetString() is not { Length: > 0 } token || string.IsNullOrWhiteSpace(token)) continue;
                DateTime? expires = oauth.TryGetProperty("expiresAt", out var e) && e.TryGetDouble(out double ms)
                    ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
                return new Credential(token, expires);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }

    /// <summary>
    /// The reply's windows, session first. <c>limits</c> is the main shape; the named <c>five_hour</c> and
    /// <c>seven_day</c> fill in a window that just rolled over, without twins (same alias, label, or reset and share).
    /// </summary>
    internal static IReadOnlyList<AiUsageLimit> Parse(JsonElement root)
    {
        var found = new List<(string Id, AiUsageLimit Limit)>();
        if (root.TryGetProperty("limits", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
            {
                if (Str(item, "kind") is not { } kind || !Num(item, "percent", out double percent) || Reset(item) is not { } reset) continue;
                found.Add((kind, new AiUsageLimit(Math.Clamp(percent, 0, 100), Minutes(kind), reset) { Label = LabelFor(kind) }));
            }
        foreach (var (field, id, aliases) in new[] { ("five_hour", "session", new[] { "session", "five_hour" }), ("seven_day", "seven_day", new[] { "seven_day", "weekly_all", "weekly" }) })
        {
            if (!root.TryGetProperty(field, out var w) || w.ValueKind != JsonValueKind.Object || !Num(w, "utilization", out double used)) continue;
            var reset = Reset(w);
            string label = LabelFor(id);
            used = Math.Clamp(used, 0, 100);
            bool twin = found.Any(x => aliases.Contains(x.Id) || x.Limit.Label == label
                || (reset != null && x.Limit.ResetsAt is { } r && Math.Abs((r - reset.Value).TotalSeconds) < 1 && Math.Abs(x.Limit.UsedPercent - used) < 0.5));
            if (!twin) found.Add((id, new AiUsageLimit(used, Minutes(id), reset) { Label = label }));
        }
        return found.OrderBy(x => x.Id is "session" or "five_hour" ? 0 : 1).Select(x => x.Limit).ToList();
    }

    static string LabelFor(string kind) => kind switch
    {
        "session" or "five_hour" => "Current session",
        "seven_day" or "weekly_all" or "weekly" => "Weekly · all models",
        "seven_day_opus" or "weekly_opus" => "Weekly · Opus",
        "weekly_scoped" => "Weekly · model-scoped",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.Replace('_', ' ')),
    };

    static int? Minutes(string kind) => kind is "session" or "five_hour" ? 300 : kind.StartsWith("weekly", StringComparison.Ordinal) || kind.StartsWith("seven_day", StringComparison.Ordinal) ? 10080 : null;

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Num(JsonElement e, string name, out double value)
    {
        value = 0;
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out value) && double.IsFinite(value);
    }
    static DateTime? Reset(JsonElement e) => Str(e, "resets_at") is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
}
