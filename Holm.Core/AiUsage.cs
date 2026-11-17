using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WinNotch;

// How much of their limits the AI coding agents on this PC have used, and what they are doing right now, read from the
// logs they keep themselves: Claude Code under ~/.claude/projects, Codex under ~/.codex/sessions. Read only: nothing
// is written, and nothing here ever leaves the PC. A reader remembers how far it got in each log, so a refresh only
// reads what was appended since the last one.

public enum AiTool { ClaudeCode, Codex }

public sealed record AiUsageLimit(double UsedPercent, int? WindowMinutes, DateTime? ResetsAt)
{
    /// <summary>The window's own name when the tool reports one (Claude: "Current session", "Weekly · Opus").</summary>
    public string? Label { get; init; }

    public string Name => Label ?? WindowMinutes switch
    {
        10080 => "weekly limit",
        1440 => "daily limit",
        > 0 when WindowMinutes % 60 == 0 => $"{WindowMinutes / 60}-hour limit",
        > 0 => $"{WindowMinutes}-minute limit",
        _ => "reported limit",
    };
}

/// <summary>An agent session at work: the folder it works in and its latest step.</summary>
public sealed record AiSession(AiTool Tool, string Project, string? Step, DateTime LastActive);

/// <summary>
/// One tool's usage. <see cref="Percent"/> is 0–100 or null when unknown. Codex logs its own limits, so its percent is
/// the last reported one (<see cref="IsLimit"/>), with durations in Limits; Claude Code logs only tokens, so its percent compares the current 5-hour
/// window with the busiest one of the past week.
/// </summary>
public sealed record AiUsageSnapshot(AiTool Tool, double? Percent, string Label, DateTime? ResetsAt, long TokensInWindow, IReadOnlyList<AiSession> Active)
{
    public bool IsLimit { get; init; }
    public IReadOnlyList<AiUsageLimit> Limits { get; init; } = Array.Empty<AiUsageLimit>();
    public DateTime? ReportedAt { get; init; }
    public bool IsStale { get; init; }
    /// <summary>Codex: the reported weekly percentage, only when a 7-day duration is supplied.</summary>
    public double? WeekPercent { get; init; }
    /// <summary>Claude Code: the tokens of the busiest 5-hour window in the past week.</summary>
    public long BusiestTokens { get; init; }
}

public static class AiUsage
{
    /// <summary>A session counts as working while its turn hasn't ended and its log changed this recently.</summary>
    public static TimeSpan ActiveWithin = TimeSpan.FromMinutes(5);

    /// <summary>Null when the folder doesn't exist or holds no logs from the last 7 days.</summary>
    public static AiUsageSnapshot? ReadClaudeCode(string claudeDir, DateTime nowUtc) => new AiUsageReader(AiTool.ClaudeCode, claudeDir).Read(nowUtc);

    /// <summary>Null when the folder doesn't exist or holds no logs from the last 7 days.</summary>
    public static AiUsageSnapshot? ReadCodex(string codexDir, DateTime nowUtc) => new AiUsageReader(AiTool.Codex, codexDir).Read(nowUtc);

    // ───────────── for tests: the same parsing, fed with lines ─────────────

    internal static (long Tokens, DateTime? WindowStart, long Busiest) ClaudeWindows(IEnumerable<string> lines, DateTime nowUtc)
    {
        var log = Parsed(AiTool.ClaudeCode, lines);
        log.Usage.Sort((a, b) => a.Ticks.CompareTo(b.Ticks));
        return AiUsageReader.Windows(log.Usage, nowUtc);
    }

    internal static string? ClaudeStep(IEnumerable<string> lines) => Parsed(AiTool.ClaudeCode, lines).ShownStep;

    internal static (double? Primary, double? Secondary, DateTime? ResetsAt) CodexLimits(IEnumerable<string> lines)
    {
        var log = Parsed(AiTool.Codex, lines);
        return (log.Primary, log.Secondary, log.ResetsAt);
    }

