using SoftReset.Core.Optimization.Actions;

namespace SoftReset.Core.Optimization;

public sealed class ActionRegistry
{
    private readonly Dictionary<string, IOptimizationAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    public ActionRegistry(IEnumerable<IOptimizationAction> actions)
    {
        foreach (var a in actions)
        {
            if (!_actions.TryAdd(a.Id, a)) throw new InvalidOperationException($"Duplicate action id {a.Id}");
        }
    }

    public IReadOnlyCollection<IOptimizationAction> All => _actions.Values;

    public IEnumerable<IOptimizationAction> Catalog => _actions.Values.Where(a => !a.Internal).OrderBy(a => a.Category).ThenBy(a => a.Title);

    public IOptimizationAction Get(string id) =>
        _actions.TryGetValue(id, out var a) ? a : throw new KeyNotFoundException($"Unknown action '{id}'.");

    public bool TryGet(string id, out IOptimizationAction action) => _actions.TryGetValue(id, out action!);

    public static ActionRegistry CreateDefault() => new(new IOptimizationAction[]
    {
        // Startup
        new UserStartupItemsAction(), new MachineStartupItemsAction(), new LogonTasksAction(),
        // Storage
        new UserTempCleanupAction(), new WindowsTempCleanupAction(), new RecycleBinAction(), new UpdateCacheCleanupAction(),
        new ComponentCleanupAction(), new HibernationAction(),
        // Background
        new EndProcessesAction(),
        // Power
        new PowerPlanAction(), new ProcessorMaxStateAction(), new UsbSelectiveSuspendAction(), new FastStartupAction(),
        // Gaming
        new GameModeAction(), new BackgroundRecordingAction(), new HagsAction(), new GpuPreferenceAction(), new WindowedOptimizationsAction(),
        new MouseAccelerationAction(),
        // Display
        new RefreshRateAction(), new VisualEffectsAction(),
        // Maintenance
        new RestorePointAction(), new SfcAction(), new DismRestoreHealthAction(), new ChkdskScanAction(), new ResetWindowsUpdateAction(),
        // Network
        new FlushDnsAction(), new RenewDhcpAction(), new ResetNetworkStackAction(), new DisableProxyAction(),
        // Applications
        new ClearAppCacheAction(), new WingetUpgradeAction(),
        // Desktop
        new ShowFileExtensionsAction(), new ShowHiddenFilesAction(), new TaskbarAlignmentAction(), new DarkModeAction(),
        new TransparencyAction(), new ExplorerThisPcAction(), new RestartExplorerAction(),
        // Settings Explorer (internal)
        new SettingRegistrySetAction(), new ServiceStartModeAction(), new TaskEnabledAction(),
    });
}
