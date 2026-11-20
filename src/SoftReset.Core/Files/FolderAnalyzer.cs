using System.Security.Cryptography;

namespace SoftReset.Core.Files;

public enum FileCategory
{
    Documents, Pdfs, Spreadsheets, Presentations, Images, RawPhotos, Videos, Audio, Archives, Installers,
    DiskImages, Code, Shortcuts, Fonts, Ebooks, Design, Models3D, Torrents, Other,
}

public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var d = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, params string[] exts) { foreach (var e in exts) d["." + e] = c; }
        Add(FileCategory.Documents, "doc", "docx", "odt", "rtf", "txt", "md", "pages", "wpd", "log", "tex");
        Add(FileCategory.Pdfs, "pdf", "xps", "oxps");
        Add(FileCategory.Spreadsheets, "xls", "xlsx", "xlsm", "ods", "csv", "numbers", "tsv");
        Add(FileCategory.Presentations, "ppt", "pptx", "odp", "key", "pps", "ppsx");
        Add(FileCategory.Images, "jpg", "jpeg", "png", "gif", "bmp", "webp", "heic", "heif", "tif", "tiff", "svg", "ico", "avif", "jfif");
        Add(FileCategory.RawPhotos, "cr2", "cr3", "nef", "arw", "dng", "orf", "rw2", "raf", "srw", "pef");
        Add(FileCategory.Videos, "mp4", "mkv", "mov", "avi", "wmv", "webm", "m4v", "flv", "mpg", "mpeg", "3gp", "mts", "m2ts");
        Add(FileCategory.Audio, "mp3", "wav", "flac", "aac", "m4a", "ogg", "wma", "opus", "aiff", "mid", "midi");
        Add(FileCategory.Archives, "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "zst", "tgz", "cab");
        Add(FileCategory.Installers, "exe", "msi", "msix", "msixbundle", "appx", "appxbundle", "msp", "bat", "cmd");
        Add(FileCategory.DiskImages, "iso", "img", "vhd", "vhdx", "vmdk", "dmg", "wim", "esd");
        Add(FileCategory.Code, "cs", "js", "ts", "py", "java", "c", "cpp", "h", "hpp", "go", "rs", "rb", "php", "html", "htm", "css", "json", "xml", "yml", "yaml", "ps1", "sh", "sql", "ipynb", "kt", "swift", "lua");
        Add(FileCategory.Shortcuts, "lnk", "url", "website");
        Add(FileCategory.Fonts, "ttf", "otf", "woff", "woff2", "fon");
        Add(FileCategory.Ebooks, "epub", "mobi", "azw", "azw3", "djvu", "cbz", "cbr");
        Add(FileCategory.Design, "psd", "ai", "indd", "xd", "fig", "sketch", "afdesign", "afphoto", "kra", "xcf", "eps");
        Add(FileCategory.Models3D, "stl", "obj", "fbx", "blend", "3mf", "gcode", "step", "stp", "dwg", "dxf");
        Add(FileCategory.Torrents, "torrent");
        return d;
    }

    public static FileCategory Categorize(string path) =>
        Map.TryGetValue(Path.GetExtension(path), out var c) ? c : FileCategory.Other;

    public static string FolderName(FileCategory c) => c switch
    {
        FileCategory.Pdfs => "PDFs",
        FileCategory.RawPhotos => "Raw photos",
        FileCategory.DiskImages => "Disk images",
        FileCategory.Models3D => "3D and CAD",
        FileCategory.Ebooks => "eBooks",
        _ => c.ToString(),
    };
}

