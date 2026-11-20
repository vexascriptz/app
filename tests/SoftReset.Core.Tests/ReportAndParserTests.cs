using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SoftReset.Core.Apps;
using SoftReset.Core.Collection;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Parsing;
using SoftReset.Core.Platform;
using SoftReset.Core.Reports;
using SoftReset.Core.Safety;
using SoftReset.Core.Sessions;
using SoftReset.Core.Startup;
using SoftReset.Core.Storage;
using SoftReset.Platform.Simulated;

namespace SoftReset.Core.Tests;

public class PdfTests
{
    [Fact]
    public void Pdf_HasValidStructureAndXrefOffsets()
    {
        var doc = new PdfDocument { Compress = false, Title = "T" };
        doc.NewPage();
        doc.Text(50, 50, "Hello (world) \\ – “quoted” €", PdfFont.Bold, 12);
        doc.Rect(10, 10, 100, 50, PdfColor.FromHex("EAF4FF"), PdfColor.FromHex("0B2545"), radius: 8);
        doc.NewPage();
        doc.Line(0, 0, 100, 100, new PdfColor(0, 0, 0));
        var bytes = doc.ToBytes();
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 2", text);
        Assert.Contains("Hello \\(world\\) \\\\ \\226 \\223quoted\\224 \\200", text);
        AssertXrefValid(text);
    }

    internal static void AssertXrefValid(string text)
    {
        var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[startxref..]);
        var entries = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n");
        var n = 1;
        foreach (Match e in entries)
        {
            var off = int.Parse(e.Groups[1].Value);
            Assert.StartsWith($"{n} 0 obj", text[off..]);
            n++;
        }
        Assert.True(n > 5);
    }

    [Fact]
    public void Wrap_RespectsWidth_AndBreaksLongTokens()
    {
        var lines = PdfDocument.Wrap("The quick brown fox jumps over the lazy dog " + new string('W', 200), PdfFont.Regular, 10, 120);
        Assert.True(lines.Count > 3);
        Assert.All(lines, l => Assert.True(PdfDocument.Measure(l, PdfFont.Regular, 10) <= 120.01, l));
        Assert.Equal(PdfDocument.Measure("abc", PdfFont.Regular, 10), (556 + 556 + 500) / 100.0, 3);
    }

    [Fact]
    public async Task SessionReport_BuildsMultiPagePdfWithContent()
    {
        using var rig = new TestRig();
        var engine = new DiagnosticEngine();
        var baseline = engine.Analyze(await rig.ScanAsync());
        var r1 = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.RefreshRate, Parameters = { ["device"] = @"\\.\DISPLAY1", ["refresh"] = "165" } });
        var followUp = engine.Analyze(await rig.ScanAsync());
        var session = new CustomerSession { TechnicianName = "Alex", CustomerName = "Jordan", Concerns = { new CustomerConcern { Text = "Games stutter and the PC restarted twice" } } };
        session.Approval = new ApprovalRecord { ApprovedBy = "J." };
        var bytes = ReportBuilder.BuildSessionReport(new ReportInput { Session = session, Baseline = baseline, FollowUp = followUp, Changes = new[] { r1 } });
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        AssertXrefValid(text);
        var pages = int.Parse(Regex.Match(text, @"/Count (\d+)").Groups[1].Value);
        Assert.True(pages >= 2);
        var content = InflateAll(bytes);
        Assert.Contains("PC Service Report", content);
        Assert.Contains("Games stutter", content);
        Assert.Contains("Display refresh rate", content);
        Assert.Contains("Before and after", content);
        Assert.Contains("No passwords", content);
    }

    [Fact]
    public async Task DiagnosticReport_Pdf()
    {
        using var rig = new TestRig();
        var report = new DiagnosticEngine().Analyze(await rig.ScanAsync());
        var bytes = ReportBuilder.BuildDiagnosticReport(report);
        var content = InflateAll(bytes);
        Assert.Contains("Diagnostic Report", content);
        Assert.Contains("Data that could not be collected", content);
    }

    internal static string InflateAll(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(text, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n"))
        {
            var start = m.Index + m.Length;
            var len = int.Parse(m.Groups[1].Value);
            using var z = new ZLibStream(new MemoryStream(pdf, start, len), CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.Latin1);
            sb.Append(r.ReadToEnd());
        }
        return sb.ToString();
    }
}

public class ParserTests
{
    [Fact]
    public void Powercfg_List()
    {
        const string o = "Existing Power Schemes (* Active)\r\n-----------------------------------\r\nPower Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\r\nPower Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\r\n";
        var s = ToolOutputParsers.ParsePowercfgList(o);
        Assert.Equal(2, s.Count);
        Assert.True(s[0].Active);
        Assert.Equal("High performance", s[1].Name);
    }

