using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.App.ViewModels;

public sealed class GpuCard
{
    public GpuCard(GpuInfo g) => Gpu = g;
    public GpuInfo Gpu { get; }
    public string Name => Gpu.Name;
    public string VendorText => $"{Gpu.Vendor switch { GpuVendor.Nvidia => "NVIDIA", GpuVendor.Amd => "AMD", GpuVendor.Intel => "Intel", _ => Gpu.Vendor.ToString() }}{(Gpu.IsIntegrated ? " · integrated" : " · dedicated")}{(Gpu.HasAttachedDisplay ? " · drives a display" : "")}";
    public string Driver => $"{Gpu.VendorDriverVersion ?? Gpu.DriverVersion}{(Gpu.DriverDate is { } d ? $" ({d:yyyy-MM-dd})" : "")}";
    public string Memory => Core.Parsing.ToolOutputParsers.FormatBytes(Gpu.DedicatedMemoryBytes);
    public bool HasTelemetry => Gpu.Telemetry is not null;
    public string Temperature => Gpu.Telemetry?.TemperatureC is { } t ? $"{t:0} °C" : "—";
    public string Utilization => Gpu.Telemetry?.UtilizationPercent is { } u ? $"{u:0}%" : "—";
    public string Power => Gpu.Telemetry?.PowerWatts is { } p ? $"{p:0.#} W{(Gpu.Telemetry.PowerLimitWatts is { } l ? $" / {l:0} W" : "")}" : "—";
    public string Clocks => Gpu.Telemetry?.GraphicsClockMHz is { } c ? $"{c} MHz core · {Gpu.Telemetry.MemoryClockMHz} MHz memory" : "—";
    public string Fan => Gpu.Telemetry?.FanPercent is { } f ? $"{f}%" : Gpu.Telemetry?.FanRpm is { } rpm ? $"{rpm} RPM" : "—";
    public string Vram => Gpu.Telemetry?.MemoryUsedBytes is { } used ? $"{Core.Parsing.ToolOutputParsers.FormatBytes(used)} of {Core.Parsing.ToolOutputParsers.FormatBytes(Gpu.Telemetry.MemoryTotalBytes)}" : "—";
    public string Extra => string.Join(" · ", new[] { Gpu.Telemetry?.PerformanceState, Gpu.Telemetry?.PcieLink }.Where(x => !string.IsNullOrEmpty(x)));
    public string Source => Gpu.Telemetry is null ? "No live sensor data available for this GPU." : $"Source: {Gpu.Telemetry.Source}";
    public string Throttle => Gpu.Telemetry?.ThrottleReasons is { Count: > 0 } r ? "Clock limits active: " + string.Join(", ", r) : "";

    // Plain-language labels next to each reading.
    public Rating TemperatureRating => Ratings.GpuTemperature(Gpu.Telemetry?.TemperatureC);
    public Rating LoadRating => Ratings.GpuLoad(Gpu.Telemetry?.UtilizationPercent);
    public Rating VramRating => Ratings.VideoMemory(Gpu.Telemetry?.MemoryUsedBytes, Gpu.Telemetry?.MemoryTotalBytes);
    public Rating? DriverRating => Gpu.Vendor is GpuVendor.Microsoft or GpuVendor.Unknown ? null : Ratings.DriverAge(Gpu.DriverDate, DateTimeOffset.Now);
}

public sealed partial class DisplayRow : ObservableObject
{
    public DisplayRow(DisplayInfo d) => Display = d;
    public DisplayInfo Display { get; }
    public string Name => string.IsNullOrEmpty(Display.MonitorName) ? Display.DeviceName : Display.MonitorName;
    public string Mode => $"{Display.Width} × {Display.Height} @ {Display.RefreshHz} Hz{(Display.ScalePercent is { } s ? $" · {s}% scale" : "")}";
    public string Max => $"Highest at this resolution: {Display.MaxRefreshHzAtCurrentResolution} Hz";
    public bool CanIncrease => Display.MaxRefreshHzAtCurrentResolution > Display.RefreshHz;
    public Rating RefreshRating => Ratings.RefreshRate(Display.RefreshHz, Math.Max(Display.RefreshHz, Display.MaxRefreshHzAtCurrentResolution));
    public string Adapter => $"Connected to {Display.AdapterName}{(Display.IsPrimary ? " · primary" : "")}";
}

