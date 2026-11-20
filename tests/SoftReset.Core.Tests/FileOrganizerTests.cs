using SoftReset.Core.Files;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Tests;

public class FileOrganizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "softreset-files-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _logs;

    public FileOrganizerTests()
    {
        Directory.CreateDirectory(_root);
        _logs = Path.Combine(_root, "_logs_outside");
        Directory.CreateDirectory(_logs);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Make(string rel, string content, int ageDays = 30)
    {
        var p = Path.Combine(_root, "scan", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-ageDays));
        File.SetCreationTimeUtc(p, DateTime.UtcNow.AddDays(-ageDays));
        return p;
    }

    private string ScanRoot => Path.Combine(_root, "scan");

    [Fact]
    public void Analyze_CategorizesAndSummarises()
    {
        Make("a.pdf", "pdf");
        Make("b.jpg", "img1");
        Make("c.jpg", "img2");
        Make("setup.exe", "exe");
        Make("sub/notes.txt", "txt");
        Directory.CreateDirectory(Path.Combine(ScanRoot, "empty"));
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        Assert.Equal(5, a.FileCount);
        Assert.Equal(2, a.Categories.Single(c => c.Category == FileCategory.Images).Count);
        Assert.Single(a.EmptyFolders);
        Assert.Equal(4, a.Files.Count(f => f.IsTopLevel));
    }

    [Fact]
    public void Duplicates_FoundByContentNotName()
    {
        Make("invoice.pdf", new string('x', 5000));
        Make("invoice (1).pdf", new string('x', 5000));
        Make("other/renamed.pdf", new string('x', 5000));
        Make("different.pdf", new string('y', 5000));
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var groups = new DuplicateFinder().Find(a.Files);
        var g = Assert.Single(groups);
        Assert.Equal(3, g.Files.Count);
        Assert.Equal(10000, g.WastedBytes);
    }

    [Fact]
    public void Duplicates_LargeFilesUseFullHash()
    {
        var big1 = new string('a', 200_000) + "END1";
        var big2 = new string('a', 200_000) + "END2"; // same first 64 KB, different tail
        Make("x.bin", big1);
        Make("y.bin", big2);
        Make("z.bin", big1);
        var groups = new DuplicateFinder().Find(new FolderAnalyzer().Analyze(ScanRoot).Files);
        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
    }

    [Fact]
    public void Plan_ByCategory_SkipsRecentAndShortcuts_AndResolvesConflicts()
    {
        Make("a.pdf", "1");
        Make("b.jpg", "2");
        Make("recent.docx", "3", ageDays: 0);
        Make("link.lnk", "4");
        Make("PDFs/a.pdf", "existing");
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var plan = new OrganizationPlanner().Plan(a, new OrganizePlanOptions());
        Assert.Equal(2, plan.Moves.Count);
        var pdfMove = plan.Moves.Single(m => m.Source.EndsWith("a.pdf") && !m.Source.Contains("PDFs"));
        Assert.True(pdfMove.RenamedToAvoidConflict);
        Assert.EndsWith(Path.Combine("PDFs", "a (1).pdf"), pdfMove.Destination);
        Assert.Contains(plan.Skipped, s => s.Contains("recent.docx"));
        Assert.Contains(plan.Skipped, s => s.Contains("link.lnk"));
        // Planning never touches the disk.
        Assert.True(File.Exists(Path.Combine(ScanRoot, "a.pdf")));
    }

    [Fact]
    public async Task Execute_ThenUndo_RestoresEveryFile()
    {
        var files = new[] { Make("a.pdf", "1"), Make("b.jpg", "2"), Make("c.mp3", "3"), Make("d.zip", "4") };
        var originalHashes = files.ToDictionary(f => f, File.ReadAllText);
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var plan = new OrganizationPlanner().Plan(a, new OrganizePlanOptions());
        var exec = new FileOperationExecutor(_logs);
        var log = await exec.ExecuteAsync(plan, "organize", "test");
        Assert.Equal("Completed", log.Status);
        Assert.Equal(4, log.Done);
        Assert.All(files, f => Assert.False(File.Exists(f)));
        Assert.True(File.Exists(Path.Combine(ScanRoot, "Images", "b.jpg")));

        var undone = await exec.UndoAsync(log.Id);
        Assert.Equal("Undone", undone.Status);
        foreach (var (path, content) in originalHashes) Assert.Equal(content, File.ReadAllText(path));
        // Folders created by the operation were removed again.
        Assert.False(Directory.Exists(Path.Combine(ScanRoot, "Images")));
    }

    [Fact]
    public async Task Execute_NeverOverwrites()
    {
        var src = Make("a.pdf", "new");
        var plan = new OrganizationPlan { Root = ScanRoot, Moves = { new PlannedMove { Source = src, Destination = Path.Combine(ScanRoot, "taken.pdf") } } };
        File.WriteAllText(Path.Combine(ScanRoot, "taken.pdf"), "precious");
        var log = await new FileOperationExecutor(_logs).ExecuteAsync(plan, "organize", "t");
        Assert.Equal(FileOpState.Failed, log.Entries.Single().State);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(ScanRoot, "taken.pdf")));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task Undo_RefusesToOverwriteNewFileAtOriginalLocation()
    {
        var src = Make("a.pdf", "1");
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var exec = new FileOperationExecutor(_logs);
        var log = await exec.ExecuteAsync(new OrganizationPlanner().Plan(a, new OrganizePlanOptions()), "organize", "t");
        File.WriteAllText(src, "customer created a new file here");
        var undone = await exec.UndoAsync(log.Id);
        Assert.Equal("Partially undone", undone.Status);
        Assert.Equal("customer created a new file here", File.ReadAllText(src));
    }

    [Fact]
    public async Task DuplicateReview_MovesCopiesToReviewFolder_KeepsOriginal()
    {
        var keep = Make("invoice.pdf", new string('x', 999), ageDays: 100);
        var copy = Make("invoice (1).pdf", new string('x', 999), ageDays: 10);
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var groups = new DuplicateFinder().Find(a.Files);
        var plan = OrganizationPlanner.PlanDuplicateReview(ScanRoot, groups);
        var move = Assert.Single(plan.Moves);
        Assert.Equal(copy, move.Source);
        Assert.Contains(FileOperationExecutor.ReviewFolderName, move.Destination);
        var log = await new FileOperationExecutor(_logs).ExecuteAsync(plan, "duplicates", "dupes");
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(copy));
        // The review folder is excluded from later scans.
        Assert.DoesNotContain(new FolderAnalyzer().Analyze(ScanRoot).Files, f => f.Path.Contains(FileOperationExecutor.ReviewFolderName));
        Assert.Equal(1, log.Done);
    }

    [Fact]
    public void CloudPlaceholderAttributes_AreDetected()
    {
        Assert.True(FolderAnalyzer.IsCloudPlaceholder(FileAttributes.Offline));
        Assert.True(FolderAnalyzer.IsCloudPlaceholder((FileAttributes)0x00400000));
        Assert.True(FolderAnalyzer.IsCloudPlaceholder((FileAttributes)0x00040000 | FileAttributes.Archive));
        Assert.False(FolderAnalyzer.IsCloudPlaceholder(FileAttributes.Archive | FileAttributes.ReadOnly));
    }

    [Fact]
    public void Plan_SkipsCloudPlaceholders()
    {
        var a = new FolderAnalysis { Root = ScanRoot, Files = { new ScannedFile { Path = Path.Combine(ScanRoot, "x.pdf"), RelativePath = "x.pdf", IsTopLevel = true, IsCloudPlaceholder = true, Category = FileCategory.Pdfs, ModifiedUtc = DateTime.UtcNow.AddDays(-5) } } };
        var plan = new OrganizationPlanner().Plan(a, new OrganizePlanOptions());
        Assert.Empty(plan.Moves);
        Assert.Contains(plan.Skipped, s => s.Contains("cloud-only"));
        Assert.Empty(new DuplicateFinder().Find(a.Files.Concat(a.Files)));
    }

    [Fact]
    public void ProtectedFolders_AreRefused()
    {
        using var rig = new TestRig();
        var f = rig.Platform.Folders;
        Assert.True(ProtectedComponents.IsProtectedFolder(f.WindowsDir, f));
        Assert.True(ProtectedComponents.IsProtectedFolder(Path.Combine(f.ProgramFiles, "App"), f));
        Assert.True(ProtectedComponents.IsProtectedFolder(f.UserProfile, f));
        Assert.True(ProtectedComponents.IsProtectedFolder(f.AppDataLocal, f));
        Assert.False(ProtectedComponents.IsProtectedFolder(f.Downloads, f));
        Assert.False(ProtectedComponents.IsProtectedFolder(f.Desktop, f));
    }

    [Fact]
    public void Plan_ByYearMonth()
    {
        Make("a.pdf", "1", ageDays: 400);
        var a = new FolderAnalyzer().Analyze(ScanRoot);
        var plan = new OrganizationPlanner().Plan(a, new OrganizePlanOptions { Strategy = OrganizeStrategy.ByYearMonth });
        var m = Assert.Single(plan.Moves);
        var expectedYear = DateTime.UtcNow.AddDays(-400).ToLocalTime().ToString("yyyy");
        Assert.Contains(Path.DirectorySeparatorChar + expectedYear + Path.DirectorySeparatorChar, m.Destination);
    }
}
