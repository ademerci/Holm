namespace WinNotch.Tests;

public class BluetoothControlTests
{
    static readonly Guid Headphones = new("5A1B2C3D-0000-4000-8000-112233445566");
    static BtDevice Device => new() { Address = "A0B1C2D3E4F5", Name = "WH-1000XM4", Paired = true, ContainerId = Headphones };

    // What Core Audio reports as the filter behind a Bluetooth music and a hands-free (calls) endpoint.
    const string MusicFilter = @"{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_vid&0001054c_pid&0d58#8&2a3b4c5d&0&a0b1c2d3e4f5_c00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\a2dp";
    const string CallFilter = @"{2}.\\?\bthhfenum#bthhfpaudio#8&1f2e3d4c&0&97#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\hfpaudio";
    const string UsbFilter = @"{2}.\\?\usb#vid_046d&pid_0a87&mi_00#7&1234abcd&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global";

    [Fact]
    public void A_music_filter_belongs_to_the_device_by_its_address()
        => Assert.True(BluetoothControl.Belongs(MusicFilter, Guid.Empty, Device));

    [Fact]
    public void A_call_filter_belongs_to_the_device_by_its_container()
        => Assert.True(BluetoothControl.Belongs(CallFilter, Headphones, Device));

    [Fact]
    public void Another_devices_filters_do_not_belong()
    {
        var other = new BtDevice { Address = "112233445566", Name = "Speaker", Paired = true, ContainerId = Guid.NewGuid() };
        Assert.False(BluetoothControl.Belongs(MusicFilter, Headphones, other));
        Assert.False(BluetoothControl.Belongs(CallFilter, Headphones, other));
    }

    [Fact]
    public void An_unknown_container_matches_nothing()
        => Assert.False(BluetoothControl.Belongs(CallFilter, Guid.Empty, new BtDevice { Address = "112233445566", Name = "Speaker" }));

    [Theory]
    [InlineData(MusicFilter, true)]
    [InlineData(CallFilter, true)]
    [InlineData(UsbFilter, false)]
    public void Only_Bluetooth_filters_are_asked(string filterId, bool bluetooth)
        => Assert.Equal(bluetooth, BluetoothControl.IsBluetooth(filterId));

    /// <summary>Only reads; nothing connects or disconnects.</summary>
    [Fact, Trait("Category", "Local")]
    public void Lists_this_PCs_paired_devices_by_name()
    {
        var paired = BluetoothControl.Paired();
        Assert.All(paired, p => Assert.True(p.Device.Paired));
        Assert.All(paired, p => Assert.False(string.IsNullOrWhiteSpace(p.Device.Name)));
        Assert.Equal(paired.Count, paired.Select(p => p.Device.Address).Distinct().Count());
    }
}
