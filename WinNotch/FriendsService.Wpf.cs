using System.Security.Cryptography;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The WPF side: the service's work for the window runs on its Dispatcher, the identity's key is kept with
/// Windows' DPAPI and the name is the Windows account's. The rest is in FriendsService.cs.
/// </summary>
internal sealed partial class FriendsService
{
    static FriendsService()
    {
        ProtectKey = (data, entropy) => ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
        UnprotectKey = (data, entropy) => ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
        AccountName = Native.DisplayName;
    }

    public FriendsService(AppSettings settings, Dispatcher ui) : this(settings, action => ui.InvokeAsync(action).Task) { }
}