    [Fact]
    public void NetshWlan_ParsesConnectedInterface()
    {
        const string o = "There is 1 interface on the system:\r\n\r\n    Name                   : Wi-Fi\r\n    State                  : connected\r\n    SSID                   : HomeNet\r\n    Radio type             : 802.11ax\r\n    Authentication         : WPA2-Personal\r\n    Band                   : 5 GHz\r\n    Channel                : 36\r\n    Receive rate (Mbps)    : 1201\r\n    Transmit rate (Mbps)   : 960.5\r\n    Signal                 : 38%\r\n";
        var w = ToolOutputParsers.ParseNetshWlanInterfaces(o, out var problem)!;
        Assert.Null(problem);
        Assert.Equal("HomeNet", w.Ssid);
        Assert.Equal(38, w.SignalPercent);
        Assert.Equal(960.5, w.TransmitRateMbps);
        Assert.Equal("5 GHz", w.Band);
    }

    [Fact]
    public void NetshWlan_ReportsLocationPermission()
    {
        Assert.Null(ToolOutputParsers.ParseNetshWlanInterfaces("Network shell commands need location permission to access WLAN information.", out var p));
        Assert.Contains("Location permission", p);
    }

    [Fact]
    public void NvidiaSmi_ParsesValuesAndNotSupported()
    {
        var r = ToolOutputParsers.ParseNvidiaSmi("NVIDIA GeForce RTX 3070, 566.36, 47, 3, 1, 1123, 8192, 28.51, 220.00, 30, 210, 405, P8, 4, 16\n");
        var (name, driver, t) = Assert.Single(r);
        Assert.Equal("NVIDIA GeForce RTX 3070", name);
        Assert.Equal("566.36", driver);
        Assert.Equal(47, t.TemperatureC);
        Assert.Equal("PCIe Gen4 x16", t.PcieLink);
        var na = ToolOutputParsers.ParseNvidiaSmi("Quadro, 1.0, 40, 1, 1, 1, 1, [N/A], [N/A], [Not Supported], 1, 1, P0, 3, 16");
        Assert.Null(na[0].Telemetry.PowerWatts);
        Assert.Null(na[0].Telemetry.FanPercent);
    }

