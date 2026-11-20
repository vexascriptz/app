using SoftReset.Core.Knowledge;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Tests;

public class KnowledgeBaseTests
{
    private static readonly KnowledgeBase Kb = KnowledgeBase.Default;

    public static readonly string[] RequiredCategories =
    {
        "System", "Display", "Graphics", "Sound", "Notifications", "Power", "Storage", "Multitasking", "Bluetooth", "Network",
        "Personalization", "Applications", "Startup", "Privacy", "Security", "Windows Update", "Accessibility", "Gaming", "Accounts",
        "Devices", "Printers", "Taskbar", "File Explorer", "Advanced system properties", "Services", "Scheduled tasks",
        "Registry-backed preferences", "Group Policy", "Legacy Control Panel", "Administrative tools",
    };

    private static readonly string[] AllowedUriSchemes = { "ms-settings:", "windowsdefender:", "https:", "ms-availablenetworks:" };
    private static readonly string[] RestartValues = { "none", "app", "explorer", "signout", "restart" };

    [Fact]
    public void Settings_CoverEveryRequiredCategory()
    {
        var cats = Kb.Settings.Select(s => s.Category).ToHashSet();
        foreach (var c in RequiredCategories) Assert.Contains(c, cats);
        Assert.True(Kb.Settings.Count >= 200, $"Only {Kb.Settings.Count} settings");
        Assert.All(Kb.Settings, s => Assert.Contains(s.Category, RequiredCategories));
    }

    [Fact]
    public void Settings_HaveAllElevenFields()
    {
        foreach (var s in Kb.Settings)
        {
            var ctx = s.Id;
            Assert.False(string.IsNullOrWhiteSpace(s.Name), ctx);
            Assert.False(string.IsNullOrWhiteSpace(s.Description), ctx);
            Assert.NotNull(s.Read);
            Assert.NotEmpty(s.Values);
            Assert.NotEmpty(s.Recommendations);
            Assert.All(s.Recommendations, r => Assert.False(string.IsNullOrWhiteSpace(r.UseCase + r.Value + r.Why), ctx));
            Assert.False(string.IsNullOrWhiteSpace(s.Performance), ctx);
            Assert.False(string.IsNullOrWhiteSpace(s.Risks), ctx);
            Assert.Contains(s.Restart, RestartValues);
            Assert.False(string.IsNullOrWhiteSpace(s.Rollback), ctx);
            Assert.NotEmpty(s.Controls);
        }
    }

