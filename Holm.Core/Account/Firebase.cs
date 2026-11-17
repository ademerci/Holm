using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Account;

/// <summary>
/// The Holm account's Firebase project. The web API key only names the project (Google: it is meant to be public);
/// what a signed-in user may read and write is decided by the Firestore rules (firebase/firestore.rules).
/// </summary>
public static class FirebaseProject
{
    public const string ApiKey = "FIREBASE_API_KEY";
    public const string ProjectId = "FIREBASE_PROJECT_ID";

    public static bool IsConfigured => !ApiKey.StartsWith("FIREBASE_", StringComparison.Ordinal) && !ProjectId.StartsWith("FIREBASE_", StringComparison.Ordinal);
}

/// <summary>Why Firebase said no, in words for the user.</summary>
public sealed class AccountException : IOException
{
    /// <summary>Firebase's error code ("EMAIL_EXISTS", …), or null when the server couldn't be reached.</summary>
    public string? Code { get; }

    public AccountException(string message, string? code = null, Exception? inner = null) : base(message, inner) => Code = code;

    /// <summary>The email and password don't open an account (wrong password, no such account).</summary>
    public bool IsWrongLogin => Code is "INVALID_LOGIN_CREDENTIALS" or "EMAIL_NOT_FOUND" or "INVALID_PASSWORD";
}

/// <summary>A signed-in session: who, and the tokens to act for them.</summary>
public sealed record FirebaseSession(string Uid, string Email, string IdToken, string RefreshToken, DateTimeOffset Expires);

/// <summary>A document as Holm keeps it: string fields only, and the server's time of its last change.</summary>
public sealed record FirebaseDocument(IReadOnlyDictionary<string, string> Fields, string UpdateTime);