public sealed class ScannedFile
{
    public string Path { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public FileCategory Category { get; set; }
    public bool IsCloudPlaceholder { get; set; }
    public bool IsTopLevel { get; set; }
}

public sealed class CategorySummary
{
    public FileCategory Category { get; set; }
    public int Count { get; set; }
    public long Bytes { get; set; }
}

public sealed class FolderScanOptions
{
    public bool Recursive { get; set; } = true;
    public int MaxFiles { get; set; } = 250_000;
    public bool IncludeHidden { get; set; }
    public HashSet<string> ExcludedDirectoryNames { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "__pycache__", ".venv", "venv", "$RECYCLE.BIN", "System Volume Information",
        FileOperationExecutor.ReviewFolderName,
    };
}

public sealed class FolderAnalysis
{
    public string Root { get; set; } = "";
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.Now;
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public long TotalBytes { get; set; }
    public List<CategorySummary> Categories { get; set; } = new();
    public List<ScannedFile> LargestFiles { get; set; } = new();
    public List<string> EmptyFolders { get; set; } = new();
    public int StaleFileCount { get; set; }
    public long StaleFileBytes { get; set; }
    public int SkippedCloudFiles { get; set; }
    public int SkippedHiddenOrSystem { get; set; }
    public int SkippedErrors { get; set; }
    public bool Truncated { get; set; }
    public List<ScannedFile> Files { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

/// <summary>Read-only folder analysis. Never opens cloud-only placeholder files (which would trigger a download).</summary>
public sealed class FolderAnalyzer
{
    // Windows file attribute bits not exposed by FileAttributes on all platforms.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static bool IsCloudPlaceholder(FileAttributes a) =>
        (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    public FolderAnalysis Analyze(string root, FolderScanOptions? options = null, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        options ??= new FolderScanOptions();
        root = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var a = new FolderAnalysis { Root = root };
        var stack = new Stack<string>();
        stack.Push(root);
        var cats = new Dictionary<FileCategory, CategorySummary>();
        var staleCutoff = DateTime.UtcNow.AddYears(-1);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch { a.SkippedErrors++; continue; }

            var any = false;
            foreach (var e in entries)
            {
                any = true;
                FileAttributes attrs;
                try { attrs = e.Attributes; } catch { a.SkippedErrors++; continue; }
                if ((attrs & FileAttributes.ReparsePoint) != 0 && e is DirectoryInfo) { a.SkippedErrors++; continue; }
                var hidden = (attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                if (hidden && !options.IncludeHidden) { a.SkippedHiddenOrSystem++; continue; }

                if (e is DirectoryInfo sub)
                {
                    a.FolderCount++;
                    if (options.Recursive && !options.ExcludedDirectoryNames.Contains(sub.Name)) stack.Push(sub.FullName);
                    continue;
                }
                if (e is not FileInfo f) continue;
                if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
                if (a.FileCount >= options.MaxFiles) { a.Truncated = true; break; }

                var placeholder = IsCloudPlaceholder(attrs);
                if (placeholder) a.SkippedCloudFiles++;
                long size;
                try { size = f.Length; } catch { a.SkippedErrors++; continue; }
                var sf = new ScannedFile
                {
                    Path = f.FullName,
                    RelativePath = System.IO.Path.GetRelativePath(root, f.FullName),
                    Size = size,
                    ModifiedUtc = f.LastWriteTimeUtc,
                    CreatedUtc = f.CreationTimeUtc,
                    Category = FileCategories.Categorize(f.Name),
                    IsCloudPlaceholder = placeholder,
                    IsTopLevel = string.Equals(System.IO.Path.GetDirectoryName(f.FullName), root, StringComparison.OrdinalIgnoreCase),
                };
                a.Files.Add(sf);
                a.FileCount++;
                a.TotalBytes += size;
                if (!cats.TryGetValue(sf.Category, out var cs)) cats[sf.Category] = cs = new CategorySummary { Category = sf.Category };
                cs.Count++;
                cs.Bytes += size;
                if (sf.ModifiedUtc < staleCutoff) { a.StaleFileCount++; a.StaleFileBytes += size; }
                if (a.FileCount % 500 == 0) progress?.Report(a.FileCount);
            }
            if (!any && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) a.EmptyFolders.Add(dir);
            if (a.Truncated) break;
        }

        a.Categories = cats.Values.OrderByDescending(c => c.Bytes).ToList();
        a.LargestFiles = a.Files.OrderByDescending(f => f.Size).Take(50).ToList();
        a.Suggestions = BuildSuggestions(a);
        progress?.Report(a.FileCount);
        return a;
    }

    private static List<string> BuildSuggestions(FolderAnalysis a)
    {
        var s = new List<string>();
        string B(long b) => Parsing.ToolOutputParsers.FormatBytes(b);
        var top = a.Files.Where(f => f.IsTopLevel).ToList();
        var installers = a.Categories.FirstOrDefault(c => c.Category == FileCategory.Installers);
        if (installers is { Count: >= 5 })
            s.Add($"{installers.Count} installer files use {B(installers.Bytes)}. Installers are rarely needed after use; review them with the customer before removing.");
        var images = a.Categories.FirstOrDefault(c => c.Category is FileCategory.Images);
        if (images is { Count: >= 30 } && !a.Root.Contains("Pictures", StringComparison.OrdinalIgnoreCase))
            s.Add($"{images.Count} images ({B(images.Bytes)}) could be organised into the Pictures library.");
        var disk = a.Categories.FirstOrDefault(c => c.Category == FileCategory.DiskImages);
        if (disk is { Bytes: > 2L * 1024 * 1024 * 1024 })
            s.Add($"Disk images (ISO/VHD) use {B(disk.Bytes)}. These are often one-time downloads.");
        if (top.Count >= 40)
            s.Add($"{top.Count} files sit directly in this folder. Sorting them into category subfolders makes them easier to find.");
        if (a.StaleFileCount >= 50)
            s.Add($"{a.StaleFileCount} files ({B(a.StaleFileBytes)}) have not been modified in over a year — candidates for archiving to external storage.");
        if (a.EmptyFolders.Count >= 5)
            s.Add($"{a.EmptyFolders.Count} empty folders were found.");
        if (a.SkippedCloudFiles > 0)
            s.Add($"{a.SkippedCloudFiles} cloud-only files (OneDrive/Dropbox placeholders) were not opened or hashed, so they are excluded from duplicate checks and moves.");
        return s;
    }
}

public sealed class DuplicateGroup
{
    public long Size { get; set; }
    public string Hash { get; set; } = "";
    public List<ScannedFile> Files { get; set; } = new();
    public long WastedBytes => Size * (Files.Count - 1);

    /// <summary>Keep the oldest copy (likely the original); ties go to the shortest path.</summary>
    public ScannedFile SuggestedKeep => Files.OrderBy(f => f.CreatedUtc).ThenBy(f => f.Path.Length).First();
}

/// <summary>Finds byte-identical files: size → partial SHA-256 → full SHA-256.</summary>
public sealed class DuplicateFinder
{
    private const int PartialBytes = 64 * 1024;

    public List<DuplicateGroup> Find(IEnumerable<ScannedFile> files, long minSize = 1, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var groups = new List<DuplicateGroup>();
        var bySize = files.Where(f => !f.IsCloudPlaceholder && f.Size >= minSize).GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
        var processed = 0;
        foreach (var sizeGroup in bySize)
        {
            ct.ThrowIfCancellationRequested();
            var partial = sizeGroup.GroupBy(f => SafeHash(f.Path, sizeGroup.Key <= PartialBytes ? -1 : PartialBytes)).Where(g => g.Key is not null && g.Count() > 1);
            foreach (var pg in partial)
            {
                IEnumerable<IGrouping<string?, ScannedFile>> full = sizeGroup.Key <= PartialBytes
                    ? new[] { pg }
                    : pg.GroupBy(f => SafeHash(f.Path, -1)).Where(g => g.Key is not null && g.Count() > 1);
                foreach (var fg in full)
                    groups.Add(new DuplicateGroup { Size = sizeGroup.Key, Hash = fg.Key!, Files = fg.OrderBy(f => f.Path).ToList() });
            }
            processed += sizeGroup.Count();
            progress?.Report(processed);
        }
        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static string? SafeHash(string path, int maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            if (maxBytes < 0) return Convert.ToHexString(SHA256.HashData(fs));
            var buf = new byte[maxBytes];
            var read = fs.ReadAtLeast(buf, maxBytes, throwOnEndOfStream: false);
            return Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, read)));
        }
        catch
        {
            return null;
        }
    }
}
