using System.Globalization;
using WinNotch;

namespace WinNotch.Tests;

public class AiUsageTests
{
    const string RunTests = "[{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"dotnet test\",\"description\":\"Run the tests\"}}]";

    static string Claude(string time, string id, int input, int output, string content = "[]", string? stop = null) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{time}\",\"cwd\":\"C:\\\\work\\\\Holm\",\"message\":{{\"id\":\"{id}\","
        + (stop == null ? "" : $"\"stop_reason\":\"{stop}\",")
        + $"\"usage\":{{\"input_tokens\":{input},\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":99999,\"output_tokens\":{output}}},\"content\":{content}}}}}";

    static string CodexLimits(string time, double used, int resetsIn) =>
        $"{{\"timestamp\":\"{time}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"total_tokens\":1200}}}},"
        + $"\"rate_limits\":{{\"primary\":{{\"used_percent\":{used.ToString("0.0", CultureInfo.InvariantCulture)},\"window_minutes\":300,\"resets_in_seconds\":{resetsIn}}},\"secondary\":{{\"used_percent\":12.5,\"resets_in_seconds\":400000}}}}}}}}";

    static string Stamp(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static void WithTemp(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "holm-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void Claude_tokens_are_counted_per_five_hour_window_once_per_message()
    {
        var lines = new[]
        {
            Claude("2026-11-15T09:20:00Z", "a", 100, 50),
            Claude("2026-11-15T09:20:01Z", "a", 100, 50), // the same message logged again
            Claude("2026-11-15T10:00:00Z", "b", 200, 100),
            Claude("2026-11-15T16:30:00Z", "c", 1000, 0), // after 14:00: a new window from 16:00
            "not json",
        };
        var (tokens, start, busiest) = AiUsage.ClaudeWindows(lines, new DateTime(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1000, tokens); // cache reads don't count
        Assert.Equal(new DateTime(2026, 11, 15, 16, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(1000, busiest);

        var (earlier, earlierStart, _) = AiUsage.ClaudeWindows(lines, new DateTime(2026, 11, 15, 11, 0, 0, DateTimeKind.Utc));
        Assert.Equal(450, earlier);
        Assert.Equal(new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc), earlierStart);
    }

    [Fact]
    public void A_message_logged_block_by_block_counts_with_its_last_usage()
    {
        var lines = new[] { Claude("2026-11-15T09:20:00Z", "a", 100, 1), Claude("2026-11-15T09:20:02Z", "a", 100, 400) };
        Assert.Equal(500, AiUsage.ClaudeWindows(lines, new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)).Tokens);
    }

    [Fact]
    public void Claude_step_names_the_latest_tool()
    {
        var lines = new[]
        {
            Claude("2026-11-15T09:20:00Z", "a", 1, 1, "[{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{\"file_path\":\"C:\\\\work\\\\Holm\\\\Gifs.cs\"}}]"),
            Claude("2026-11-15T09:21:00Z", "b", 1, 1, RunTests),
        };
        Assert.Equal("Edit: Gifs.cs", AiUsage.ClaudeStep(lines.Take(1)));
        Assert.Equal("Bash: Run the tests", AiUsage.ClaudeStep(lines));
        Assert.Equal("Writing", AiUsage.ClaudeStep(new[] { Claude("2026-11-15T09:20:00Z", "a", 1, 1) }));
        Assert.Equal("update_plan", AiUsage.ClaudeStep(new[] { Claude("2026-11-15T09:20:00Z", "a", 1, 1, "[{\"type\":\"tool_use\",\"name\":\"mcp__ccd_turn__update_plan\",\"input\":{}}]") }));
    }

    [Fact]
    public void Codex_limits_come_from_the_newest_token_count()
    {
        var lines = new[]
        {
            "{\"timestamp\":\"2026-11-15T09:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":10.0,\"resets_in_seconds\":100}}}}",
            CodexLimits("2026-11-15T10:00:00Z", 42, 3600),
        };
        var (primary, secondary, resets) = AiUsage.CodexLimits(lines);
        Assert.Equal(42.0, primary);
        Assert.Equal(12.5, secondary);
        Assert.Equal(new DateTime(2026, 11, 15, 11, 0, 0, DateTimeKind.Utc), resets?.ToUniversalTime());
    }

    [Fact]
    public void Codex_step_names_the_latest_command()
    {
        var lines = new[]
        {
            "{\"timestamp\":\"2026-11-15T10:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell\",\"arguments\":\"{\\\"command\\\":[\\\"bash\\\",\\\"-lc\\\",\\\"npm test\\\"]}\"}}",
        };
        Assert.Equal("shell: bash -lc npm test", AiUsage.CodexStep(lines));
    }

    [Fact]
    public void A_reader_only_reads_what_was_added_and_waits_for_whole_lines()
        => WithTemp(dir =>
        {
            var now = DateTime.UtcNow;
            string log = Path.Combine(Directory.CreateDirectory(Path.Combine(dir, "projects", "C--work-Holm")).FullName, "session.jsonl");
            File.WriteAllText(log, Claude(Stamp(now.AddMinutes(-3)), "a", 100, 50) + "\n" + Claude(Stamp(now.AddMinutes(-3)), "b", 200, 100) + "\r\n");
            var reader = new AiUsageReader(AiTool.ClaudeCode, dir);
            Assert.Equal(450, reader.Read(now)!.TokensInWindow);

            // One more message, and half of the next: only the whole one counts.
            string half = Claude(Stamp(now.AddMinutes(-2)), "d", 4000, 0);
            File.AppendAllText(log, Claude(Stamp(now.AddMinutes(-2)), "c", 1000, 0) + "\n" + half[..40]);
            Assert.Equal(1450, reader.Read(now)!.TokensInWindow);
            File.AppendAllText(log, half[40..] + "\n");
            var usage = reader.Read(now)!;
            Assert.Equal(5450, usage.TokensInWindow);
            Assert.Equal(0.3, usage.Percent); // of the 2 M a quiet week is measured against
            Assert.False(usage.IsLimit);
            Assert.True(usage.ResetsAt > now && usage.ResetsAt <= now.AddHours(5));
        });

    [Fact]
    public void A_session_works_until_its_turn_ends()
        => WithTemp(dir =>
        {
            var now = DateTime.UtcNow;
            var project = Directory.CreateDirectory(Path.Combine(dir, "projects", "C--work-Holm"));
            string log = Path.Combine(project.FullName, "session.jsonl");
            File.WriteAllText(log, Claude(Stamp(now.AddMinutes(-1)), "a", 10, 10, RunTests, "tool_use") + "\n");
            // A helper agent's log adds its tokens, but isn't a session of its own.
            string helper = Path.Combine(Directory.CreateDirectory(Path.Combine(project.FullName, "session", "subagents")).FullName, "agent-1.jsonl");
            File.WriteAllText(helper, Claude(Stamp(now.AddMinutes(-1)), "h", 500, 0, RunTests) + "\n");

            var reader = new AiUsageReader(AiTool.ClaudeCode, dir);
            var usage = reader.Read(now)!;
            var session = Assert.Single(usage.Active);
            Assert.Equal("Holm", session.Project);
            Assert.Equal("Bash: Run the tests", session.Step);
            Assert.Equal(520, usage.TokensInWindow);

            // The tool's result comes back: the agent thinks on. Then it answers and stops.
            File.AppendAllText(log, "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\"}]}}\n");
            Assert.Single(reader.Read(now)!.Active);
            File.AppendAllText(log, Claude(Stamp(now), "b", 10, 10, "[]", "end_turn") + "\n");
            Assert.Empty(reader.Read(now)!.Active);
        });

    [Fact]
    public void A_long_log_is_read_from_near_its_end()
        => WithTemp(dir =>
        {
            var now = DateTime.UtcNow;
            string log = Path.Combine(Directory.CreateDirectory(Path.Combine(dir, "projects", "p")).FullName, "long.jsonl");
            using (var file = new StreamWriter(log))
            {
                file.Write(Claude(Stamp(now.AddMinutes(-3)), "old", 7000, 0) + "\n");
                // More than the reader goes back: a pasted file in a user's line, as in real logs.
                file.Write("{\"type\":\"user\",\"message\":{\"content\":\"" + new string('x', (int)AiUsageReader.FirstRead + 1000) + "\"}}\n");
                file.Write(Claude(Stamp(now.AddMinutes(-2)), "new", 300, 0) + "\n");
            }
            Assert.Equal(300, new AiUsageReader(AiTool.ClaudeCode, dir).Read(now)!.TokensInWindow);
        });

    [Fact]
    public void Codex_starts_empty_once_its_window_has_run_out()
        => WithTemp(dir =>
        {
            var now = DateTime.UtcNow;
            string log = Path.Combine(Directory.CreateDirectory(Path.Combine(dir, "sessions", "2026", "11", "15")).FullName, "rollout-1.jsonl");
            string meta = "{\"timestamp\":\"" + Stamp(now.AddMinutes(-20)) + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"1\",\"cwd\":\"C:\\\\work\\\\Holm\"}}";
            string call = "{\"timestamp\":\"" + Stamp(now) + "\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell\",\"arguments\":\"{\\\"command\\\":\\\"npm test\\\"}\"}}";
            File.WriteAllText(log, meta + "\n" + CodexLimits(Stamp(now.AddMinutes(-10)), 42, 3600) + "\n" + call + "\n");
            var usage = new AiUsageReader(AiTool.Codex, dir).Read(now)!;
            Assert.True(usage.IsLimit);
            Assert.Equal(42.0, usage.Percent);
            Assert.Equal(12.5, usage.WeekPercent);
            var session = Assert.Single(usage.Active);
            Assert.Equal(("Holm", "shell: npm test"), (session.Project, session.Step));

            File.WriteAllText(log, CodexLimits(Stamp(now.AddMinutes(-10)), 42, 60) + "\n{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n"); // ran out nine minutes ago
            var later = new AiUsageReader(AiTool.Codex, dir).Read(now)!;
            Assert.Equal(0, later.Percent);
            Assert.Null(later.ResetsAt);
            Assert.Equal(12.5, later.WeekPercent);
            Assert.Empty(later.Active);
        });

    [Fact]
    public void Missing_folders_mean_nothing_to_show()
    {
        string nowhere = Path.Combine(Path.GetTempPath(), "holm-no-such-" + Guid.NewGuid().ToString("N"));
        Assert.Null(AiUsage.ReadClaudeCode(nowhere, DateTime.UtcNow));
        Assert.Null(AiUsage.ReadCodex(nowhere, DateTime.UtcNow));
        WithTemp(empty => Assert.Null(AiUsage.ReadClaudeCode(empty, DateTime.UtcNow)));
    }
}
