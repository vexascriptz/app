using System.Reflection;
using System.Text.RegularExpressions;
using SoftReset.Core.Platform;
using SoftReset.Core.Storage;

namespace SoftReset.Core.Knowledge;

/// <summary>Loads the embedded knowledge bases (settings, processes, applications, GPU settings).</summary>
public sealed class KnowledgeBase
{
    private static readonly Lazy<KnowledgeBase> _default = new(LoadEmbedded);
    public static KnowledgeBase Default => _default.Value;

    public IReadOnlyList<SettingDefinition> Settings { get; init; } = Array.Empty<SettingDefinition>();
    public IReadOnlyList<ProcessKnowledge> Processes { get; init; } = Array.Empty<ProcessKnowledge>();
    public IReadOnlyList<AppGuide> AppGuides { get; init; } = Array.Empty<AppGuide>();
    public IReadOnlyList<GpuSettingKnowledge> GpuSettings { get; init; } = Array.Empty<GpuSettingKnowledge>();

    private Dictionary<string, ProcessKnowledge>? _processIndex;
    private List<(AppGuide Guide, Regex[] Patterns)>? _appIndex;

    public static KnowledgeBase LoadEmbedded()
    {
        var asm = typeof(KnowledgeBase).Assembly;
        var settings = new List<SettingDefinition>();
        var processes = new List<ProcessKnowledge>();
        var apps = new List<AppGuide>();
        var gpu = new List<GpuSettingKnowledge>();

        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("SoftReset.Knowledge.", StringComparison.Ordinal)))
        {
            var json = ReadResource(asm, res);
            var file = res["SoftReset.Knowledge.".Length..];
            if (file.StartsWith("settings", StringComparison.OrdinalIgnoreCase))
                settings.AddRange(Json.Deserialize<List<SettingDefinition>>(json) ?? new());
            else if (file.StartsWith("processes", StringComparison.OrdinalIgnoreCase))
                processes.AddRange(Json.Deserialize<List<ProcessKnowledge>>(json) ?? new());
            else if (file.StartsWith("apps", StringComparison.OrdinalIgnoreCase))
                apps.AddRange(Json.Deserialize<List<AppGuide>>(json) ?? new());
            else if (file.StartsWith("gpu", StringComparison.OrdinalIgnoreCase))
                gpu.AddRange(Json.Deserialize<List<GpuSettingKnowledge>>(json) ?? new());
        }

        return new KnowledgeBase { Settings = settings, Processes = processes, AppGuides = apps, GpuSettings = gpu };
    }

    private static string ReadResource(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing resource {name}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public IEnumerable<string> SettingCategories => Settings.Select(s => s.Category).Distinct();

    public SettingDefinition? FindSetting(string id) => Settings.FirstOrDefault(s => s.Id == id);

    public ProcessKnowledge? FindProcess(string exeName)
    {
        _processIndex ??= BuildProcessIndex();
        return _processIndex.TryGetValue(exeName, out var k) ? k : null;
    }

    private Dictionary<string, ProcessKnowledge> BuildProcessIndex()
    {
        var d = new Dictionary<string, ProcessKnowledge>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Processes)
        {
            d.TryAdd(p.Name, p);
            foreach (var a in p.Aliases) d.TryAdd(a, p);
        }
        return d;
    }

    public AppGuide? FindAppGuide(InstalledApp app) => FindAppGuide(app.Name, app.PackageFamilyName);

    public AppGuide? FindAppGuide(string name, string? packageFamily = null)
    {
        _appIndex ??= AppGuides
            .Select(g => (g, g.Match.Select(m => new Regex(m, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray()))
            .ToList();
        foreach (var (guide, patterns) in _appIndex)
        {
            if (packageFamily is not null && guide.MatchPackages.Any(p => packageFamily.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return guide;
            if (patterns.Any(p => p.IsMatch(name)))
                return guide;
        }
        return null;
    }

    public AppGuide? FindAppGuideByKey(string key) => AppGuides.FirstOrDefault(g => g.Key == key);

    public IEnumerable<GpuSettingKnowledge> GpuSettingsFor(string vendor) =>
        GpuSettings.Where(g => g.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Is the process at <paramref name="path"/> where the knowledge base expects it?</summary>
    public static bool? PathMatchesExpectation(ProcessKnowledge k, string? path)
    {
        if (string.IsNullOrEmpty(path) || k.ExpectedPaths.Count == 0) return null;
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        foreach (var expected in k.ExpectedPaths)
        {
            var exp = Environment.ExpandEnvironmentVariables(expected).TrimEnd('\\', '/');
            var pattern = "^" + Regex.Escape(exp).Replace(@"\*", ".*") + "$";
            if (Regex.IsMatch(dir.TrimEnd('\\', '/'), pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
}
