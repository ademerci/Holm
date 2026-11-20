using System;

namespace WinNotch;

internal static class NotchPlacement
{
    internal const double WidthPadding = 2;

    internal static double FitScale(double scale, double shapeWidth, double availableWidth)
        => Math.Min(scale, Math.Max(1, availableWidth - WidthPadding) / shapeWidth);

    internal static int Left(int left, int right, int width, double? position)
    {
        double fraction = position is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
        int x = (int)Math.Round(left + (right - left) * fraction - width / 2.0);
        return Math.Clamp(x, left, Math.Max(left, right - width));
    }

    internal static double Fraction(int left, int right, int width, int x)
        => Math.Clamp((x + width / 2.0 - left) / Math.Max(1, right - left), 0, 1);
}
