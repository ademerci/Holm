using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace WinNotch.Tests;

/// <summary>
/// Before any test: Holm's data folder is a folder of the test run's own, so whatever a test saves (settings, chats,
/// the account) never lands in the person's real Holm folder. Test classes run in parallel: switching saving off per
/// class let one class's end switch it back on while another class's FriendsService saved, and test identities (with
/// an inbox on localhost) landed in the real settings.
/// </summary>
static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        Diag.Folder = Path.Combine(Path.GetTempPath(), "HolmTests", Environment.ProcessId.ToString());
        FriendsService.AllowLoopbackServers = true;
    }
}
