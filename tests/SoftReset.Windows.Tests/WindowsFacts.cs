using System.Runtime.CompilerServices;

namespace SoftReset.Windows.Tests;

/// <summary>Runs only on Windows; reported as skipped elsewhere so results never claim Windows coverage that did not happen.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute([CallerMemberName] string? _ = null)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows — not run on this machine.";
    }
}

/// <summary>
/// Tests that really change (and then restore) Windows settings. They run only on Windows and only when
/// SOFTRESET_ALLOW_SYSTEM_CHANGES=1 (set in CI on disposable runners), never by accident on a customer PC.
/// </summary>
public sealed class WindowsChangeFactAttribute : FactAttribute
{
    public WindowsChangeFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows — not run on this machine.";
        else if (Environment.GetEnvironmentVariable("SOFTRESET_ALLOW_SYSTEM_CHANGES") != "1")
            Skip = "Changes real Windows settings; set SOFTRESET_ALLOW_SYSTEM_CHANGES=1 to run (CI does).";
    }
}
