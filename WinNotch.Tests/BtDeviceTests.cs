namespace WinNotch.Tests;

public class BtDeviceTests
{
    const string Headphones = "", Mouse = "", Keyboard = "", Controller = "", Pen = "";

    /// <summary>Bluetooth LE devices carry no class of device, so the name decides.</summary>
    [Theory]
    [InlineData("MX Keys", Keyboard)]
    [InlineData("MX Keys Mini", Keyboard)]
    [InlineData("Logitech Keyboard K380", Keyboard)]
    [InlineData("Clavier Apple", Keyboard)]
    [InlineData("MX Master 3S", Mouse)]
    [InlineData("Magic Trackpad", Mouse)]
    [InlineData("WH-1000XM5", Headphones)]
    [InlineData("Xbox Wireless Controller", Controller)]
    [InlineData("Surface Pen", Pen)]
    public void Devices_without_a_class_get_their_icon_from_the_name(string name, string glyph)
        => Assert.Equal(glyph, new BtDevice { Name = name }.Glyph);

    /// <summary>Peripheral (major class 0x05): bits 6-7 say keyboard, pointing device or both; bits 2-5 the subclass.</summary>
    [Theory]
    [InlineData(0x0540u, Keyboard)]   // keyboard
    [InlineData(0x0580u, Mouse)]      // pointing device
    [InlineData(0x05C0u, Keyboard)]   // keyboard with a touchpad
    [InlineData(0x0508u, Controller)] // gamepad
    [InlineData(0x0504u, Controller)] // joystick
    [InlineData(0x0404u, Headphones)] // audio / video
    public void Classic_devices_get_their_icon_from_the_class(uint classOfDevice, string glyph)
        => Assert.Equal(glyph, new BtDevice { Name = "Device", ClassOfDevice = classOfDevice }.Glyph);
}
