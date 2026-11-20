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
    }

    public AppServices Services { get; }
    public MainWindowViewModel Main { get; }
    public ActionRunner Runner { get; }
}

public sealed class NavItem
{
    public NavItem(string title, string iconKey, PageViewModel page) { Title = title; IconKey = iconKey; Page = page; }
    public string Title { get; }
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
        Session = new SessionViewModel(Shell);
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
            new("Home", "IconHome", Dashboard),
            new("Customer session", "IconSession", Session),
            new("Diagnostics", "IconPulse", Diagnostics),
            new("Background processes", "IconProcesses", Processes),
            new("Settings explorer", "IconSliders", SettingsExplorer),
            new("Graphics & NVIDIA", "IconChip", Graphics),
            new("Applications", "IconApps", Apps),
            new("Optimization", "IconWand", Optimization),
            new("File organizer", "IconFolder", Files),
            new("Change history", "IconHistory", History),
            new("Data & about", "IconInfo", Data),
        };
        _selectedNav = NavItems[0];
        _currentPage = Dashboard;
        services.ActiveSessionChanged += (_, _) => OnPropertyChanged(nameof(SessionBanner));
        Shell.Runner.ChangeCompleted += (_, _) => History.MarkStale();
    }

    public AppServices Services { get; }
    public Shell Shell { get; }
    public DashboardViewModel Dashboard { get; }
    public SessionViewModel Session { get; }
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
    public string? SessionBanner => Services.ActiveSession is { IsClosed: false } s
        ? $"Session {s.Id}{(string.IsNullOrWhiteSpace(s.CustomerName) ? "" : " · " + s.CustomerName)}"
        : null;

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
            "The console will close and reopen with administrator rights (one UAC prompt). This allows a complete scan (boot times, crash dumps, SSD health) and avoids a UAC prompt for each administrator change.\n\nAn active session is saved and can be resumed.", "Restart as administrator");
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

    public IDisposable Busy(string text)
    {
        BusyText = text;
        return new BusyScope(this, text);
    }

    private sealed class BusyScope : IDisposable
    {
        private readonly MainWindowViewModel _vm;
        private readonly string _text;
        public BusyScope(MainWindowViewModel vm, string text) { _vm = vm; _text = text; }
        public void Dispose() { if (_vm.BusyText == _text) _vm.BusyText = null; }
    }
}
