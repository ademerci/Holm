using WinNotch;

namespace Holm.App;

/// <summary>
/// Which design Holm runs in, read once at start (the WPF app's Look): the classic look, or the instrument look from
/// Settings › General (black, white ink, small corners, tools as words). Switching applies at the next start.
/// </summary>
internal static class Look
{
    public static bool Instrument { get; set; } = AppSettings.Load().InstrumentLook;

    /// <summary>The instrument value or the classic one.</summary>
    public static T Pick<T>(T instrument, T classic) => Instrument ? instrument : classic;
}