    internal static string? CodexStep(IEnumerable<string> lines) => Parsed(AiTool.Codex, lines).ToolStep;

    static AiLog Parsed(AiTool tool, IEnumerable<string> lines)
    {
        var log = new AiLog();
        foreach (string line in lines)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            if (tool == AiTool.ClaudeCode) AiUsageReader.ParseClaude(log, bytes);
            else AiUsageReader.ParseCodex(log, bytes);
        }
        return log;
    }
}

/// <summary>What a reader keeps of one log file.</summary>
internal sealed class AiLog
{
    /// <summary>How far the file has been read: the byte after the last complete line.</summary>
    public long Offset;
    public DateTime LastWrite;
    public bool Subagent;
    public string? Cwd, ToolStep;
    public bool LastWasUser, Ended;

    // Claude Code: one entry per assistant message.
    public readonly List<(long Ticks, long Tokens)> Usage = new();
    public readonly Dictionary<string, int> Ids = new();

    // Codex: the limits it last reported, and the session's running total.
    public double? Primary, Secondary;
    public readonly List<AiUsageLimit> Limits = new();
    public DateTime? LimitsAt, ResetsAt, WeekResetsAt;
    public long TotalTokens;

    /// <summary>The latest tool call, else whether the agent is writing or thinking.</summary>
    public string ShownStep => ToolStep ?? (LastWasUser ? "Thinking" : "Writing");
}

/// <summary>Reads one tool's logs, a little more on every call.</summary>
internal sealed class AiUsageReader
{
    /// <summary>A log seen for the first time is read from this far before its end: enough for days of work.</summary>
    internal static long FirstRead = 8 * 1024 * 1024;
    static readonly TimeSpan Kept = TimeSpan.FromDays(7), Window = TimeSpan.FromHours(5);
    const long QuietBusiest = 2_000_000; // a first day's few tokens shouldn't read as "100 %"

    readonly AiTool _tool;
    readonly string _dir;
    readonly Dictionary<string, AiLog> _logs = new(StringComparer.OrdinalIgnoreCase);

    public AiUsageReader(AiTool tool, string dir)
    {
        _tool = tool;
        _dir = dir ?? "";
    }

    delegate void LineHandler(ReadOnlySpan<byte> line);