public sealed class GpuSettingRow
{
    public GpuSettingRow(GpuSettingKnowledge k, string? value, string? location) { Knowledge = k; Value = value; Location = location; }
    public GpuSettingKnowledge Knowledge { get; }
    public string? Value { get; }
    public string? Location { get; }
    public string Name => Knowledge.Name;
    public string Category => Knowledge.Category;
    public bool HasValue => Value is not null;
    public string ValueText => Value ?? Knowledge.Readability ?? "";
    public string RecommendationText => string.Join("\n", Knowledge.Recommendations.Select(r => $"• {r.UseCase}: {r.Value} — {r.Why}"));
}

public sealed partial class GraphicsViewModel : PageViewModel
{
    public GraphicsViewModel(Shell shell) : base(shell) { }
    public override string Title => "Graphics";
    public override string Subtitle => "Your graphics card, monitors, and driver settings.";

    public ObservableCollection<GpuCard> Gpus { get; } = new();
    public ObservableCollection<DisplayRow> Displays { get; } = new();
    public ObservableCollection<GpuSettingRow> NvidiaSettings { get; } = new();
    public ObservableCollection<NvidiaProfileSummary> Profiles { get; } = new();
    public ObservableCollection<GpuSettingRow> AmdSettings { get; } = new();
    public ObservableCollection<GpuSettingRow> IntelSettings { get; } = new();
    [ObservableProperty] private string _nvidiaStatus = "Driver settings have not been read yet.";
    [ObservableProperty] private bool _hasNvidia;
    [ObservableProperty] private bool _hasAmd;
    [ObservableProperty] private bool _hasIntel;
    [ObservableProperty] private VendorSoftwareStatus? _vendor;
    [ObservableProperty] private bool _loading;
    private bool _loaded;

    public override async Task OnActivatedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var k in S.Knowledge.GpuSettingsFor("AMD")) AmdSettings.Add(new GpuSettingRow(k, null, null));
        foreach (var k in S.Knowledge.GpuSettingsFor("Intel")) IntelSettings.Add(new GpuSettingRow(k, null, null));
        foreach (var k in S.Knowledge.GpuSettingsFor("NVIDIA")) NvidiaSettings.Add(new GpuSettingRow(k, null, null));
        await Refresh();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        Loading = true;
        try
        {
            var (gpus, displays, vendor) = await Task.Run(() => (S.Platform.Gpu.GetGpus(true), S.Platform.Display.GetDisplays(), S.Platform.Gpu.GetVendorSoftware()));
            Gpus.Clear();
            foreach (var g in gpus) Gpus.Add(new GpuCard(g));
            Displays.Clear();
            foreach (var d in displays) Displays.Add(new DisplayRow(d));
            Vendor = vendor;
            HasNvidia = gpus.Any(g => g.Vendor == GpuVendor.Nvidia);
            HasAmd = gpus.Any(g => g.Vendor == GpuVendor.Amd);
            HasIntel = gpus.Any(g => g.Vendor == GpuVendor.Intel);
        }
        catch (Exception ex) { Dialogs.Toast($"Graphics information unavailable: {ex.Message}", ToastKind.Warning); }
        finally { Loading = false; }
    }

    [RelayCommand]
    private async Task ReadNvidiaSettings()
    {
        Loading = true;
        try
        {
            var r = await S.Platform.Gpu.ReadNvidiaDriverSettingsAsync(CancellationToken.None);
            NvidiaStatus = r.Available ? $"{r.Status} Driver {r.DriverVersion} ({r.DriverBranch}) · global profile \"{r.GlobalProfileName}\"." : r.Status;
            NvidiaSettings.Clear();
            foreach (var k in S.Knowledge.GpuSettingsFor("NVIDIA"))
            {
                var s = k.DrsId is { } id ? r.GlobalSettings.FirstOrDefault(x => x.SettingId == id) : null;
                var value = s is null ? null : s.StringValue ?? k.DescribeValue(s.Value);
                NvidiaSettings.Add(new GpuSettingRow(k, value, s?.Location));
            }
            Profiles.Clear();
            foreach (var p in r.CustomizedProfiles) Profiles.Add(p);
        }
        catch (Exception ex) { NvidiaStatus = $"Could not read NVIDIA settings: {ex.Message}"; }
        finally { Loading = false; }
    }

    [RelayCommand]
    private void OpenVendor(string which)
    {
        var v = Vendor;
        string? target = which switch
        {
            "nvcp" => v?.NvidiaControlPanel,
            "nvapp" => v?.NvidiaApp ?? v?.GeForceExperience,
            "amd" => v?.AmdSoftware,
            "intel" => v?.IntelGraphicsSoftware,
            _ => null,
        };
        if (target is null)
        {
            var url = which switch
            {
                "nvcp" => "https://apps.microsoft.com/detail/9NF8H0H7WMLT",
                "nvapp" => "https://www.nvidia.com/en-us/software/nvidia-app/",
                "amd" => "https://www.amd.com/en/support/download/drivers.html",
                _ => "https://www.intel.com/content/www/us/en/support/products/80939/graphics.html",
            };
            Dialogs.Toast("Not installed on this PC — opening the official download page.");
            S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Uri, url));
            return;
        }
        var launch = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? new LaunchTarget(LaunchKind.InstalledApplication, target) : new LaunchTarget(LaunchKind.AppsFolder, target);
        var r = S.Platform.Shell.Launch(launch);
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
        else if (S.DemoMode) Dialogs.Toast(r.Message);
    }

    [RelayCommand] private async Task AutoGraphics() { await Auto.RunAsync(AutoScope.Graphics, "Optimize graphics and gaming"); RefreshUndoAuto(); await Refresh(); }

    /// <summary>Opens the vendor control panel for one driver setting.</summary>
    [RelayCommand] private void OpenSettingVendor(GpuSettingRow row) => OpenVendor(row.Knowledge.Vendor switch { "AMD" => "amd", "Intel" => "intel", _ => "nvcp" });

    [RelayCommand] private void OpenWindowsGraphics() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Uri, "ms-settings:display-advancedgraphics"));
    [RelayCommand] private void OpenDxDiag() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.SystemTool, "dxdiag.exe"));

    [RelayCommand]
    private async Task SetMaxRefresh(DisplayRow d)
    {
        await Runner.RunAsync(new ActionRequest { ActionId = ActionIds.RefreshRate, Parameters = { ["device"] = d.Display.DeviceName, ["refresh"] = d.Display.MaxRefreshHzAtCurrentResolution.ToString() } });
        await Refresh();
    }

    [RelayCommand]
    private async Task Hags() { Shell.Main.Navigate(Shell.Main.Optimization); await Shell.Main.Optimization.SelectActionAsync(ActionIds.Hags); }

    [RelayCommand]
    private async Task GpuPreference() { Shell.Main.Navigate(Shell.Main.Optimization); await Shell.Main.Optimization.SelectActionAsync(ActionIds.GpuPreference); }
}

