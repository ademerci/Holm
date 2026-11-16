using System;
using System.IO;

namespace Holm.Platform;

/// <summary>Whether Holm starts when the user signs in (Windows: the Run key; Linux: XDG autostart; macOS: a LaunchAgent).</summary>
public interface IAutostartPlatform
{
    /// <summary>False where the switch may not be touched (the port next to an installed Holm on Windows).</summary>
    bool CanChange { get; }
    bool IsEnabled { get; }
    void Set(bool enabled);
}

/// <summary>For systems without a backend yet: never starts by itself.</summary>
public sealed class NoAutostartPlatform : IAutostartPlatform
{
    public bool CanChange => false;
    public bool IsEnabled => false;
    public void Set(bool enabled) { }
}

/// <summary>How to start this very app again: the executable, and the app's file when it runs through the dotnet host.</summary>
public static class AppCommand
{
    public static string[] Current()
    {
        string process = Environment.ProcessPath ?? "";
        string name = Path.GetFileNameWithoutExtension(process);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
            return new[] { process, dll };
        return new[] { process };
    }
}
