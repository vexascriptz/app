using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SoftReset.Core.Storage;

namespace SoftReset.App.Services;

/// <summary>
/// Asks Windows for administrator rights as soon as the app opens (the normal UAC prompt), so
/// scans see everything and changes don't each need their own prompt. If the user says no, the
/// app opens normally with standard rights.
/// </summary>
public static class AdminStartup
{
    /// <summary>Added to the elevated copy's arguments so it never asks again (stops loops when UAC can't elevate).</summary>
    public const string RelaunchedFlag = "--relaunched-elevated";
    /// <summary>Opens the app without asking for administrator rights.</summary>
    public const string NoAdminFlag = "--no-admin";

    public enum Outcome { NotAsked, Relaunched, Declined, Failed }

    /// <summary>Whether to show the UAC prompt at start-up.</summary>
    public static bool ShouldAsk(string[] args, StartupOptions options, bool isWindows, bool isElevated, bool settingOn) =>
        isWindows && !isElevated && settingOn
        && !options.Demo && options.CaptureDirectory is null
        && !args.Contains(RelaunchedFlag) && !args.Contains(NoAdminFlag);

    /// <summary>The arguments for the elevated copy: same as now, with paths made absolute and the loop guard added.</summary>
    public static string BuildArguments(string[] args)
    {
        var list = args.ToList();
        var i = list.IndexOf("--data");
        if (i >= 0 && i + 1 < list.Count) list[i + 1] = Path.GetFullPath(list[i + 1]);
        list.Add(RelaunchedFlag);
        return string.Join(' ', list.Select(Quote));
    }

    /// <summary>Quotes one argument the way the Windows command-line parser reads it back.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>Reads the "ask for admin when the app opens" setting (on unless turned off in About).</summary>
    public static bool ReadSetting(StartupOptions options)
    {
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: options.DataDirectory);
            return (Json.ReadFile<AppSettings>(paths.SettingsFile) ?? new AppSettings()).AskForAdminOnStart;
        }
        catch { return true; }
    }

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>Shows the UAC prompt and starts an elevated copy. Relaunched means this copy should exit.</summary>
    public static Outcome TryRelaunch(string[] args, StartupOptions options)
    {
        if (!ShouldAsk(args, options, OperatingSystem.IsWindows(), IsElevated(), ReadSetting(options))) return Outcome.NotAsked;
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
            using var p = Process.Start(new ProcessStartInfo(exe, BuildArguments(args))
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,
            });
            return p is null ? Outcome.Failed : Outcome.Relaunched;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return Outcome.Declined; } // user clicked No
        catch { return Outcome.Failed; }
    }
}