/// <summary>Firebase Authentication (email and password) and Cloud Firestore over their REST APIs.</summary>
public sealed class FirebaseClient
{
    static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "Holm-Account/1" } },
    };

    readonly HttpClient _http;
    readonly string _apiKey, _projectId;

    public FirebaseClient(string apiKey, string projectId, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _projectId = projectId;
        _http = http ?? SharedHttp;
    }

    string AuthUrl(string method) => $"https://identitytoolkit.googleapis.com/v1/accounts:{method}?key={Uri.EscapeDataString(_apiKey)}";
    string DocumentsUrl => $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_projectId)}/databases/(default)/documents";

    // ───────────── Authentication ─────────────

    public Task<FirebaseSession> SignUpAsync(string email, string secret, CancellationToken ct) => PasswordAsync("signUp", email, secret, ct);

    public Task<FirebaseSession> SignInAsync(string email, string secret, CancellationToken ct) => PasswordAsync("signInWithPassword", email, secret, ct);

    async Task<FirebaseSession> PasswordAsync(string method, string email, string secret, CancellationToken ct)
    {
        var body = new JsonObject { ["email"] = email, ["password"] = secret, ["returnSecureToken"] = true };
        var json = await SendAsync(HttpMethod.Post, AuthUrl(method), body, null, ct).ConfigureAwait(false);
        return new FirebaseSession(Text(json, "localId"), Text(json, "email"), Text(json, "idToken"), Text(json, "refreshToken"),
                                   DateTimeOffset.UtcNow.AddSeconds(Seconds(json, "expiresIn")));
    }

    /// <summary>A fresh ID token (they last an hour) from the refresh token, which lasts until the password changes or the account goes.</summary>
    public async Task<FirebaseSession> RefreshAsync(string email, string refreshToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(_apiKey)}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }),
        };
        var json = await SendAsync(request, ct).ConfigureAwait(false) ?? throw new AccountException("Firebase gave no answer.");
        return new FirebaseSession(Text(json, "user_id"), email, Text(json, "id_token"), Text(json, "refresh_token"),
                                   DateTimeOffset.UtcNow.AddSeconds(Seconds(json, "expires_in")));
    }

    /// <summary>Deletes the sign-in itself; its documents must go first (the rules only let the account remove them).</summary>
    public async Task DeleteAccountAsync(string idToken, CancellationToken ct)
        => await SendAsync(HttpMethod.Post, AuthUrl("delete"), new JsonObject { ["idToken"] = idToken }, null, ct).ConfigureAwait(false);

    // ───────────── Firestore ─────────────

    /// <summary>The document at <paramref name="path"/> ("users/uid/sync/settings"), or null if there is none.</summary>
    public async Task<FirebaseDocument?> GetAsync(string path, string idToken, CancellationToken ct)
    {
        try
        {
            var json = await SendAsync(HttpMethod.Get, $"{DocumentsUrl}/{path}", null, idToken, ct).ConfigureAwait(false);
            return json == null ? null : ToDocument(json);
        }
        catch (AccountException ex) when (ex.Code == "NOT_FOUND") { return null; }
    }

    /// <summary>
    /// Writes the document, but only if it is still as last read: <paramref name="updateTime"/> from that read, or null
    /// when there was none. False if someone else wrote it meanwhile (read it again and merge).
    /// </summary>
    public async Task<bool> PutAsync(string path, IReadOnlyDictionary<string, string> fields, string? updateTime, string idToken, CancellationToken ct)
    {
        var map = new JsonObject();
        foreach (var (name, value) in fields) map[name] = new JsonObject { ["stringValue"] = value };
        string precondition = updateTime == null ? "currentDocument.exists=false" : "currentDocument.updateTime=" + Uri.EscapeDataString(updateTime);
        try
        {
            await SendAsync(HttpMethod.Patch, $"{DocumentsUrl}/{path}?{precondition}", new JsonObject { ["fields"] = map }, idToken, ct).ConfigureAwait(false);
            return true;
        }
        catch (AccountException ex) when (ex.Code is "FAILED_PRECONDITION" or "ALREADY_EXISTS" or "NOT_FOUND") { return false; }
    }

    public async Task DeleteAsync(string path, string idToken, CancellationToken ct)
    {
        try { await SendAsync(HttpMethod.Delete, $"{DocumentsUrl}/{path}", null, idToken, ct).ConfigureAwait(false); }
        catch (AccountException ex) when (ex.Code == "NOT_FOUND") { }
    }

    static FirebaseDocument ToDocument(JsonNode json)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (json["fields"] is JsonObject map)
            foreach (var (name, value) in map)
                if (value?["stringValue"]?.GetValue<string>() is { } text) fields[name] = text;
        return new FirebaseDocument(fields, json["updateTime"]?.GetValue<string>() ?? "");
    }

    // ───────────── HTTP ─────────────

    Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonNode? body, string? idToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        if (idToken != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
        return SendAsync(request, ct);
    }

    async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                throw new AccountException("Couldn't reach the Holm account service. Check the internet connection.", null, ex);
            }
            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                JsonNode? json = null;
                try { if (text.Length > 0) json = JsonNode.Parse(text); } catch (JsonException) { }
                if (response.IsSuccessStatusCode) return json;
                string? code = ErrorCode(json, response.StatusCode);
                throw new AccountException(Describe(code), code);
            }
        }
    }

    /// <summary>Auth answers {"error":{"message":"EMAIL_EXISTS"}}, Firestore {"error":{"status":"FAILED_PRECONDITION"}}.</summary>
    static string? ErrorCode(JsonNode? json, HttpStatusCode status)
    {
        var error = json?["error"];
        string? message = null, state = null;
        try { message = error?["message"]?.GetValue<string>(); } catch (InvalidOperationException) { }
        try { state = error?["status"]?.GetValue<string>(); } catch (InvalidOperationException) { }
        // Auth's message is a code, sometimes followed by words: "WEAK_PASSWORD : Password should be at least 6 characters".
        if (message != null)
        {
            int cut = message.IndexOf(" : ", StringComparison.Ordinal);
            string first = (cut >= 0 ? message[..cut] : message).Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(first, "^[A-Z_]+$")) return first;
        }
        return state ?? (status == HttpStatusCode.NotFound ? "NOT_FOUND" : ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    static string Describe(string? code) => code switch
    {
        "EMAIL_EXISTS" => "There is already a Holm account with this email. Sign in instead.",
        "INVALID_LOGIN_CREDENTIALS" or "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" => "The email or the password is wrong.",
        "INVALID_EMAIL" => "That isn't an email address.",
        "USER_DISABLED" => "This Holm account has been switched off.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many tries. Wait a few minutes and try again.",
        "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "INVALID_ID_TOKEN" or "USER_NOT_FOUND" => "You were signed out. Sign in again.",
        "OPERATION_NOT_ALLOWED" => "Signing in with email isn't switched on for the Holm account service yet.",
        "PERMISSION_DENIED" or "UNAUTHENTICATED" => "The Holm account service refused this. Sign in again.",
        _ => "The Holm account service answered with an error" + (code != null ? $" ({code})." : "."),
    };

    static string Text(JsonNode? json, string name)
        => json?[name]?.GetValue<string>() ?? throw new AccountException("Firebase's answer is missing " + name + ".");

    static double Seconds(JsonNode? json, string name)
        => double.TryParse(json?[name]?.GetValue<string>(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 3600;
}
