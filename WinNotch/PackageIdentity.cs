using System.Runtime.InteropServices;

namespace WinNotch;

internal static class PackageIdentity
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    public static bool IsPackaged { get; } = HasPackage();
    static bool HasPackage()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, null) == 122;
    }
}
