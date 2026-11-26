using System;
using System.Runtime.InteropServices;

namespace WinNotch;

/// <summary>Whether Holm runs as the Microsoft Store package: then the Store updates it and Windows starts it.</summary>
internal static class PackageIdentity
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    public static bool IsPackaged { get; } = HasPackage();

    static bool HasPackage()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            int length = 0;
            // ERROR_INSUFFICIENT_BUFFER (122): there is a package name, only no room was given for it.
            return GetCurrentPackageFullName(ref length, null) == 122;
        }
        catch { return false; }
    }
}
