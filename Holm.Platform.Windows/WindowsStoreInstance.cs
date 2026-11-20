using System;
using System.ComponentModel;
using System.Diagnostics;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>Detects the Store copy, whose MSIX data path can isolate its lock file from the portable app.</summary>
public static class WindowsStoreInstance
{
    const string StorePackageFamily = "HolmTeam.HolmforWindows_2jmhnt2pkv2gj";

    public static bool IsRunning()
    {
        using var current = Process.GetCurrentProcess();
        int session = current.SessionId;
        var candidates = Process.GetProcessesByName("Holm");
        try
        {
            foreach (var process in candidates)
            {
                try
                {
                    if (process.Id != current.Id && process.SessionId == session
                        && Native.ProcessHasPackageFamily(process.Id, StorePackageFamily))
                        return true;
                }
                catch (InvalidOperationException) { } // exited during the check
                catch (Win32Exception) { } // inaccessible process
            }
            return false;
        }
        finally
        {
            foreach (var process in candidates) process.Dispose();
        }
    }
}