    [Fact]
    public void Winget_ParsesTableWithSpinnerNoise()
    {
        var list = ToolOutputParsers.ParseWingetUpgrade("   - \r   \\ \r\nName           Id              Version  Available  Source\r\n-----------------------------------------------------------\r\nGoogle Chrome  Google.Chrome   120.0    121.0      winget\r\nVLC            VideoLAN.VLC    3.0.18   3.0.21     winget\r\n2 upgrades available.\r\n");
        Assert.Equal(2, list.Count);
        Assert.Equal("Google.Chrome", list[0].Id);
        Assert.Equal("3.0.21", list[1].Available);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --min", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe /background", "C:\\Tools\\tool.exe")]
    [InlineData("C:\\Program Files\\No Quotes\\x.exe -a", "C:\\Program Files\\No Quotes\\x.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    public void CommandLine_ExtractExecutable(string cmd, string expected) => Assert.Equal(expected, CommandLine.ExtractExecutable(cmd));

    [Fact]
    public void BugCheck_And_UpdateErrors()
    {
        Assert.Equal(0x116u, BugCheckCodes.ParseFromText("The bugcheck was: 0x00000116 (0xffff...)"));
        Assert.Equal(0x116u, BugCheckCodes.ParseNumber("278"));
        Assert.Equal(0x116u, BugCheckCodes.ParseNumber("0x116"));
        Assert.Contains("VIDEO_TDR_FAILURE", BugCheckCodes.Describe(0x116));
        Assert.Contains("COMPONENT_STORE", WindowsUpdateErrors.Describe(WindowsUpdateErrors.Parse("error 0x80073712")!.Value));
    }

    [Fact]
    public void Hosts_IgnoresCommentsAndLocalhost_FlagsNotable()
    {
        var (count, notable) = RegistryReaders.AnalyzeHosts(new[] { "# comment", "127.0.0.1 localhost", "0.0.0.0 ads.example.com", "0.0.0.0 update.microsoft.com # block", "" });
        Assert.Equal(2, count);
        Assert.Single(notable);
    }

    [Fact]
    public void ProcessHeuristics_FlagsMimicsAndLookalikes_NotNormalApps()
    {
        using var rig = new TestRig();
        var f = rig.Platform.Folders;
        Assert.NotEmpty(ProcessHeuristics.Evaluate("svchost.exe", Path.Combine(f.AppDataRoaming, "svchost.exe"), SignatureState.Unsigned, f));
        Assert.Contains(ProcessHeuristics.Evaluate("scvhost.exe", Path.Combine(f.ProgramFiles, "x", "scvhost.exe"), SignatureState.Signed, f), r => r.Contains("one character"));
        Assert.Empty(ProcessHeuristics.Evaluate("Discord.exe", Path.Combine(f.AppDataLocal, "Discord", "Discord.exe"), SignatureState.Signed, f));
        Assert.Empty(ProcessHeuristics.Evaluate("svchost.exe", Path.Combine(f.System32, "svchost.exe"), SignatureState.SignedMicrosoftWindows, f));
    }

    [Fact]
    public void CpuSampler_ComputesPercentOfTotalCapacity()
    {
        var sampler = new ProcessCpuSampler(4);
        var t0 = DateTime.UtcNow;
        sampler.Sample(new[] { new ProcessInfo { Pid = 1, Name = "a", TotalProcessorTime = TimeSpan.Zero } }, t0);
        var r = sampler.Sample(new[] { new ProcessInfo { Pid = 1, Name = "a", TotalProcessorTime = TimeSpan.FromSeconds(2) } }, t0.AddSeconds(1));
        Assert.Equal(50, r[1], 1);
    }

    [Fact]
    public void WindowedOptimizations_EditsOnlyItsKey()
    {
        var s = Optimization.Actions.WindowedOptimizationsAction.SetFlag("VRROptimizeEnable=0;", "SwapEffectUpgradeEnable", "1");
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", s);
        Assert.Equal("1", Optimization.Actions.WindowedOptimizationsAction.GetFlag(s, "SwapEffectUpgradeEnable"));
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;", Optimization.Actions.WindowedOptimizationsAction.SetFlag(s, "SwapEffectUpgradeEnable", "0"));
    }
}

public class InventoryAndStorageTests
{
    [Fact]
    public void StartupApproved_OddFirstByteMeansDisabled()
    {
        var reg = new InMemoryRegistry();
        using var rig = new TestRig();
        var inv = new StartupInventory(reg, rig.Platform.Folders);
        Assert.True(inv.IsApproved(RegHive.CurrentUser, "Run", "x"));
        reg.SetValue(RegHive.CurrentUser, $@"{StartupInventory.ApprovedRoot}\Run", "x", new RegValue(RegKind.Binary, new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.False(inv.IsApproved(RegHive.CurrentUser, "Run", "x"));
        reg.SetValue(RegHive.CurrentUser, $@"{StartupInventory.ApprovedRoot}\Run", "x", new RegValue(RegKind.Binary, new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.True(inv.IsApproved(RegHive.CurrentUser, "Run", "x"));
        Assert.Equal((RegHive.LocalMachine, "Run32", "Some:Name"), StartupInventory.ParseApprovedTarget("reg:HKLM:Run32:Some:Name"));
        var disabled = StartupInventory.BuildApprovedValue(false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(3, disabled[0]);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), BitConverter.ToInt64(disabled, 4));
    }

    [Fact]
    public void UninstallReader_FiltersAndDetectsMsi()
    {
        var reg = new InMemoryRegistry();
        const string root = UninstallRegistryReader.UninstallPath;
        reg.Set(RegHive.LocalMachine, $@"{root}\App", "DisplayName", "Visible App");
        reg.Set(RegHive.LocalMachine, $@"{root}\App", "EstimatedSize", 2048);
        reg.Set(RegHive.LocalMachine, $@"{root}\Hidden", "DisplayName", "Hidden Component");
        reg.Set(RegHive.LocalMachine, $@"{root}\Hidden", "SystemComponent", 1);
        reg.Set(RegHive.LocalMachine, $@"{root}\Patch", "DisplayName", "Patch");
        reg.Set(RegHive.LocalMachine, $@"{root}\Patch", "ParentKeyName", "App");
        reg.Set(RegHive.LocalMachine, $@"{root}\{{11111111-2222-3333-4444-555555555555}}", "DisplayName", "MSI App");
        reg.Set(RegHive.LocalMachine, $@"{root}\{{11111111-2222-3333-4444-555555555555}}", "WindowsInstaller", 1);
        reg.Set(RegHive.LocalMachine, $@"{root}\NoName", "Publisher", "x");
        var apps = new UninstallRegistryReader(reg).Read();
        Assert.Equal(2, apps.Count);
        Assert.Equal(2048 * 1024, apps.Single(a => a.Name == "Visible App").EstimatedSizeBytes);
        var msi = apps.Single(a => a.Name == "MSI App");
        Assert.True(msi.IsMsi);
        Assert.False(msi.NoRepair);
        Assert.Equal("{11111111-2222-3333-4444-555555555555}", msi.MsiProductCode);
    }

    [Fact]
    public void AppPaths_PortableWhenWritable_FallbackOtherwise()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr-paths-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var p = AppPaths.Resolve(dir);
            Assert.True(p.IsPortable);
            Assert.Equal(Path.Combine(dir, AppPaths.PortableFolderName), p.Root);
            p.EnsureCreated();
            Assert.True(File.Exists(Path.Combine(p.Root, AppPaths.MarkerFileName)));
            var fallback = AppPaths.Resolve(Path.Combine(dir, "does-not-exist"));
            Assert.False(fallback.IsPortable);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Json_AtomicWriteAndRead()
    {
        var f = Path.Combine(Path.GetTempPath(), "sr-json-" + Guid.NewGuid().ToString("N") + ".json");
        Json.WriteFile(f, new ChangeRecord { Title = "a", Status = ChangeStatus.Verified });
        Json.WriteFile(f, new ChangeRecord { Title = "b", Status = ChangeStatus.Verified });
        var r = Json.ReadFile<ChangeRecord>(f)!;
        Assert.Equal("b", r.Title);
        Assert.Contains("\"Verified\"", File.ReadAllText(f));
        Assert.False(File.Exists(f + ".tmp"));
        File.Delete(f);
    }

    [Fact]
    public void RegistryBackup_RoundTripsAllKinds()
    {
        var reg = new InMemoryRegistry();
        reg.SetValue(RegHive.CurrentUser, "k", "dw", RegValue.DWord(5));
        reg.SetValue(RegHive.CurrentUser, "k", "bin", new RegValue(RegKind.Binary, new byte[] { 1, 2, 3 }));
        reg.SetValue(RegHive.CurrentUser, "k", "multi", new RegValue(RegKind.MultiString, new[] { "a", "b" }));
        reg.SetValue(RegHive.CurrentUser, "k", "q", new RegValue(RegKind.QWord, 1L << 40));
        var items = new[] { "dw", "bin", "multi", "q", "missing" }.Select(n => RegistryBackup.Capture(reg, RegHive.CurrentUser, "k", n)).ToList();
        var before = reg.Dump();
        foreach (var n in new[] { "dw", "bin", "multi", "q" }) reg.SetValue(RegHive.CurrentUser, "k", n, RegValue.Str("changed"));
        reg.SetValue(RegHive.CurrentUser, "k", "missing", RegValue.DWord(1));
        foreach (var i in items) RegistryBackup.Restore(reg, i);
        Assert.All(items, i => Assert.True(RegistryBackup.Matches(reg, i)));
        Assert.Equal(before["CurrentUser\\k"].OrderBy(x => x.Key), reg.Dump()["CurrentUser\\k"].OrderBy(x => x.Key));
    }
}

public class SessionTests
{
    [Fact]
    public async Task Session_LifecyclePersistsAndLoads()
    {
        using var rig = new TestRig();
        var svc = new SessionService(rig.Paths);
        var s = svc.Start("Alex", "Jordan", "T-100", "Gaming PC", "DESKTOP-1");
        s.Concerns.Add(new CustomerConcern { Text = "Slow startup" });
        s.Advance(SessionStage.ConcernsRecorded);
        var report = new DiagnosticEngine().Analyze(await rig.ScanAsync());
        s.Baseline = svc.SaveScan(s, report, "baseline");
        s.Advance(SessionStage.BaselineCaptured);
        svc.Save(s);

        var loaded = svc.Load(s.Id)!;
        Assert.Equal("Jordan", loaded.CustomerName);
        Assert.Equal(SessionStage.BaselineCaptured, loaded.Stage);
        var scan = svc.LoadScan(loaded.Baseline)!;
        Assert.Equal(report.Findings.Count, scan.Findings.Count);
        loaded.Advance(SessionStage.ConcernsRecorded); // never moves backwards
        Assert.Equal(SessionStage.BaselineCaptured, loaded.Stage);
        svc.Close(loaded);
        Assert.True(svc.Load(s.Id)!.IsClosed);
        Assert.Single(svc.List());
        svc.Delete(s.Id);
        Assert.Empty(svc.List());
    }

    [Theory]
    [InlineData("password: hunter2", true)]
    [InlineData("PIN=1234", true)]
    [InlineData("card 4111 1111 1111 1111", true)]
    [InlineData("PC is slow after update", false)]
    [InlineData("forgot their password, reset via Microsoft account", false)]
    public void SensitiveText(string text, bool flagged) => Assert.Equal(flagged, SensitiveTextDetector.Check(text) is not null);
}
