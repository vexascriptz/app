using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.App.Services;

namespace SoftReset.App.ViewModels;

public abstract partial class PageViewModel : ViewModelBase
{
    protected PageViewModel(Shell shell) => Shell = shell;
    public Shell Shell { get; }
    protected AppServices S => Shell.Services;
    protected IDialogService Dialogs => Shell.Main;
    protected ActionRunner Runner => Shell.Runner;
    protected AutoRunner Auto => Shell.Auto;
    public bool CanUndoAuto => Shell.Auto.LastRunRecordIds.Count > 0;
    protected void RefreshUndoAuto() => OnPropertyChanged(nameof(CanUndoAuto));
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public virtual Task OnActivatedAsync() => Task.CompletedTask;
}

/// <summary>Shared context handed to every page.</summary>
public sealed class Shell
{
    public Shell(AppServices services, MainWindowViewModel main)
    {
        Services = services;
        Main = main;
        Runner = new ActionRunner(services, main);
        Auto = new AutoRunner(services, main);
    }

    public AppServices Services { get; }
    public MainWindowViewModel Main { get; }
    public ActionRunner Runner { get; }
    public AutoRunner Auto { get; }
}

public sealed class NavItem
{
    public NavItem(string title, string iconKey, PageViewModel page, string description) { Title = title; IconKey = iconKey; Page = page; Description = description; }
    public string Title { get; }
    public string Description { get; }
    public string IconKey { get; }
    public PageViewModel Page { get; }
    public Avalonia.Media.Geometry? Icon => Avalonia.Application.Current?.FindResource(IconKey) as Avalonia.Media.Geometry;
}