    [Fact]
    public void Settings_IdsAreUnique()
    {
        var dupes = Kb.Settings.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void Settings_ReadSpecsAreComplete()
    {
        foreach (var s in Kb.Settings)
        {
            switch (s.Read.Type)
            {
                case "registry":
                    Assert.NotNull(s.Read.Path);
                    Assert.NotNull(s.Read.Value);
                    RegistryBackup.ParseHive(s.Read.Hive!);
                    Assert.False(s.Read.Path!.StartsWith('\\'), s.Id);
                    break;
                case "probe": Assert.False(string.IsNullOrWhiteSpace(s.Read.Probe), s.Id); break;
                case "service": Assert.False(string.IsNullOrWhiteSpace(s.Read.Service), s.Id); break;
                case "task": Assert.StartsWith("\\", s.Read.Path); break;
                case "none": break;
                default: Assert.Fail($"{s.Id}: unknown read type {s.Read.Type}"); break;
            }
        }
    }

    [Fact]
    public void Settings_ControlsAreSafeAndResolvable()
    {
        var registry = ActionRegistry.CreateDefault();
        foreach (var s in Kb.Settings)
        foreach (var c in s.Controls)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label), s.Id);
            switch (c.Type)
            {
                case "open":
                    Assert.False(string.IsNullOrWhiteSpace(c.Target), s.Id);
                    if (c.Launch == LaunchKind.Uri) Assert.Contains(AllowedUriSchemes, p => c.Target!.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    if (c.Launch == LaunchKind.SystemTool) Assert.Matches(@"^[A-Za-z0-9_.-]+\.exe$", c.Target!);
                    if (c.Launch == LaunchKind.Msc) Assert.EndsWith(".msc", c.Target!);
                    if (c.Launch == LaunchKind.ControlPanelApplet) Assert.EndsWith(".cpl", c.Target!);
                    if (c.Launch == LaunchKind.ControlPanelItem) Assert.StartsWith("Microsoft.", c.Target!);
                    break;
                case "registry-set":
                    Assert.Equal("registry", s.Read.Type);
                    Assert.NotNull(c.Kind);
                    Assert.NotNull(c.Data);
                    var kind = Enum.Parse<RegKind>(c.Kind!, true);
                    RegistryBackup.Decode(kind, c.Data!); // must parse
                    Assert.False(string.IsNullOrWhiteSpace(c.ResultLabel), s.Id);
                    break;
                case "registry-delete":
                    Assert.Equal("registry", s.Read.Type);
                    break;
                case "action":
                    Assert.True(registry.TryGet(c.ActionId!, out _), $"{s.Id}: unknown action {c.ActionId}");
                    break;
                default:
                    Assert.Fail($"{s.Id}: unknown control type {c.Type}");
                    break;
            }
        }
    }

    [Fact]
    public void Settings_SecurityCategoryNeverOffersWriteControls()
    {
        foreach (var s in Kb.Settings.Where(s => s.Category == "Security"))
            Assert.DoesNotContain(s.Controls, c => c.Type is "registry-set" or "registry-delete");
    }

    [Fact]
    public void Settings_RegistryWritesToHklmRequireAdmin()
    {
        foreach (var s in Kb.Settings.Where(s => s.Read.Type == "registry" && s.Controls.Any(c => c.Type.StartsWith("registry-"))))
        {
            var hive = RegistryBackup.ParseHive(s.Read.Hive!);
            if (hive is RegHive.LocalMachine or RegHive.Users) Assert.True(s.AdminRequired, $"{s.Id} writes {s.Read.Hive} without AdminRequired");
        }
    }

    [Fact]
    public async Task Settings_AllEvaluateWithoutExceptionOnSimulatedPc()
    {
        using var rig = new TestRig();
        var reader = new SettingsReader(rig.Platform, "Core");
        var states = Kb.Settings.Select(reader.Evaluate).ToList();
        Assert.Equal(Kb.Settings.Count, states.Count);
        Assert.Contains(states, s => s.Availability == SettingAvailability.NotOnThisEdition); // Pro-only items on Home
        Assert.Contains(states, s => s.Availability == SettingAvailability.NotOnThisBuild);   // Windows 10-only items on 11
        var gameMode = states.Single(s => s.Definition.Id == "gaming.game-mode");
        Assert.Equal("On", gameMode.Current.Display);
        var refresh = states.Single(s => s.Definition.Id == "display.refresh-rate");
        Assert.Contains("165", refresh.Current.Display);
        var svc = states.Single(s => s.Definition.Id == "update.service");
        Assert.Contains("Manual", svc.Current.Display);
        await Task.CompletedTask;
    }

    [Fact]
    public void Processes_AreUniqueAndComplete()
    {
        Assert.True(Kb.Processes.Count >= 120);
        foreach (var p in Kb.Processes)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.Description), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.Category), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.Functionality), p.Name);
        }
        Assert.NotNull(Kb.FindProcess("svchost.exe"));
        Assert.NotNull(Kb.FindProcess("SVCHOST.EXE"));
        Assert.NotNull(Kb.FindProcess("RazerCentralService.exe")); // alias
        Assert.True(Kb.FindProcess("lsass.exe")!.Critical);
    }

    [Fact]
    public void Processes_CriticalOnesMatchProtectedList()
    {
        foreach (var p in Kb.Processes.Where(p => p.Critical && p.Name.EndsWith(".exe")))
            Assert.True(ProtectedComponents.IsCriticalProcess(p.Name) || p.Category is "security" or "updater" or "windows-core" or "windows-service" or "windows-shell", p.Name);
        Assert.All(Kb.Processes.Where(p => p.Critical), p => Assert.False(p.SafeToEnd, p.Name));
    }

    [Fact]
    public void AppGuides_AreCompleteAndMatchInstalledApps()
    {
        Assert.True(Kb.AppGuides.Count >= 45);
        foreach (var g in Kb.AppGuides)
        {
            Assert.NotEmpty(g.Match);
            Assert.False(string.IsNullOrWhiteSpace(g.Purpose), g.Key);
            Assert.NotEmpty(g.SafeUninstall);
            Assert.NotEmpty(g.Links);
            Assert.All(g.Links, l => Assert.StartsWith("https://", l.Url));
            Assert.All(g.Caches, c => Assert.StartsWith("%", c.Path));
            Assert.All(g.Caches, c => Assert.DoesNotContain("*", c.Path));
            if (g.Essential) Assert.False(string.IsNullOrWhiteSpace(g.EssentialReason), g.Key);
        }
        foreach (var name in new[] { "Discord", "Spotify", "Steam", "Google Chrome", "Microsoft Edge", "Epic Games Launcher", "OBS Studio", "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810" })
            Assert.NotNull(Kb.FindAppGuide(name));
        Assert.Equal("spotify", Kb.FindAppGuide("Spotify Music", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0")!.Key);
        Assert.True(Kb.FindAppGuideByKey("vcredist")!.Essential);
    }

    [Fact]
    public void AppGuide_CachesNeverPointAtProfilesOrSettings()
    {
        foreach (var c in Kb.AppGuides.SelectMany(g => g.Caches))
        {
            var p = c.Path.ToLowerInvariant();
            Assert.DoesNotContain("cookies", p);
            Assert.DoesNotContain("login", p);
            Assert.DoesNotContain("local storage", p);
            Assert.False(p.EndsWith("user data") || p.EndsWith("\\default"), c.Path);
            Assert.True(p.Contains("cache") || p.Contains("storage") || p.Contains("data") || p.Contains("temp"), c.Path);
        }
    }

    [Fact]
    public void GpuSettings_DrsIdsParseAndValuesDescribe()
    {
        Assert.True(Kb.GpuSettings.Count >= 45);
        foreach (var vendor in new[] { "NVIDIA", "AMD", "Intel" }) Assert.NotEmpty(Kb.GpuSettingsFor(vendor));
        var readable = Kb.GpuSettings.Where(g => g.DrsSettingId is not null).ToList();
        Assert.True(readable.Count >= 15);
        foreach (var g in readable)
        {
            Assert.NotNull(g.DrsId);
            Assert.False(string.IsNullOrWhiteSpace(g.DescribeValue(g.ValueNames is { Count: > 0 } ? null : 1)));
        }
        var vsync = Kb.GpuSettings.Single(g => g.Id == "nv.vsync");
        Assert.Equal(0x00A879CFu, vsync.DrsId);
        Assert.Equal("Off", vsync.DescribeValue(0x08416747));
        Assert.Equal("141 FPS", Kb.GpuSettings.Single(g => g.Id == "nv.max-frame-rate").DescribeValue(141));
        Assert.Equal("Prefer maximum performance", Kb.GpuSettings.Single(g => g.Id == "nv.power-management").DescribeValue(1));
        Assert.All(Kb.GpuSettings, g => Assert.False(string.IsNullOrWhiteSpace(g.Effects + g.Tradeoffs + g.WhereToChange)));
    }

    [Fact]
    public void ActionCatalog_EveryActionHasHonestMetadata()
    {
        foreach (var a in ActionRegistry.CreateDefault().All)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Title), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Description), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.ExpectedBenefit), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Risks), a.Id);
            Assert.DoesNotContain("FPS boost", a.ExpectedBenefit, StringComparison.OrdinalIgnoreCase);
        }
        var cats = ActionRegistry.CreateDefault().Catalog.Select(a => a.Category).ToHashSet();
        foreach (var c in new[] { OptimizationCategory.Startup, OptimizationCategory.Storage, OptimizationCategory.Background, OptimizationCategory.Power, OptimizationCategory.Gaming,
                     OptimizationCategory.Display, OptimizationCategory.Maintenance, OptimizationCategory.AppMaintenance, OptimizationCategory.Network, OptimizationCategory.Desktop })
            Assert.Contains(c, cats);
    }
}