// =====================================================================  Applications

public sealed class AppRow
{
    public AppRow(InstalledApp app, AppGuide? guide) { App = app; Guide = guide; }
    public InstalledApp App { get; }
    public AppGuide? Guide { get; }
    public string Name => App.Name;
    public string Publisher => App.Publisher;
    public string Version => App.Version;
    public string KindText => App.Kind switch { AppKind.Store => App.IsInboxWindowsApp ? "Windows app" : "Store app", AppKind.SystemComponent => "System component", _ => "Desktop program" };
    public string Size => App.EstimatedSizeBytes is { } s ? Core.Parsing.ToolOutputParsers.FormatBytes(s) : "";
    public bool HasGuide => Guide is not null;
    public bool IsEssential => Guide?.Essential == true || ProtectedComponents.IsEssentialApp(App.Name) || (App.PackageFamilyName is { } pf && ProtectedComponents.IsEssentialApp(pf)) || App.IsInboxWindowsApp;
}

public sealed partial class AppsViewModel : PageViewModel
{
    private List<AppRow> _all = new();
    private List<StartupEntry> _startup = new();

    public AppsViewModel(Shell shell) : base(shell) { }
    public override string Title => "Apps";
    public override string Subtitle => "Installed programs, with fixes for common problems.";

    public ObservableCollection<AppRow> Apps { get; } = new();
    public ObservableCollection<OptionalFeatureInfo> Features { get; } = new();
    public string[] KindFilters { get; } = { "All apps", "Desktop programs", "Store apps", "Windows apps", "Apps with guides" };
    [ObservableProperty] private string _kindFilter = "All apps";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private AppRow? _selected;
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _startupText = "";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private string _summary = "";
    public bool HasSelection => Selected is not null;
    public AppGuide? Guide => Selected?.Guide;
    public bool HasGuide => Guide is not null;
    public bool CanRepairMsi => Selected?.App is { IsMsi: true, MsiProductCode: not null, NoRepair: false };
    public bool CanModify => Selected?.App.ModifyCommand is not null && !CanRepairMsi;
    public bool IsStore => Selected?.App.Kind == AppKind.Store;
    public bool CanUninstall => Selected is not null && !Selected.IsEssential && (Selected.App.UninstallCommand is not null || IsStore) && !Selected.App.NoRemove;
    public bool HasCaches => Guide?.Caches.Count > 0;
    public string EssentialText => Selected?.IsEssential == true ? "Part of Windows or needed by other programs — removing it could break things." : "";

