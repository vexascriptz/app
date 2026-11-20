namespace SoftReset.Core.Storage;

/// <summary>
/// Resolves where the console keeps its data. Portable-first: if the folder that
/// contains the executable is writable, data lives in "softreset-data" beside it
/// (ideal for a technician USB drive). Otherwise it falls back to
/// %LOCALAPPDATA%\softreset-console. Nothing is written anywhere else.
/// </summary>
public sealed class AppPaths
{
    public const string PortableFolderName = "softreset-data";
    public const string FallbackFolderName = "softreset-console";
    public const string MarkerFileName = ".softreset-data";

    public AppPaths(string root, bool isPortable)
    {
        Root = Path.GetFullPath(root);
        IsPortable = isPortable;
    }

    public string Root { get; }
    public bool IsPortable { get; }

    public string Sessions => Path.Combine(Root, "sessions");
    public string Journal => Path.Combine(Root, "journal");
    public string Reports => Path.Combine(Root, "reports");
    public string Scans => Path.Combine(Root, "scans");
    public string FileOperations => Path.Combine(Root, "file-operations");
    public string Backups => Path.Combine(Root, "backups");
    public string Temp => Path.Combine(Root, "temp");
    public string Logs => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public static AppPaths Resolve(string? executableDirectory = null, string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return new AppPaths(overrideRoot, isPortable: false);

        var exeDir = executableDirectory ?? AppContext.BaseDirectory;
        var portable = Path.Combine(exeDir, PortableFolderName);
        if (IsWritable(exeDir, portable))
            return new AppPaths(portable, isPortable: true);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
        return new AppPaths(Path.Combine(local, FallbackFolderName), isPortable: false);
    }

    public void EnsureCreated()
    {
        foreach (var d in new[] { Root, Sessions, Journal, Reports, Scans, FileOperations, Backups, Temp, Logs })
            Directory.CreateDirectory(d);
        var marker = Path.Combine(Root, MarkerFileName);
        if (!File.Exists(marker))
            File.WriteAllText(marker, "This folder was created by the softreset Technician Console. It contains only data created by the console and can be deleted safely.\n");
    }

    private static bool IsWritable(string exeDir, string candidate)
    {
        try
        {
            if (Directory.Exists(candidate))
            {
                var probe = Path.Combine(candidate, $".write-test-{Environment.ProcessId}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }

            if (!Directory.Exists(exeDir)) return false;
            // Never create a portable data folder inside Program Files or the Windows directory.
            var full = Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var protectedRoot in ProtectedRoots())
            {
                if (!string.IsNullOrEmpty(protectedRoot) &&
                    full.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var test = Path.Combine(exeDir, $".softreset-write-test-{Environment.ProcessId}");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> ProtectedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }
}
