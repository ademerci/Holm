using System.Text.Json;

namespace WinNotch.Tests;

public class CapsLockTests
{
    [Fact]
    public void Counts_a_press_once_however_long_it_is_held()
    {
        var key = new CapsKey();
        Assert.True(key.Pressed(0x14, down: true, up: false));
        Assert.False(key.Pressed(0x14, down: true, up: false)); // auto-repeat
        Assert.False(key.Pressed(0x14, down: true, up: false));
        Assert.False(key.Pressed(0x14, down: false, up: true));
        Assert.True(key.Pressed(0x14, down: true, up: false));  // the next press
    }

    [Theory]
    [InlineData(0x41u)] // A
    [InlineData(0x10u)] // Shift
    [InlineData(0x90u)] // Num Lock
    [InlineData(0x91u)] // Scroll Lock
    public void Ignores_every_other_key(uint vk)
    {
        var key = new CapsKey();
        Assert.False(key.Pressed(vk, down: true, up: false));
        Assert.False(key.Pressed(vk, down: false, up: true));
        Assert.True(key.Pressed(0x14, down: true, up: false)); // and they don't disturb Caps Lock
    }

    [Fact]
    public void Other_keys_while_held_do_not_end_the_press()
    {
        var key = new CapsKey();
        Assert.True(key.Pressed(0x14, true, false));
        Assert.False(key.Pressed(0x41, false, true));
        Assert.False(key.Pressed(0x14, true, false));
    }

    [Fact]
    public void Settings_from_before_the_caps_lock_hint_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"QuietHours\":true}")!;
        Assert.True(old.QuietHours);
        Assert.True(old.CapsLock);
    }
}
