using System.Security.Cryptography;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>DPAPI and the Windows account's name, as the WPF app keeps the friends key (the same settings open in both).</summary>
public sealed class WindowsIdentityPlatform : IIdentityPlatform
{
    public byte[] Protect(byte[] data, byte[] entropy) => ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data, byte[] entropy) => ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
    public string AccountName() => Native.DisplayName();
}