public sealed partial class MainWindowViewModel : ViewModelBase, IDialogService
{
    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        Shell = new Shell(services, this);
        Dashboard = new DashboardViewModel(Shell);
        Diagnostics = new DiagnosticsViewModel(Shell);
        Processes = new ProcessesViewModel(Shell);
        SettingsExplorer = new SettingsExplorerViewModel(Shell);
        Graphics = new GraphicsViewModel(Shell);
        Apps = new AppsViewModel(Shell);
        Optimization = new OptimizationViewModel(Shell);
        Files = new FilesViewModel(Shell);
        History = new HistoryViewModel(Shell);
        Data = new DataViewModel(Shell);
        NavItems = new ObservableCollection<NavItem>
        {
            new("Home", "IconHome", Dashboard, ""),
            new("Find problems", "IconPulse", Diagnostics, "Scan this PC and see what's wrong"),
            new("Speed up & fix", "IconWand", Optimization, "Pick a fix and apply it"),
            new("Running programs", "IconProcesses", Processes, "See what's running and who made it"),
            new("Windows settings", "IconSliders", SettingsExplorer, "Look up and change any setting"),
            new("Graphics", "IconChip", Graphics, "Graphics card, monitors, NVIDIA"),
            new("Apps", "IconApps", Apps, "Repair or remove installed programs"),
            new("Organize files", "IconFolder", Files, "Tidy a folder, find duplicates"),
            new("Undo changes", "IconHistory", History, "Everything this app changed"),
            new("About", "IconInfo", Data, "Saved data and app info"),
        };
        _selectedNav = NavItems[0];
        _currentPage = Dashboard;
        Shell.Runner.ChangeCompleted += (_, _) => History.MarkStale();
        Shell.Auto.Completed += (_, _) => History.MarkStale();
    }

    public AppServices Services { get; }
    public Shell Shell { get; }
    public DashboardViewModel Dashboard { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public ProcessesViewModel Processes { get; }
    public SettingsExplorerViewModel SettingsExplorer { get; }
    public GraphicsViewModel Graphics { get; }
    public AppsViewModel Apps { get; }
    public OptimizationViewModel Optimization { get; }
    public FilesViewModel Files { get; }
    public HistoryViewModel History { get; }
    public DataViewModel Data { get; }
    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private DialogViewModel? _currentDialog;
    [ObservableProperty] private string? _busyText;

    public bool IsBusy => BusyText is not null;
    partial void OnBusyTextChanged(string? value) => OnPropertyChanged(nameof(IsBusy));

    public bool IsElevated => Services.Platform.IsElevated;
    public string ElevationText => IsElevated ? "Administrator" : "Standard rights";
    public bool DemoMode => Services.DemoMode;
    public string MachineText => Services.DemoMode ? "Demo PC (simulated)" : Environment.MachineName;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is null) return;
        CurrentPage = value.Page;
        _ = SafeActivate(value.Page);
    }

    private async Task SafeActivate(PageViewModel page)
    {
        try { await page.OnActivatedAsync(); }
        catch (Exception ex)
        {
            Services.Log($"Page {page.GetType().Name} failed to load: {ex}");
            Toast($"Could not load {page.Title}: {ex.Message}", ToastKind.Error);
        }
    }

    public void Navigate(PageViewModel page)
    {
        var item = NavItems.FirstOrDefault(n => n.Page == page);
        if (item is not null) SelectedNav = item;
    }

    public Task ActivateCurrentAsync() => SafeActivate(CurrentPage);

    [RelayCommand]
    private async Task RestartAsAdmin()
    {
        if (IsElevated) return;
        var ok = await ConfirmAsync("Restart as administrator?",
            "The app will reopen with full access, so scans are more complete and Windows won't ask for permission on every change.", "Restart");
        if (!ok) return;
        if (Services.Platform.Elevation.RelaunchElevated(out var error))
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        }
        else Toast(error, ToastKind.Warning);
    }

    // ------------------------------------------------------------------ dialogs

    private async Task<bool> Show(DialogViewModel dialog)
    {
        while (CurrentDialog is not null) await CurrentDialog.Result;
        CurrentDialog = dialog;
        var result = await dialog.Result;
        if (CurrentDialog == dialog) CurrentDialog = null;
        return result;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue", string cancelText = "Cancel", bool danger = false, string? requireTypedText = null) =>
        Show(new ConfirmDialogViewModel { Title = title, Message = message, ConfirmText = confirmText, CancelText = cancelText, Danger = danger, RequireTypedText = requireTypedText });

    public Task<bool> ApproveAsync(ApprovalDialogViewModel dialog) => Show(dialog);

    public Task ShowMessageAsync(string title, string message) => Show(new MessageDialogViewModel { Title = title, Message = message });

    public Task<bool> KeepDisplayChangesAsync(int seconds) => Show(new KeepChangesDialogViewModel(seconds));

    public Task<bool> ShowAsync(DialogViewModel dialog) => Show(dialog);

    public void SetBusyText(string text)
    {
        if (_busyScopes.Count == 0) return;
        _busyScopes[^1].Text = text;
        BusyText = text;
    }

    public void Toast(string text, ToastKind kind = ToastKind.Info)
    {
        var t = new ToastItem { Text = text, Kind = kind };
        void Add()
        {
            Toasts.Add(t);
            while (Toasts.Count > 4) Toasts.RemoveAt(0);
            DispatcherTimer.RunOnce(() => Toasts.Remove(t), TimeSpan.FromSeconds(kind == ToastKind.Error ? 12 : 7));
        }
        if (Dispatcher.UIThread.CheckAccess()) Add(); else Dispatcher.UIThread.Post(Add);
    }

    [RelayCommand]
    private void DismissToast(ToastItem t) => Toasts.Remove(t);

    private readonly List<BusyScope> _busyScopes = new();

    public IDisposable Busy(string text)
    {
        var scope = new BusyScope(this, text);
        _busyScopes.Add(scope);
        BusyText = text;
        return scope;
    }

    private void EndBusy(BusyScope scope)
    {
        _busyScopes.Remove(scope);
        BusyText = _busyScopes.Count > 0 ? _busyScopes[^1].Text : null;
    }

    private sealed class BusyScope : IDisposable
    {
        private readonly MainWindowViewModel _vm;
        private bool _disposed;
        public BusyScope(MainWindowViewModel vm, string text) { _vm = vm; Text = text; }
        public string Text { get; set; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Dispatcher.UIThread.CheckAccess()) _vm.EndBusy(this); else Dispatcher.UIThread.Post(() => _vm.EndBusy(this));
        }
    }
}
