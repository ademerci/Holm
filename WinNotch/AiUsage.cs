using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WinNotch
{
    /// <summary>
    /// Represents the supported AI coding agent tools.
    /// </summary>
    public enum AiTool
    {
        ClaudeCode,
        Codex
    }

    /// <summary>
    /// Represents an active session of an AI coding agent.
    /// </summary>
    public sealed record AiSession(AiTool Tool, string Project, string? Step, DateTime LastActive);

    /// <summary>
    /// Represents the current usage and active sessions snapshot for an AI tool.
    /// </summary>
    public sealed record AiUsageSnapshot(AiTool Tool, double? Percent, string Label, DateTime? ResetsAt, long TokensInWindow, IReadOnlyList<AiSession> Active);

    /// <summary>
    /// Provides logic to read local logs of AI coding agents and compute usage and active sessions.
    /// </summary>
    public static class AiUsage
    {
        /// <summary>
        /// A session counts as working if its log changed within this duration.
        /// </summary>
        public static TimeSpan ActiveWithin = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Reads Claude Code usage and active sessions from the specified directory.
        /// </summary>
        public static AiUsageSnapshot? ReadClaudeCode(string claudeDir, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(claudeDir))
            {
                return null;
            }

            string projectsDir = Path.Combine(claudeDir, "projects");
            if (!Directory.Exists(projectsDir))
            {
                return null;
            }

            DateTime threshold = nowUtc.AddDays(-7);
            List<string> recentFiles = new List<string>();

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(projectsDir))
                {
                    foreach (string file in Directory.EnumerateFiles(dir, "*.jsonl"))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(file);
                            if (fi.LastWriteTimeUtc >= threshold)
                            {
                                recentFiles.Add(file);
                            }
                        }
                        catch
                        {
                            // Skip unreadable files/info
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            if (recentFiles.Count == 0)
            {
                return null;
            }

            List<ParsedClaudeLine> allLines = new List<ParsedClaudeLine>();
            List<AiSession> activeSessions = new List<AiSession>();

            foreach (string file in recentFiles)
            {
                FileInfo fi = new FileInfo(file);
                bool isActive = (nowUtc - fi.LastWriteTimeUtc) <= ActiveWithin;
                List<string> fileLines = ReadLastBytes(file);
                List<ParsedClaudeLine> parsedLines = new List<ParsedClaudeLine>();
                string? latestCwd = null;

                foreach (string line in fileLines)
                {
                    ParsedClaudeLine? parsed = ParseClaudeLine(line);
                    if (parsed != null)
                    {
                        parsedLines.Add(parsed.Value);
                        if (!string.IsNullOrEmpty(parsed.Value.Cwd))
                        {
                            latestCwd = parsed.Value.Cwd;
                        }
                    }
                }

                if (parsedLines.Count > 0)
                {
                    allLines.AddRange(parsedLines);

                    if (isActive)
                    {
                        string project = GetProjectName(latestCwd);
                        string? step = ClaudeStep(parsedLines.Select(p => p.RawLine));
                        activeSessions.Add(new AiSession(AiTool.ClaudeCode, project, step, fi.LastWriteTimeUtc));
                    }
                }
            }

            if (allLines.Count == 0)
            {
                return null;
            }

            var windowResult = ClaudeWindows(allLines.Select(p => p.RawLine), nowUtc);
            double? percent = null;
            string label = "quiet";

            if (windowResult.Busiest > 0 || windowResult.Tokens > 0)
            {
                long denom = Math.Max(2000000, windowResult.Busiest);
                percent = Math.Round(((double)windowResult.Tokens / denom) * 100.0, 1);
                if (windowResult.Tokens == 0)
                {
                    label = "quiet";
                    percent = 0;
                }
                else
                {
                    label = string.Format(CultureInfo.InvariantCulture, "about {0:0}% of your busiest 5 hours", percent.Value);
                }
            }

            DateTime? resetsAt = null;
            if (windowResult.WindowStart.HasValue)
            {
                resetsAt = windowResult.WindowStart.Value.AddHours(5);
            }

            return new AiUsageSnapshot(
                AiTool.ClaudeCode,
                percent,
                label,
                resetsAt,
                windowResult.Tokens,
                activeSessions
            );
        }

        /// <summary>
        /// Reads Codex usage and active sessions from the specified directory.
        /// </summary>
        public static AiUsageSnapshot? ReadCodex(string codexDir, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(codexDir))
            {
                return null;
            }

            string sessionsDir = Path.Combine(codexDir, "sessions");
            if (!Directory.Exists(sessionsDir))
            {
                return null;
            }

            DateTime threshold = nowUtc.AddDays(-7);
            List<string> recentFiles = new List<string>();

            try
            {
                foreach (string file in Directory.EnumerateFiles(sessionsDir, "rollout-*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc >= threshold)
                        {
                            recentFiles.Add(file);
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }
            catch
            {
                return null;
            }

            if (recentFiles.Count == 0)
            {
                return null;
            }

            List<string> allTokenLines = new List<string>();
            List<AiSession> activeSessions = new List<AiSession>();
            long totalTokensInWindow = 0;
            DateTime windowThreshold = nowUtc.AddHours(-5);

            foreach (string file in recentFiles)
            {
                FileInfo fi = new FileInfo(file);
                bool isActive = (nowUtc - fi.LastWriteTimeUtc) <= ActiveWithin;
                List<string> fileLines = ReadLastBytes(file);
                
                string? latestCwd = null;
                List<string> fileRawLines = new List<string>();
                long? fileLastTokens = null;

                foreach (string line in fileLines)
                {
                    fileRawLines.Add(line);
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(line))
                        {
                            JsonElement root = doc.RootElement;
                            if (root.TryGetProperty("type", out JsonElement typeElem))
                            {
                                string? type = typeElem.GetString();
                                if (type == "session_meta" && root.TryGetProperty("payload", out JsonElement payload))
                                {
                                    if (payload.TryGetProperty("cwd", out JsonElement cwdElem))
                                    {
                                        latestCwd = cwdElem.GetString();
                                    }
                                }
                                else if (type == "event_msg" && root.TryGetProperty("payload", out JsonElement ev))
                                {
                                    if (ev.TryGetProperty("type", out JsonElement pt) && pt.GetString() == "token_count")
                                    {
                                        allTokenLines.Add(line);
                                        long tokens = ExtractTotalTokens(ev);
                                        if (tokens > 0)
                                        {
                                            fileLastTokens = tokens;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip invalid JSON
                    }
                }

                if (fileLastTokens.HasValue && fi.LastWriteTimeUtc >= windowThreshold)
                {
                    totalTokensInWindow += fileLastTokens.Value;
                }

                if (fileRawLines.Count > 0 && isActive)
                {
                    string project = GetProjectName(latestCwd);
                    string? step = CodexStep(fileRawLines);
                    activeSessions.Add(new AiSession(AiTool.Codex, project, step, fi.LastWriteTimeUtc));
                }
            }

            var limits = CodexLimits(allTokenLines);
            double? primary = limits.Primary;
            double? secondary = limits.Secondary;
            DateTime? resetsAt = limits.ResetsAt;

            string label = "quiet";
            if (primary.HasValue)
            {
                if (secondary.HasValue)
                {
                    label = string.Format(CultureInfo.InvariantCulture, "{0:0}% of your 5-hour limit · {1:0}% this week", primary.Value, secondary.Value);
                }
                else
                {
                    label = string.Format(CultureInfo.InvariantCulture, "{0:0}% of your 5-hour limit", primary.Value);
                }
            }

            return new AiUsageSnapshot(
                AiTool.Codex,
                primary,
                label,
                resetsAt,
                totalTokensInWindow,
                activeSessions
            );
        }

        internal static (long Tokens, DateTime? WindowStart, long Busiest) ClaudeWindows(IEnumerable<string> lines, DateTime nowUtc)
        {
            List<AssistantMessage> messages = new List<AssistantMessage>();

            foreach (string line in lines)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("type", out JsonElement typeElem) && typeElem.GetString() == "assistant")
                        {
                            if (root.TryGetProperty("timestamp", out JsonElement tsElem) && tsElem.TryGetDateTime(out DateTime ts))
                            {
                                string? messageId = null;
                                if (root.TryGetProperty("message", out JsonElement msgObj))
                                {
                                    if (msgObj.TryGetProperty("id", out JsonElement idElem))
                                    {
                                        messageId = idElem.GetString();
                                    }

                                    long inputTokens = 0;
                                    long cacheCreation = 0;
                                    long outputTokens = 0;

                                    if (msgObj.TryGetProperty("usage", out JsonElement usageObj))
                                    {
                                        if (usageObj.TryGetProperty("input_tokens", out JsonElement it))
                                        {
                                            inputTokens = it.GetInt64();
                                        }
                                        if (usageObj.TryGetProperty("cache_creation_input_tokens", out JsonElement cct))
                                        {
                                            cacheCreation = cct.GetInt64();
                                        }
                                        if (usageObj.TryGetProperty("output_tokens", out JsonElement ot))
                                        {
                                            outputTokens = ot.GetInt64();
                                        }
                                    }

                                    long total = inputTokens + cacheCreation + outputTokens;
                                    messages.Add(new AssistantMessage(messageId, ts, total));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip malformed
                }
            }

            // Deduplicate by message.id when present
            Dictionary<string, AssistantMessage> uniqueById = new Dictionary<string, AssistantMessage>();
            List<AssistantMessage> orderedMessages = new List<AssistantMessage>();

            foreach (var m in messages.OrderBy(m => m.Timestamp))
            {
                if (!string.IsNullOrEmpty(m.MessageId))
                {
                    uniqueById[m.MessageId] = m;
                }
                else
                {
                    orderedMessages.Add(m);
                }
            }

            var allMsgs = uniqueById.Values.Concat(orderedMessages).OrderBy(m => m.Timestamp).ToList();

            if (allMsgs.Count == 0)
            {
                return (0, null, 0);
            }

            List<WindowData> windows = new List<WindowData>();
            DateTime windowStart = RoundDownToHour(allMsgs[0].Timestamp);
            DateTime windowEnd = windowStart.AddHours(5);
            long currentWindowTokens = 0;

            foreach (var m in allMsgs)
            {
                if (m.Timestamp >= windowEnd)
                {
                    windows.Add(new WindowData(windowStart, currentWindowTokens));
                    windowStart = RoundDownToHour(m.Timestamp);
                    windowEnd = windowStart.AddHours(5);
                    currentWindowTokens = 0;
                }

                currentWindowTokens += m.TotalTokens;
            }

            windows.Add(new WindowData(windowStart, currentWindowTokens));

            long busiest = windows.Count > 0 ? windows.Max(w => w.Tokens) : 0;

            // Find current window containing nowUtc
            long tokensInCurrentWindow = 0;
            DateTime? activeWindowStart = null;

            foreach (var w in windows)
            {
                if (nowUtc >= w.Start && nowUtc < w.Start.AddHours(5))
                {
                    tokensInCurrentWindow = w.Tokens;
                    activeWindowStart = w.Start;
                }
            }

            return (tokensInCurrentWindow, activeWindowStart, busiest);
        }

        internal static string? ClaudeStep(IEnumerable<string> lines)
        {
            ParsedToolUse? latestToolUse = null;
            string? latestAssistantText = null;
            string? latestUserText = null;

            foreach (string line in lines)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("type", out JsonElement typeElem))
                        {
                            string? type = typeElem.GetString();
                            if (type == "assistant")
                            {
                                if (root.TryGetProperty("message", out JsonElement msgObj))
                                {
                                    if (msgObj.TryGetProperty("content", out JsonElement contentElem) && contentElem.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (JsonElement content in contentElem.EnumerateArray())
                                        {
                                            if (content.TryGetProperty("type", out JsonElement ct) && ct.GetString() == "tool_use")
                                            {
                                                string? name = content.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() : null;
                                                if (!string.IsNullOrEmpty(name))
                                                {
                                                    string? desc = null;
                                                    if (content.TryGetProperty("input", out JsonElement inputObj))
                                                    {
                                                        if (inputObj.TryGetProperty("description", out JsonElement descElem))
                                                        {
                                                            desc = descElem.GetString();
                                                        }
                                                        else if (inputObj.TryGetProperty("file_path", out JsonElement fpElem))
                                                        {
                                                            string? fp = fpElem.GetString();
                                                            if (!string.IsNullOrEmpty(fp))
                                                            {
                                                                desc = Path.GetFileName(fp);
                                                            }
                                                        }
                                                        else if (inputObj.TryGetProperty("command", out JsonElement cmdElem))
                                                        {
                                                            string? cmd = cmdElem.GetString();
                                                            if (!string.IsNullOrEmpty(cmd))
                                                            {
                                                                desc = cmd.Length > 40 ? cmd.Substring(0, 40) : cmd;
                                                            }
                                                        }
                                                    }
                                                    latestToolUse = new ParsedToolUse(name, desc);
                                                }
                                            }
                                        }
                                    }
                                    latestAssistantText = "Writing";
                                }
                            }
                            else if (type == "user")
                            {
                                latestUserText = "Thinking";
                            }
                        }
                    }
                }
                catch
                {
                    // Skip
                }
            }

            if (latestToolUse.HasValue)
            {
                var tu = latestToolUse.Value;
                string stepText = string.IsNullOrEmpty(tu.Description) ? tu.Name : tu.Name + ": " + tu.Description;
                if (stepText.Length > 60)
                {
                    stepText = stepText.Substring(0, 59) + "…";
                }
                return stepText;
            }

            return latestAssistantText ?? latestUserText;
        }

        internal static (double? Primary, double? Secondary, DateTime? ResetsAt) CodexLimits(IEnumerable<string> lines)
        {
            DateTime? newestTs = null;
            double? primary = null;
            double? secondary = null;
            long? resetsInSeconds = null;

            foreach (string line in lines)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        JsonElement root = doc.RootElement;
                        DateTime? lineTs = null;
                        if (root.TryGetProperty("timestamp", out JsonElement tsElem) && tsElem.TryGetDateTime(out DateTime ts))
                        {
                            lineTs = ts;
                        }

                        if (root.TryGetProperty("payload", out JsonElement payload))
                        {
                            JsonElement rateLimitsObj = default;
                            bool hasRateLimits = false;

                            if (payload.TryGetProperty("rate_limits", out rateLimitsObj))
                            {
                                hasRateLimits = true;
                            }
                            else if (payload.TryGetProperty("info", out JsonElement infoObj) && infoObj.TryGetProperty("rate_limits", out rateLimitsObj))
                            {
                                hasRateLimits = true;
                            }

                            if (hasRateLimits)
                            {
                                if (lineTs.HasValue && (!newestTs.HasValue || lineTs.Value > newestTs.Value))
                                {
                                    newestTs = lineTs.Value;
                                    ParseRateLimits(rateLimitsObj, out primary, out secondary, out resetsInSeconds);
                                }
                                else if (!newestTs.HasValue)
                                {
                                    ParseRateLimits(rateLimitsObj, out primary, out secondary, out resetsInSeconds);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip
                }
            }

            DateTime? resetsAt = null;
            if (newestTs.HasValue && resetsInSeconds.HasValue)
            {
                resetsAt = newestTs.Value.AddSeconds(resetsInSeconds.Value);
            }

            return (primary, secondary, resetsAt);
        }

        internal static string? CodexStep(IEnumerable<string> lines)
        {
            ParsedCodexFunctionCall? latestCall = null;

            foreach (string line in lines)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("type", out JsonElement typeElem) && typeElem.GetString() == "response_item")
                        {
                            if (root.TryGetProperty("payload", out JsonElement payload))
                            {
                                if (payload.TryGetProperty("type", out JsonElement pt) && pt.GetString() == "function_call")
                                {
                                    string? name = payload.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() : null;
                                    string? args = payload.TryGetProperty("arguments", out JsonElement argElem) ? argElem.GetString() : null;
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        latestCall = new ParsedCodexFunctionCall(name, args);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip
                }
            }

            if (latestCall.HasValue)
            {
                var call = latestCall.Value;
                string commandPart = ExtractCommandFromArguments(call.Arguments);
                string stepText = string.IsNullOrEmpty(commandPart) ? call.Name : call.Name + ": " + commandPart;
                if (stepText.Length > 60)
                {
                    stepText = stepText.Substring(0, 59) + "…";
                }
                return stepText;
            }

            return "Working";
        }

        private static List<string> ReadLastBytes(string filePath)
        {
            List<string> lines = new List<string>();
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = fs.Length;
                    long bufferSize = 256 * 1024;
                    long offset = length - bufferSize;
                    if (offset < 0)
                    {
                        offset = 0;
                    }

                    fs.Seek(offset, SeekOrigin.Begin);

                    using (StreamReader reader = new StreamReader(fs, System.Text.Encoding.UTF8))
                    {
                        string? text = reader.ReadToEnd();
                        string[] rawLines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                        bool skipFirst = offset > 0;
                        bool skipped = false;

                        foreach (string rawLine in rawLines)
                        {
                            if (skipFirst && !skipped)
                            {
                                skipped = true;
                                continue;
                            }
                            if (!string.IsNullOrEmpty(rawLine))
                            {
                                lines.Add(rawLine);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return empty if unreadable
            }
            return lines;
        }

        private static ParsedClaudeLine? ParseClaudeLine(string line)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(line))
                {
                    JsonElement root = doc.RootElement;
                    string? cwd = null;
                    if (root.TryGetProperty("cwd", out JsonElement cwdElem))
                    {
                        cwd = cwdElem.GetString();
                    }
                    return new ParsedClaudeLine(cwd, line);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetProjectName(string? cwd)
        {
            if (string.IsNullOrEmpty(cwd))
            {
                return "Unknown";
            }

            try
            {
                string cleaned = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                int lastIdx = cleaned.LastIndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                if (lastIdx >= 0 && lastIdx < cleaned.Length - 1)
                {
                    return cleaned.Substring(lastIdx + 1);
                }
                return cleaned;
            }
            catch
            {
                return "Unknown";
            }
        }

        private static DateTime RoundDownToHour(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind);
        }

        private static long ExtractTotalTokens(JsonElement payload)
        {
            try
            {
                JsonElement usageObj = default;
                bool found = false;

                if (payload.TryGetProperty("info", out JsonElement info) && info.TryGetProperty("total_token_usage", out usageObj))
                {
                    found = true;
                }
                else if (payload.TryGetProperty("total_token_usage", out usageObj))
                {
                    found = true;
                }

                if (found && usageObj.TryGetProperty("total_tokens", out JsonElement tt))
                {
                    return tt.GetInt64();
                }
            }
            catch
            {
                // Ignore
            }
            return 0;
        }

        private static void ParseRateLimits(JsonElement rateLimitsObj, out double? primary, out double? secondary, out long? resetsInSeconds)
        {
            primary = null;
            secondary = null;
            resetsInSeconds = null;

            try
            {
                if (rateLimitsObj.TryGetProperty("primary", out JsonElement primaryObj))
                {
                    if (primaryObj.TryGetProperty("used_percent", out JsonElement up))
                    {
                        primary = up.GetDouble();
                    }
                    if (primaryObj.TryGetProperty("resets_in_seconds", out JsonElement ris))
                    {
                        resetsInSeconds = ris.GetInt64();
                    }
                }

                if (rateLimitsObj.TryGetProperty("secondary", out JsonElement secondaryObj))
                {
                    if (secondaryObj.TryGetProperty("used_percent", out JsonElement sup))
                    {
                        secondary = sup.GetDouble();
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        private static string ExtractCommandFromArguments(string? argsJson)
        {
            if (string.IsNullOrEmpty(argsJson))
            {
                return string.Empty;
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    JsonElement root = doc.RootElement;
                    List<string> parts = new List<string>();

                    if (root.TryGetProperty("command", out JsonElement cmdElem))
                    {
                        if (cmdElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement el in cmdElem.EnumerateArray())
                            {
                                string? s = el.GetString();
                                if (!string.IsNullOrEmpty(s))
                                {
                                    parts.Add(s);
                                }
                            }
                        }
                        else if (cmdElem.ValueKind == JsonValueKind.String)
                        {
                            string? s = cmdElem.GetString();
                            if (!string.IsNullOrEmpty(s))
                            {
                                parts.Add(s);
                            }
                        }
                    }

                    if (parts.Count > 0)
                    {
                        string joined = string.Join(" ", parts);
                        return joined.Length > 40 ? joined.Substring(0, 40) : joined;
                    }
                }
            }
            catch
            {
                // Fallback
            }

            return argsJson.Length > 40 ? argsJson.Substring(0, 40) : argsJson;
        }

        private readonly struct ParsedClaudeLine
        {
            public string? Cwd { get; }
            public string RawLine { get; }

            public ParsedClaudeLine(string? cwd, string rawLine)
            {
                Cwd = cwd;
                RawLine = rawLine;
            }
        }

        private readonly struct AssistantMessage
        {
            public string? MessageId { get; }
            public DateTime Timestamp { get; }
            public long TotalTokens { get; }

            public AssistantMessage(string? messageId, DateTime timestamp, long totalTokens)
            {
                MessageId = messageId;
                Timestamp = timestamp;
                TotalTokens = totalTokens;
            }
        }

        private readonly struct WindowData
        {
            public DateTime Start { get; }
            public long Tokens { get; }

            public WindowData(DateTime start, long tokens)
            {
                Start = start;
                Tokens = tokens;
            }
        }

        private readonly struct ParsedToolUse
        {
            public string Name { get; }
            public string? Description { get; }

            public ParsedToolUse(string name, string? description)
            {
                Name = name;
                Description = description;
            }
        }

        private readonly struct ParsedCodexFunctionCall
        {
            public string Name { get; }
            public string? Arguments { get; }

            public ParsedCodexFunctionCall(string name, string? arguments)
            {
                Name = name;
                Arguments = arguments;
            }
        }
    }
}