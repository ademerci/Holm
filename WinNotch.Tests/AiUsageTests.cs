using WinNotch;

namespace WinNotch.Tests;

public class AiUsageTests
{
    static string Claude(string time, string id, int input, int output, string content = "[]") =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{time}\",\"cwd\":\"C:\\\\work\\\\Holm\",\"message\":{{\"id\":\"{id}\",\"usage\":{{\"input_tokens\":{input},\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":99999,\"output_tokens\":{output}}},\"content\":{content}}}}}";

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
    public void Claude_step_names_the_latest_tool()
    {
        var lines = new[]
        {
            Claude("2026-11-15T09:20:00Z", "a", 1, 1, "[{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{\"file_path\":\"C:\\\\work\\\\Holm\\\\Gifs.cs\"}}]"),
            Claude("2026-11-15T09:21:00Z", "b", 1, 1, "[{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"dotnet test\",\"description\":\"Run the tests\"}}]"),
        };
        Assert.Equal("Bash: Run the tests", AiUsage.ClaudeStep(lines));
    }

    [Fact]
    public void Codex_limits_come_from_the_newest_token_count()
    {
        var lines = new[]
        {
            "{\"timestamp\":\"2026-11-15T09:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":10.0,\"resets_in_seconds\":100}}}}",
            "{\"timestamp\":\"2026-11-15T10:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":1200}},\"rate_limits\":{\"primary\":{\"used_percent\":42.0,\"window_minutes\":300,\"resets_in_seconds\":3600},\"secondary\":{\"used_percent\":12.5}}}}",
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
    public void Missing_folders_mean_nothing_to_show()
    {
        string nowhere = Path.Combine(Path.GetTempPath(), "holm-no-such-" + Guid.NewGuid().ToString("N"));
        Assert.Null(AiUsage.ReadClaudeCode(nowhere, DateTime.UtcNow));
        Assert.Null(AiUsage.ReadCodex(nowhere, DateTime.UtcNow));
    }
}
