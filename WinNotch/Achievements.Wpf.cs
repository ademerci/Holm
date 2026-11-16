using System.Windows.Media;

namespace WinNotch;

/// <summary>The WPF side of an achievement: its colour as a Color. The rest is in Achievements.cs.</summary>
internal sealed partial record Achievement
{
    public Color Color => Color.FromArgb((byte)(Argb >> 24), (byte)(Argb >> 16), (byte)(Argb >> 8), (byte)Argb);
}