    public AiUsageSnapshot? Read(DateTime nowUtc)
    {
        string root = Path.Combine(_dir, _tool == AiTool.ClaudeCode ? "projects" : "sessions");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_dir.Length == 0 || !Directory.Exists(root)) { _logs.Clear(); return null; }
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            string pattern = _tool == AiTool.ClaudeCode ? "*.jsonl" : "rollout-*.jsonl";
            foreach (var file in new DirectoryInfo(root).EnumerateFiles(pattern, options))
            {
                if (nowUtc - file.LastWriteTimeUtc > Kept) continue;
                seen.Add(file.FullName);
                if (!_logs.TryGetValue(file.FullName, out var log))
                    _logs[file.FullName] = log = new AiLog { Subagent = file.FullName.Contains(@"\subagents\", StringComparison.OrdinalIgnoreCase) || file.FullName.Contains("/subagents/", StringComparison.OrdinalIgnoreCase) };
                log.LastWrite = file.LastWriteTimeUtc;
                Update(file, log);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        foreach (string gone in _logs.Keys.Where(path => !seen.Contains(path)).ToList()) _logs.Remove(gone);
        if (_logs.Count == 0) return null;
        return _tool == AiTool.ClaudeCode ? Claude(nowUtc) : Codex(nowUtc);
    }

    // ───────────── reading ─────────────

    void Update(FileInfo file, AiLog log)
    {
        long length = file.Length;
        if (length == log.Offset) return;
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            length = stream.Length;
            if (length < log.Offset) Forget(log); // replaced by a shorter file: start over
            long start = log.Offset;
            bool midLine = false;
            if (start == 0 && length > FirstRead)
            {
                start = length - FirstRead;
                midLine = true;
                // A Codex log names its folder only in its first line.
                if (_tool == AiTool.Codex) ReadLines(stream, 0, Math.Min(length, 64 * 1024), false, line => ParseCodex(log, line));
            }
            LineHandler parse = _tool == AiTool.ClaudeCode ? line => ParseClaude(log, line) : line => ParseCodex(log, line);
            log.Offset = ReadLines(stream, start, length, midLine, parse);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // locked right now: next time
    }

    static void Forget(AiLog log)
    {
        log.Offset = 0;
        log.Usage.Clear();
        log.Ids.Clear();
        log.ToolStep = null;
        log.Primary = log.Secondary = null;
        log.Limits.Clear();
        log.LimitsAt = log.ResetsAt = log.WeekResetsAt = null;
        log.TotalTokens = 0;
    }

    /// <summary>
    /// Hands every complete line between <paramref name="start"/> and <paramref name="end"/> to the handler and returns
    /// the position after the last one: a line still being written is read next time.
    /// </summary>
    static long ReadLines(FileStream stream, long start, long end, bool skipFirst, LineHandler handle)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        MemoryStream? carry = null; // the start of a line that runs across buffers
        long consumed = start, position = start;
        try
        {
            stream.Seek(start, SeekOrigin.Begin);
            while (position < end)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - position));
                if (read <= 0) break;
                int from = 0;
                while (from < read)
                {
                    int newline = Array.IndexOf(buffer, (byte)'\n', from, read - from);
                    if (newline < 0) break;
                    if (skipFirst) skipFirst = false;
                    else if (carry is { Length: > 0 })
                    {
                        carry.Write(buffer, from, newline - from);
                        handle(Trimmed(carry.GetBuffer().AsSpan(0, (int)carry.Length)));
                    }
                    else handle(Trimmed(buffer.AsSpan(from, newline - from)));
                    carry?.SetLength(0);
                    from = newline + 1;
                    consumed = position + from;
                }
                if (from < read && !skipFirst) (carry ??= new MemoryStream()).Write(buffer, from, read - from);
                position += read;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return consumed;
    }

    static ReadOnlySpan<byte> Trimmed(ReadOnlySpan<byte> line) => line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line;

    // ───────────── Claude Code ─────────────

    /// <summary>
    /// One line of a Claude Code log. Only the assistant's lines are parsed (they carry the tokens and the tool calls);
    /// the user's, which hold whole files and tool results, are only noticed.
    /// </summary>
    internal static void ParseClaude(AiLog log, ReadOnlySpan<byte> line)
    {
        if (line.IndexOf("\"type\":\"assistant\""u8) < 0)
        {
            if (line.IndexOf("\"type\":\"user\""u8) < 0) return;
            log.LastWasUser = true; // a prompt or a tool's result: the agent goes on
            log.Ended = false;
            log.Cwd ??= QuickString(line, "\"cwd\":\""u8);
            return;
        }
        using var doc = Json(line);
        if (doc == null) return;
        var root = doc.RootElement;
        if (Text(root, "type") != "assistant" || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return;

        if (Text(root, "cwd") is { Length: > 0 } cwd) log.Cwd = cwd;
        log.LastWasUser = false;
        // A turn is over when the model stops for a reason other than calling a tool.
        log.Ended = Text(message, "stop_reason") is { } stop && stop != "tool_use";

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (Text(block, "type") == "tool_use" && Text(block, "name") is { Length: > 0 } name)
                    log.ToolStep = ClaudeStepText(name, block.TryGetProperty("input", out var input) ? input : default);

        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object || Time(root, "timestamp") is not { } at) return;
        // What was read from the cache again isn't new work.
        long tokens = Number(usage, "input_tokens") + Number(usage, "cache_creation_input_tokens") + Number(usage, "output_tokens");
        // One message is logged once per block, each time with its usage so far: the last one counts.
        if (Text(message, "id") is { Length: > 0 } id)
        {
            if (log.Ids.TryGetValue(id, out int index) && index < log.Usage.Count) { log.Usage[index] = (log.Usage[index].Ticks, tokens); return; }
            if (log.Ids.Count > 512) log.Ids.Clear(); // a message's lines follow each other closely
            log.Ids[id] = log.Usage.Count;
        }
        log.Usage.Add((at.Ticks, tokens));
    }

