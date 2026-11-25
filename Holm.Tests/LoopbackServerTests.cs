using WinNotch.Account;

namespace WinNotch.Tests;

/// <summary>An inbox on this computer is a test's: Holm never keeps one in your settings or your account.</summary>
public class LoopbackServerTests
{
    [Theory]
    [InlineData("http://localhost:45461", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("http://[::1]:9000", true)]
    [InlineData("https://relay.holmnotch.ch", false)]
    [InlineData("https://ntfy.sh", false)]
    [InlineData(null, false)]
    public void Only_inboxes_on_this_computer_count_as_a_tests(string? url, bool loopback)
        => Assert.Equal(loopback, FriendsService.IsLoopbackServer(url));

    [Fact]
    public void The_tests_never_write_to_the_real_settings_folder()
    {
        Assert.Contains("HolmTests", Diag.Folder);
        Assert.DoesNotContain(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), Diag.Folder);
    }
}
