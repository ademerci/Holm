using System;
using System.Diagnostics;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>The key sealed as on Linux; the name is the account's full name ("id -F").</summary>
public sealed class MacIdentityPlatform : UnixIdentityPlatform
{
    public MacIdentityPlatform(string folder) : base(folder) { }

    public override string AccountName()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/id", "-F") { RedirectStandardOutput = true, UseShellExecute = false });
            string name = process?.StandardOutput.ReadToEnd().Trim() ?? "";
            if (name.Length > 0) return name;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        return Environment.UserName;
    }
}