    static string ClaudeStepText(string name, JsonElement input)
    {
        if (name.StartsWith("mcp__", StringComparison.Ordinal) && name.LastIndexOf("__", StringComparison.Ordinal) is > 4 and var cut) name = name[(cut + 2)..];
        string? detail = null;
        if (input.ValueKind == JsonValueKind.Object)
            detail = Text(input, "description") is { Length: > 0 } description ? description
                   : Text(input, "file_path") is { Length: > 0 } file ? FileNameOf(file)
                   : Text(input, "command") is { Length: > 0 } command ? Cut(OneLine(command), 40)
                   : Text(input, "pattern") is { Length: > 0 } pattern ? Cut(OneLine(pattern), 40)
                   : null;
        return Cut(detail == null ? name : $"{name}: {OneLine(detail)}", 60);
    }

    AiUsageSnapshot Claude(DateTime nowUtc)
    {
        long oldest = (nowUtc - Kept).Ticks;
        var all = new List<(long Ticks, long Tokens)>();
        foreach (var log in _logs.Values)
        {
            if (log.Usage.Count > 0 && log.Usage[0].Ticks < oldest - TimeSpan.TicksPerDay)
            {
                log.Usage.RemoveAll(u => u.Ticks < oldest);
                log.Ids.Clear();
            }
            foreach (var use in log.Usage) if (use.Ticks >= oldest) all.Add(use);
        }
        all.Sort((a, b) => a.Ticks.CompareTo(b.Ticks));
        var (tokens, start, busiest) = Windows(all, nowUtc);
        double percent = tokens <= 0 ? 0 : Math.Min(100, Math.Round(tokens * 100.0 / Math.Max(QuietBusiest, busiest), 1));
        string label = tokens <= 0 ? "Quiet right now" : string.Create(CultureInfo.InvariantCulture, $"{percent:0} % of your busiest 5 hours");
        return new AiUsageSnapshot(AiTool.ClaudeCode, percent, label, start + Window, tokens, Sessions(nowUtc)) { BusiestTokens = busiest };
    }

