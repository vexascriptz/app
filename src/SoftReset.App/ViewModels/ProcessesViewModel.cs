using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Collection;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.App.ViewModels;

public sealed partial class ProcessRow : ObservableObject
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public int SessionId { get; init; }
    [ObservableProperty] private double _cpu;
    [ObservableProperty] private long _memory;
    [ObservableProperty] private string _publisher = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _signature = "";
    [ObservableProperty] private bool _isWindowsComponent;
    [ObservableProperty] private string _startup = "";
    public string MemoryText => Core.Parsing.ToolOutputParsers.FormatBytes(Memory);
    partial void OnMemoryChanged(long value) => OnPropertyChanged(nameof(MemoryText));
    public string Kind => IsWindowsComponent ? "Windows" : SessionId == 0 ? "Service / system" : "App";
    partial void OnIsWindowsComponentChanged(bool value) => OnPropertyChanged(nameof(Kind));
}

public sealed partial class ProcessesViewModel : PageViewModel
{
    private readonly ProcessCpuSampler _sampler = new();
    private DispatcherTimer? _timer;
    private Dictionary<int, IReadOnlyList<string>> _services = new();
    private List<StartupEntry> _startup = new();
    private readonly Dictionary<string, FileDetails> _details = new(StringComparer.OrdinalIgnoreCase);
    private bool _detailsWorkerRunning;

    public ProcessesViewModel(Shell shell) : base(shell) { }
    public override string Title => "Running programs";
    public override string Subtitle => "What's running right now and who made it. Click one for details.";

    public ObservableCollection<ProcessRow> Rows { get; } = new();
    private List<ProcessRow> _all = new();
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private bool _hideWindows;
    [ObservableProperty] private ProcessRow? _selected;
    [ObservableProperty] private ProcessKnowledge? _knowledge;
    [ObservableProperty] private string _locationCheck = "";
    [ObservableProperty] private string _hostedServices = "";
    [ObservableProperty] private string _signerText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _sysinternalsStatus = "";
    public IReadOnlyList<string> Indicators => _indicators;
    private List<string> _indicators = new();
    public bool HasIndicators => _indicators.Count > 0;
    public bool HasKnowledge => Knowledge is not null;
    public bool NoKnowledge => Selected is not null && Knowledge is null;
    public bool IsProtected => Selected is not null && (ProtectedComponents.IsCriticalProcess(Selected.Name) || Knowledge?.Critical == true);

    partial void OnSearchChanged(string value) => Filter();
    partial void OnHideWindowsChanged(bool value) => Filter();
    partial void OnAutoRefreshChanged(bool value) { if (value) _timer?.Start(); else _timer?.Stop(); }

    public override async Task OnActivatedAsync()
    {
        if (_timer is null)
        {
            await LoadContextAsync();
            // CPU % is a difference between two samples: take a baseline first so the first table isn't all zeros.
            try { _sampler.Sample(S.Platform.Processes.Enumerate()); await Task.Delay(750); } catch { }
            Refresh();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (_, _) => { if (Shell.Main.CurrentPage == this) Refresh(); };
            if (AutoRefresh) _timer.Start();
        }
        var tools = SysinternalsFolder();
        SysinternalsStatus = tools is null
            ? "Sysinternals tools are not bundled (Microsoft's licence does not permit redistribution). Place procexp64.exe / autoruns64.exe / tcpview64.exe in a 'tools\\sysinternals' folder next to the console to launch them from here."
            : $"Sysinternals tools found in {tools}.";
    }

    private async Task LoadContextAsync()
    {
        await Task.Run(() =>
        {
            try { _services = S.Platform.Processes.GetServicesByPid().ToDictionary(k => k.Key, v => v.Value); } catch { }
            try { _startup = new StartupInventory(S.Platform.Registry, S.Platform.Folders, S.Platform.FileExists).Enumerate(); } catch { }
        });
    }