    partial void OnSearchChanged(string value) => Filter();
    partial void OnKindFilterChanged(string value) => Filter();
    partial void OnSelectedChanged(AppRow? value)
    {
        foreach (var p in new[] { nameof(HasSelection), nameof(Guide), nameof(HasGuide), nameof(CanRepairMsi), nameof(CanModify), nameof(IsStore), nameof(CanUninstall), nameof(HasCaches), nameof(EssentialText) }) OnPropertyChanged(p);
        SizeText = value?.Size is { Length: > 0 } s ? s : "Not reported";
        StartupText = value is null ? "" : DescribeStartup(value);
    }

    private string DescribeStartup(AppRow row)
    {
        var names = row.Guide?.StartupEntryNames ?? new();
        var matches = _startup.Where(e => names.Any(n => e.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                                          || (row.App.InstallLocation is { Length: > 3 } loc && e.ExecutablePath is { } exe && exe.StartsWith(loc, StringComparison.OrdinalIgnoreCase))
                                          || e.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return "No";
        return matches.Any(m => m.Enabled) ? "Yes" : "No (turned off)";
    }

    public override async Task OnActivatedAsync()
    {
        if (_all.Count > 0) return;
        Loading = true;
        try
        {
            var (apps, features, startup) = await Task.Run(() => (S.Platform.Apps.GetInstalledApps(), S.Platform.Apps.GetOptionalFeatures(), new StartupInventory(S.Platform.Registry, S.Platform.Folders, S.Platform.FileExists).Enumerate()));
            _startup = startup;
            _all = apps.Select(a => new AppRow(a, S.Knowledge.FindAppGuide(a))).ToList();
            Features.Clear();
            foreach (var f in features) Features.Add(f);
            Summary = $"{_all.Count} applications · {_all.Count(a => a.App.Kind == AppKind.Desktop)} desktop · {_all.Count(a => a.App.Kind == AppKind.Store)} Store · {_all.Count(a => a.HasGuide)} with guides · {Features.Count(f => f.State == "Enabled")} Windows features enabled";
            Filter();
        }
        catch (Exception ex) { Dialogs.Toast($"Could not list applications: {ex.Message}", ToastKind.Error); }
        finally { Loading = false; }
    }

    private void Filter()
    {
        var q = Search.Trim();
        Apps.Clear();
        foreach (var a in _all.Where(a => KindFilter switch
                 {
                     "Desktop programs" => a.App.Kind == AppKind.Desktop,
                     "Store apps" => a.App.Kind == AppKind.Store && !a.App.IsInboxWindowsApp,
                     "Windows apps" => a.App.IsInboxWindowsApp || a.App.Kind == AppKind.SystemComponent,
                     "Apps with guides" => a.HasGuide,
                     _ => true,
                 } && (q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))))
            Apps.Add(a);
    }

    [RelayCommand]
    private async Task ComputeSize()
    {
        if (Selected?.App.InstallLocation is not { } loc) { SizeText = "Install location not recorded by the installer."; return; }
        SizeText = "Measuring…";
        var size = await Task.Run(() => S.Platform.Apps.ComputeFolderSize(loc, CancellationToken.None));
        SizeText = size is null ? "Folder not accessible." : $"{Core.Parsing.ToolOutputParsers.FormatBytes(size)} in {loc}";
    }

    private void Launch(LaunchTarget t)
    {
        var r = S.Platform.Shell.Launch(t);
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
        else if (S.DemoMode) Dialogs.Toast(r.Message);
    }

    [RelayCommand]
    private async Task RepairMsi()
    {
        if (Selected?.App.MsiProductCode is not { } code) return;
        if (!await Dialogs.ConfirmAsync($"Repair {Selected.Name}?", "Windows will check the program's files and replace any that are missing or damaged. Your settings and documents aren't touched.", "Repair")) return;
        S.Log($"MSI repair {Selected.Name} {code}");
        Launch(new LaunchTarget(LaunchKind.SystemTool, "msiexec.exe", $"/fa {code}"));
    }

    [RelayCommand]
    private async Task Modify()
    {
        if (Selected?.App.ModifyCommand is not { } cmd) return;
        if (!await Dialogs.ConfirmAsync($"Open {Selected.Name} setup?", "Opens the program's own setup so you can repair or change it.", "Open setup")) return;
        Launch(VendorCommand(cmd));
    }

    [RelayCommand]
    private void StoreAdvanced()
    {
        if (Selected?.App.PackageFamilyName is not { } pfn) return;
        Launch(new LaunchTarget(LaunchKind.Uri, $"ms-settings:appsfeatures-app?{pfn}"));
    }

    [RelayCommand]
    private void OpenApp()
    {
        if (Selected is null) return;
        if (Selected.App.AppUserModelId is { } aumid) Launch(new LaunchTarget(LaunchKind.AppsFolder, aumid));
        else if (Selected.App.InstallLocation is { } loc) Launch(new LaunchTarget(LaunchKind.Folder, loc));
    }

    [RelayCommand]
    private async Task Uninstall()
    {
        if (Selected is null || !CanUninstall) return;
        if (!await Dialogs.ConfirmAsync($"Uninstall {Selected.Name}?", "This opens the program's own uninstaller. Close the program first.", "Open uninstaller", danger: true)) return;
        S.Log($"Uninstall requested for {Selected.Name}");
        if (IsStore) Launch(new LaunchTarget(LaunchKind.Uri, "ms-settings:appsfeatures"));
        else Launch(VendorCommand(Selected.App.UninstallCommand!));
    }

    /// <summary>Turns an uninstall/modify command line from the registry into a validated launch target.</summary>
    internal static LaunchTarget VendorCommand(string commandLine)
    {
        var exe = CommandLine.ExtractExecutable(commandLine) ?? commandLine;
        var args = commandLine.Trim().StartsWith('"') ? commandLine.Trim()[(exe.Length + 2)..].Trim() : commandLine.Trim()[exe.Length..].Trim();
        if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            return new LaunchTarget(LaunchKind.SystemTool, "msiexec.exe", args);
        if (Path.GetFileName(exe).Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase))
            return new LaunchTarget(LaunchKind.SystemTool, "rundll32.exe", args);
        return new LaunchTarget(LaunchKind.InstalledApplication, Environment.ExpandEnvironmentVariables(exe), args.Length > 0 ? args : null);
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        if (Guide is null) return;
        Shell.Main.Navigate(Shell.Main.Optimization);
        await Shell.Main.Optimization.SelectActionAsync(ActionIds.ClearAppCache, new() { ["app"] = Guide.Key });
    }