    /// <summary>
    /// Five-hour windows as Claude's limits count them: one opens on the full hour of the first message after the
    /// last one closed. Returns the window around now (if any) and the largest one.
    /// </summary>
    internal static (long Tokens, DateTime? WindowStart, long Busiest) Windows(List<(long Ticks, long Tokens)> sorted, DateTime nowUtc)
    {
        long busiest = 0, current = 0, sum = 0;
        DateTime? currentStart = null;
        DateTime start = default, end = default;
        bool open = false;
        void Close()
        {
            if (!open) return;
            busiest = Math.Max(busiest, sum);
            if (nowUtc >= start && nowUtc < end) (current, currentStart) = (sum, start);
        }
        foreach (var (ticks, tokens) in sorted)
        {
            var at = new DateTime(ticks, DateTimeKind.Utc);
            if (!open || at >= end)
            {
                Close();
                start = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0, DateTimeKind.Utc);
                end = start + Window;
                sum = 0;
                open = true;
            }
            sum += tokens;
        }
        Close();
        return (current, currentStart, busiest);
    }

    // ───────────── Codex ─────────────

    internal static void ParseCodex(AiLog log, ReadOnlySpan<byte> line)
    {
        bool limits = line.IndexOf("\"token_count\""u8) >= 0, meta = !limits && line.IndexOf("\"session_meta\""u8) >= 0;
        bool call = !limits && !meta && (line.IndexOf("\"function_call\""u8) >= 0 || line.IndexOf("\"local_shell_call\""u8) >= 0 || line.IndexOf("\"custom_tool_call\""u8) >= 0);
        if (!limits && !meta && !call)
        {
            if (line.IndexOf("\"task_complete\""u8) >= 0 || line.IndexOf("\"turn_aborted\""u8) >= 0) log.Ended = true;
            else if (line.IndexOf("\"task_started\""u8) >= 0 || line.IndexOf("\"user_message\""u8) >= 0) log.Ended = false;
            return;
        }
        using var doc = Json(line);
        if (doc == null || !doc.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;
        string? type = Text(doc.RootElement, "type"), kind = Text(payload, "type");

        if (type == "session_meta")
        {
            if (Text(payload, "cwd") is { Length: > 0 } cwd) log.Cwd = cwd;
        }
        else if (kind == "token_count")
        {
            var info = payload.TryGetProperty("info", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
            if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("total_token_usage", out var total) && Number(total, "total_tokens") is > 0 and var sum)
                log.TotalTokens = sum;
            // The limits sit beside the token counts, or inside them in older logs.
            if (!(payload.TryGetProperty("rate_limits", out var rates) && rates.ValueKind == JsonValueKind.Object)
                && !(info.ValueKind == JsonValueKind.Object && info.TryGetProperty("rate_limits", out rates) && rates.ValueKind == JsonValueKind.Object)) return;
            var at = Time(doc.RootElement, "timestamp");
            // Primary and secondary are positions, not fixed durations. Replace the whole
            // snapshot so a removed secondary limit cannot survive from an older event.
            log.LimitsAt = at;
            log.Limits.Clear();
            var primary = Limit(rates, "primary", at);
            var secondary = Limit(rates, "secondary", at);
            if (primary != null) log.Limits.Add(primary);
            if (secondary != null) log.Limits.Add(secondary);
            (log.Primary, log.ResetsAt) = (primary?.UsedPercent, primary?.ResetsAt);
            (log.Secondary, log.WeekResetsAt) = (secondary?.UsedPercent, secondary?.ResetsAt);
        }
        else if (call && kind is "function_call" or "local_shell_call" or "custom_tool_call")
        {
            log.Ended = false;
            string name = Text(payload, "name") ?? (kind == "local_shell_call" ? "shell" : "tool");
            log.ToolStep = Cut(CodexCommand(payload) is { Length: > 0 } command ? $"{name}: {Cut(command, 40)}" : name, 60);
        }
    }

    static AiUsageLimit? Limit(JsonElement rates, string name, DateTime? at)
    {
        if (!rates.TryGetProperty(name, out var limit) || limit.ValueKind != JsonValueKind.Object) return null;
        if (!limit.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number) return null;
        DateTime? resets = null;
        if (limit.TryGetProperty("resets_at", out var when) && when.ValueKind == JsonValueKind.Number && when.TryGetInt64(out long unix))
            resets = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        else if (at != null && limit.TryGetProperty("resets_in_seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number)
            resets = at.Value.AddSeconds(seconds.GetDouble());
        int? minutes = limit.TryGetProperty("window_minutes", out var window) && window.ValueKind == JsonValueKind.Number && window.TryGetInt32(out int value) && value > 0 ? value : null;
        return new AiUsageLimit(Math.Clamp(used.GetDouble(), 0, 100), minutes, resets);
    }

    /// <summary>The command of a tool call: its arguments are JSON in a string, the command a list or a line.</summary>
    static string? CodexCommand(JsonElement payload)
    {
        try
        {
            JsonElement arguments = default;
            JsonDocument? parsed = null;
            if (payload.TryGetProperty("arguments", out var raw) && raw.ValueKind == JsonValueKind.String) arguments = (parsed = JsonDocument.Parse(raw.GetString() ?? "{}")).RootElement;
            else if (payload.TryGetProperty("action", out var action)) arguments = action;
            using (parsed)
            {
                if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("command", out var command)) return null;
                return command.ValueKind switch
                {
                    JsonValueKind.String => OneLine(command.GetString() ?? ""),
                    JsonValueKind.Array => OneLine(string.Join(' ', command.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()))),
                    _ => null,
                };
            }
        }
        catch (JsonException) { return null; }
    }

    AiUsageSnapshot Codex(DateTime nowUtc)
    {
        var newest = _logs.Values.Where(l => l.LimitsAt != null).OrderByDescending(l => l.LimitsAt).FirstOrDefault();
        var limits = newest?.Limits.ToArray() ?? Array.Empty<AiUsageLimit>();
        var first = limits.FirstOrDefault();
        bool stale = newest?.LimitsAt is { } at && (nowUtc - at > TimeSpan.FromMinutes(15)
            || limits.Any(l => l.ResetsAt <= nowUtc));
        long tokens = _logs.Values.Where(l => nowUtc - l.LastWrite <= Window).Sum(l => l.TotalTokens);
        string label = first == null ? "No limits reported yet" : $"of the {first.Name}";
        return new AiUsageSnapshot(AiTool.Codex, first?.UsedPercent, label, first?.ResetsAt, tokens, Sessions(nowUtc))
        {
            IsLimit = true, Limits = limits, ReportedAt = newest?.LimitsAt, IsStale = stale,
            WeekPercent = limits.FirstOrDefault(l => l.WindowMinutes == 10080)?.UsedPercent,
        };
    }

    // ───────────── shared ─────────────

    List<AiSession> Sessions(DateTime nowUtc)
        => _logs.Values.Where(l => !l.Subagent && !l.Ended && nowUtc - l.LastWrite <= AiUsage.ActiveWithin)
                .OrderByDescending(l => l.LastWrite)
                .Select(l => new AiSession(_tool, ProjectName(l.Cwd), _tool == AiTool.ClaudeCode ? l.ShownStep : l.ToolStep ?? "Working", l.LastWrite))
                .ToList();

    static readonly char[] PathSeparators = { '\\', '/' };

    // Split by hand so Windows paths logged on another OS (or in tests) read the same on Linux.
    static string FileNameOf(string path)
    {
        string trimmed = path.TrimEnd(PathSeparators);
        string name = trimmed[(trimmed.LastIndexOfAny(PathSeparators) + 1)..];
        return name.Length == 2 && name[1] == ':' ? "" : name;
    }

    static string ProjectName(string? cwd)
    {
        string name = string.IsNullOrWhiteSpace(cwd) ? "" : FileNameOf(cwd);
        return name.Length > 0 ? name : "A session";
    }

    static JsonDocument? Json(ReadOnlySpan<byte> line)
    {
        try
        {
            var reader = new Utf8JsonReader(line);
            return JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException) { return null; }
    }

    static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static long Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           ? value.TryGetInt64(out long whole) ? whole : (long)value.GetDouble() : 0;

    static DateTime? Time(JsonElement element, string name)
        => Text(element, name) is { } text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;

    /// <summary>A string value found by its key without parsing the line around it.</summary>
    static string? QuickString(ReadOnlySpan<byte> line, ReadOnlySpan<byte> key)
    {
        int at = line.IndexOf(key);
        if (at < 0) return null;
        var rest = line[(at + key.Length - 1)..]; // from the opening quote
        for (int i = 1; i < rest.Length && i < 1024; i++)
        {
            if (rest[i] == (byte)'\\') { i++; continue; }
            if (rest[i] != (byte)'"') continue;
            try { return JsonSerializer.Deserialize<string>(rest[..(i + 1)]); }
            catch (JsonException) { return null; }
        }
        return null;
    }

    static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    static string Cut(string text, int length) => text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";
}