    [RelayCommand]
    public void Refresh()
    {
        IReadOnlyList<ProcessInfo> procs;
        try { procs = S.Platform.Processes.Enumerate(); }
        catch (Exception ex) { Dialogs.Toast($"Could not list processes: {ex.Message}", ToastKind.Error); return; }
        var cpu = _sampler.Sample(procs);
        var byPid = _all.ToDictionary(r => r.Pid);
        var fresh = new List<ProcessRow>();
        foreach (var p in procs)
        {
            if (!byPid.TryGetValue(p.Pid, out var row) || row.Name != p.Name)
            {
                row = new ProcessRow { Pid = p.Pid, Name = p.Name, Path = p.Path, SessionId = p.SessionId };
                var k = S.Knowledge.FindProcess(p.Name);
                row.Description = k?.DisplayName ?? "";
                row.IsWindowsComponent = k?.WindowsComponent == true || (p.Path?.StartsWith(S.Platform.Folders.WindowsDir, StringComparison.OrdinalIgnoreCase) ?? false) || p.Pid <= 4;
                row.Startup = StartupFor(p);
                if (p.Path is not null && _details.TryGetValue(p.Path, out var d)) ApplyDetails(row, d);
            }
            row.Cpu = Math.Round(cpu.GetValueOrDefault(p.Pid), 1);
            row.Memory = p.PrivateBytes;
            fresh.Add(row);
        }
        _all = fresh;
        Summary = $"{fresh.Count} running";
        Filter();
        if (!_detailsWorkerRunning) _ = LoadDetailsAsync();
    }

    private string StartupFor(ProcessInfo p)
    {
        if (_services.TryGetValue(p.Pid, out var svcs) && svcs.Count > 0) return svcs.Count == 1 ? $"Service: {svcs[0]}" : $"Services ({svcs.Count})";
        if (p.Path is null) return "";
        var e = _startup.FirstOrDefault(s => s.ExecutablePath is { } x && string.Equals(Environment.ExpandEnvironmentVariables(x), p.Path, StringComparison.OrdinalIgnoreCase));
        return e is null ? "" : $"Startup: {e.Name}{(e.Enabled ? "" : " (disabled)")}";
    }