    /// <summary>Updates every app that has an update available (Windows Package Manager), after one confirmation.</summary>
    [RelayCommand]
    private async Task UpdateAll()
    {
        var action = S.Actions.Get(ActionIds.WingetUpgrade);
        ActionInspection insp;
        using (Dialogs.Busy("Checking for app updates…"))
            insp = await Task.Run(() => action.InspectAsync(S.ActionContext, new ActionRequest { ActionId = action.Id }, CancellationToken.None));
        if (!insp.Applicable) { Dialogs.Toast(insp.NotApplicableReason ?? "App updates aren't available on this PC.", ToastKind.Warning); return; }
        var items = insp.Items.Where(i => i.Selectable).Select(i => i.Id).ToList();
        if (items.Count == 0) { Dialogs.Toast("All apps are up to date.", ToastKind.Success); return; }
        await Runner.RunAsync(new ActionRequest { ActionId = action.Id, SelectedItems = items });
    }

    [RelayCommand] private void OpenLink(GuideLink l) => Launch(new LaunchTarget(LaunchKind.Uri, l.Url));
    [RelayCommand] private void OpenAppsSettings() => Launch(new LaunchTarget(LaunchKind.Uri, "ms-settings:appsfeatures"));
    [RelayCommand] private void OpenFeatures() => Launch(new LaunchTarget(LaunchKind.SystemTool, "optionalfeatures.exe"));
}
