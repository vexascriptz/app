using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SoftReset.Core.Platform;
using SoftReset.Platform.Windows.Native;

namespace SoftReset.Platform.Windows;

internal sealed class WindowsRegistry : IRegistryAccess
{
    private static RegistryKey Base(RegHive hive, RegView view)
    {
        var v = view switch { RegView.Registry32 => RegistryView.Registry32, RegView.Registry64 => RegistryView.Registry64, _ => RegistryView.Default };
        var h = hive switch
        {
            RegHive.CurrentUser => RegistryHive.CurrentUser,
            RegHive.LocalMachine => RegistryHive.LocalMachine,
            RegHive.ClassesRoot => RegistryHive.ClassesRoot,
            _ => RegistryHive.Users,
        };
        return RegistryKey.OpenBaseKey(h, v);
    }

    public bool KeyExists(RegHive hive, string path, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.OpenSubKey(path, false);
        return k is not null;
    }

    public RegValue? GetValue(RegHive hive, string path, string name, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.OpenSubKey(path, false);
        if (k is null) return null;
        var data = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (data is null) return null;
        var kind = k.GetValueKind(name) switch
        {
            RegistryValueKind.String => RegKind.String,
            RegistryValueKind.ExpandString => RegKind.ExpandString,
            RegistryValueKind.DWord => RegKind.DWord,
            RegistryValueKind.QWord => RegKind.QWord,
            RegistryValueKind.Binary => RegKind.Binary,
            RegistryValueKind.MultiString => RegKind.MultiString,
            _ => RegKind.None,
        };
        return new RegValue(kind, data);
    }

    public void SetValue(RegHive hive, string path, string name, RegValue value, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.CreateSubKey(path, writable: true) ?? throw new UnauthorizedAccessException($"Cannot open {path} for writing.");
        var kind = value.Kind switch
        {
            RegKind.String => RegistryValueKind.String,
            RegKind.ExpandString => RegistryValueKind.ExpandString,
            RegKind.DWord => RegistryValueKind.DWord,
            RegKind.QWord => RegistryValueKind.QWord,
            RegKind.Binary => RegistryValueKind.Binary,
            RegKind.MultiString => RegistryValueKind.MultiString,
            _ => RegistryValueKind.Unknown,
        };
        k.SetValue(name, value.Data ?? "", kind);
    }

    public void DeleteValue(RegHive hive, string path, string name, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.OpenSubKey(path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.OpenSubKey(path, false);
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path, RegView view = RegView.Default)
    {
        using var b = Base(hive, view);
        using var k = b.OpenSubKey(path, false);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }
}

internal sealed class WindowsKnownFolders : IKnownFolders
{
    private static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f, Environment.SpecialFolderOption.DoNotVerify);

    public string UserProfile => F(Environment.SpecialFolder.UserProfile);
    public string Desktop => F(Environment.SpecialFolder.DesktopDirectory);
    public string Documents => F(Environment.SpecialFolder.MyDocuments);
    public string Pictures => F(Environment.SpecialFolder.MyPictures);
    public string Videos => F(Environment.SpecialFolder.MyVideos);
    public string Music => F(Environment.SpecialFolder.MyMusic);
    public string AppDataRoaming => F(Environment.SpecialFolder.ApplicationData);
    public string AppDataLocal => F(Environment.SpecialFolder.LocalApplicationData);
    public string Temp => Path.GetTempPath().TrimEnd('\\');
    public string WindowsDir => F(Environment.SpecialFolder.Windows);
    public string System32 => Environment.SystemDirectory;
    public string ProgramFiles => F(Environment.SpecialFolder.ProgramFiles);
    public string ProgramFilesX86 => F(Environment.SpecialFolder.ProgramFilesX86);
    public string ProgramData => F(Environment.SpecialFolder.CommonApplicationData);
    public string StartupUser => F(Environment.SpecialFolder.Startup);
    public string StartupCommon => F(Environment.SpecialFolder.CommonStartup);

    public string Downloads
    {
        get
        {
            try
            {
                if (Win32.SHGetKnownFolderPath(Win32.FOLDERID_Downloads, 0, IntPtr.Zero, out var p) == 0)
                {
                    var s = Marshal.PtrToStringUni(p) ?? "";
                    Marshal.FreeCoTaskMem(p);
                    if (s.Length > 0) return s;
                }
            }
            catch { }
            return Path.Combine(UserProfile, "Downloads");
        }
    }
}

/// <summary>Small WMI helper with timeouts; failures return an empty list and a reason.</summary>
internal static class Wmi
{
    public static List<ManagementBaseObject> Query(string scope, string query, out string? error, int timeoutSeconds = 30)
    {
        error = null;
        var list = new List<ManagementBaseObject>();
        try
        {
            var ms = new ManagementScope(scope, new ConnectionOptions { Timeout = TimeSpan.FromSeconds(timeoutSeconds) });
            ms.Connect();
            using var searcher = new ManagementObjectSearcher(ms, new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(timeoutSeconds), ReturnImmediately = true, Rewindable = false });
            foreach (var o in searcher.Get()) list.Add(o);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            error = "Access denied (requires administrator).";
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied (requires administrator).";
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotFound)
        {
            error = "Not available on this PC.";
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        return list;
    }

    public static List<ManagementBaseObject> Query(string query) => Query(@"root\cimv2", query, out _);

    public static string? S(this ManagementBaseObject o, string p) { try { return o[p]?.ToString(); } catch { return null; } }
    public static long? L(this ManagementBaseObject o, string p) { try { return o[p] is { } v ? Convert.ToInt64(v) : null; } catch { return null; } }
    public static int? I(this ManagementBaseObject o, string p) { try { return o[p] is { } v ? Convert.ToInt32(v) : null; } catch { return null; } }
    public static bool? B(this ManagementBaseObject o, string p) { try { return o[p] is { } v ? Convert.ToBoolean(v) : null; } catch { return null; } }

    public static DateTimeOffset? D(this ManagementBaseObject o, string p)
    {
        try { return o[p] is string s && s.Length >= 14 ? new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(s)) : null; }
        catch { return null; }
    }
}
