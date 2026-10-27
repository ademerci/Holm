using System;

namespace WinNotch;

/// <summary>The note's rules: how long it may be, and the line the idle notch shows of it.</summary>
internal static class Notepad
{
    public const int MaxLength = 4000;

    public static string Clamp(string text) => text.Length > MaxLength ? text[..MaxLength] : text;

    /// <summary>The first line with something on it, shortened for a preview; "" when the note is empty.</summary>
    public static string Preview(string? note, int max = 80)
    {
        foreach (string line in (note ?? "").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed.Length > max ? trimmed[..max].TrimEnd() + "…" : trimmed;
        }
        return "";
    }
}