    private async Task LoadDetailsAsync()
    {
        _detailsWorkerRunning = true;
        try
        {
            var pending = _all.Where(r => r.Path is not null && !_details.ContainsKey(r.Path)).Select(r => r.Path!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var path in pending)
            {
                if (_details.ContainsKey(path)) continue;
                var d = await Task.Run(() => { try { return S.Platform.Processes.GetFileDetails(path, true); } catch { return new FileDetails(); } });
                _details[path] = d;
                foreach (var r in _all.Where(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) ApplyDetails(r, d);
                if (Selected is { } sel && string.Equals(sel.Path, path, StringComparison.OrdinalIgnoreCase)) UpdateSignaturePanel(sel);
            }
        }
        finally { _detailsWorkerRunning = false; }
    }

    private static void ApplyDetails(ProcessRow r, FileDetails d)
    {
        r.Publisher = d.Signer ?? d.Company ?? "";
        if (string.IsNullOrEmpty(r.Description)) r.Description = d.Description ?? "";
        r.Signature = d.Signature switch
        {
            SignatureState.SignedMicrosoftWindows => "Microsoft Windows",
            SignatureState.Signed => "Signed",
            SignatureState.Unsigned => "Unsigned",
            SignatureState.InvalidOrUntrusted => "Invalid",
            SignatureState.Expired => "Expired cert",
            SignatureState.Revoked => "Revoked",
            SignatureState.AccessDenied => "No access",
            _ => "",
        };
        if (d.Signature == SignatureState.SignedMicrosoftWindows) r.IsWindowsComponent = true;
    }

    private void Filter()
    {
        var q = Search.Trim();
        var keep = Selected;
        Rows.Clear();
        foreach (var r in _all.Where(r => (!HideWindows || !r.IsWindowsComponent) &&
                                         (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Description.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Pid.ToString() == q))
                              .OrderByDescending(r => r.Cpu).ThenByDescending(r => r.Memory))
            Rows.Add(r);
        if (keep is not null) Selected = Rows.FirstOrDefault(r => r.Pid == keep.Pid);
    }

    partial void OnSelectedChanged(ProcessRow? value)
    {
        Knowledge = value is null ? null : S.Knowledge.FindProcess(value.Name);
        OnPropertyChanged(nameof(HasKnowledge)); OnPropertyChanged(nameof(NoKnowledge)); OnPropertyChanged(nameof(IsProtected));
        if (value is null) return;
        var match = Knowledge is null ? null : KnowledgeBase.PathMatchesExpectation(Knowledge, value.Path);
        LocationCheck = match switch
        {
            true => "In its normal folder.",
            false => "Not in its normal folder — worth checking.",
            _ => value.Path is null ? "Location hidden by Windows." : "",
        };
        HostedServices = _services.TryGetValue(value.Pid, out var svcs) ? string.Join(", ", svcs) : "";
        UpdateSignaturePanel(value);
        // Selected before the background worker reached it: check this file now instead of waiting.
        if (value.Path is { } path && !_details.ContainsKey(path)) _ = LoadSelectedDetailsAsync(value, path);
    }

    private async Task LoadSelectedDetailsAsync(ProcessRow row, string path)
    {
        var d = await Task.Run(() => { try { return S.Platform.Processes.GetFileDetails(path, true); } catch { return new FileDetails(); } });
        _details[path] = d;
        ApplyDetails(row, d);
        if (ReferenceEquals(Selected, row)) UpdateSignaturePanel(row);
    }

    private void UpdateSignaturePanel(ProcessRow value)
    {
        var d = value.Path is not null && _details.TryGetValue(value.Path, out var det) ? det : null;
        SignerText = d is null ? "Checking…" : d.Signature switch
        {
            SignatureState.SignedMicrosoftWindows or SignatureState.Signed => $"{d.Signer ?? "A verified publisher"} (verified)",
            SignatureState.Unsigned => "Unknown — not signed. Common for small apps, but check if unsure.",
            SignatureState.NotChecked => "Not checked",
            _ => d.Signature.ToString(),
        };
        _indicators = value.Path is null || d is null ? new() : ProcessHeuristics.Evaluate(value.Name, value.Path, d.Signature, S.Platform.Folders);
        OnPropertyChanged(nameof(Indicators)); OnPropertyChanged(nameof(HasIndicators));
    }

    [RelayCommand]
    private async Task EndProcess()
    {
        if (Selected is null) return;
        if (IsProtected) { await Dialogs.ShowMessageAsync("Can't end this", $"{Selected.Name} is part of Windows. Ending it could crash the PC."); return; }
        if (!await Dialogs.ConfirmAsync($"End {Selected.Name}?", "Any unsaved work in it will be lost.", "End it", danger: true)) return;
        if (S.Platform.Processes.TryTerminate(Selected.Pid, out var err)) { Dialogs.Toast($"{Selected.Name} ended.", ToastKind.Success); S.Log($"Ended process {Selected.Name} ({Selected.Pid})"); }
        else Dialogs.Toast($"Could not end {Selected.Name}: {err}", ToastKind.Warning);
        Refresh();
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (Selected?.Path is null) return;
        var r = S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.RevealFile, Selected.Path));
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
    }

    [RelayCommand]
    private void SearchOnline()
    {
        if (Selected is null) return;
        var r = S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Uri, $"https://www.bing.com/search?q={Uri.EscapeDataString($"\"{Selected.Name}\" process {Selected.Publisher}")}"));
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
    }

    private string? SysinternalsFolder()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tools", "sysinternals");
        return Directory.Exists(dir) ? dir : null;
    }

    [RelayCommand]
    private void LaunchSysinternals(string tool)
    {
        var dir = SysinternalsFolder();
        var exe = dir is null ? null : new[] { tool + "64.exe", tool + ".exe" }.Select(f => Path.Combine(dir, f)).FirstOrDefault(File.Exists);
        var target = exe is not null
            ? new LaunchTarget(LaunchKind.InstalledApplication, exe)
            : new LaunchTarget(LaunchKind.Uri, tool switch
            {
                "procexp" => "https://learn.microsoft.com/sysinternals/downloads/process-explorer",
                "autoruns" => "https://learn.microsoft.com/sysinternals/downloads/autoruns",
                "tcpview" => "https://learn.microsoft.com/sysinternals/downloads/tcpview",
                _ => "https://learn.microsoft.com/sysinternals/",
            });
        var r = S.Platform.Shell.Launch(target);
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
        else if (exe is null) Dialogs.Toast("Opened the official Microsoft download page.");
    }

    [RelayCommand] private void OpenTaskManager() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.SystemTool, "taskmgr.exe"));
    [RelayCommand] private void OpenResourceMonitor() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.SystemTool, "resmon.exe"));
}
