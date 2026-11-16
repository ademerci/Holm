namespace WinNotch;

/// <summary>Caps Lock going down, once per press: holding it repeats the key, which must not count again.</summary>
internal sealed class CapsKey
{
    public const int VirtualKey = 0x14; // VK_CAPITAL

    bool _down;

    public bool Pressed(uint vk, bool down, bool up)
    {
        if (vk != VirtualKey) return false;
        if (up) { _down = false; return false; }
        if (!down || _down) return false;
        _down = true;
        return true;
    }
}
