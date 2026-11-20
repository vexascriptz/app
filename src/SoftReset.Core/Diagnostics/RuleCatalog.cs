using SoftReset.Core.Diagnostics.Rules;

namespace SoftReset.Core.Diagnostics;

public static class RuleCatalog
{
    public static IEnumerable<IDiagnosticRule> All() => new IDiagnosticRule[]
    {
        // Performance & memory
        new CpuSustainedHighRule(),
        new CpuProcessHogRule(),
        new MemoryPressureRule(),
        new LowInstalledMemoryRule(),
        new ProcessCountRule(),
        // Storage
        new StorageLowSpaceRule(),
        new CleanupOpportunityRule(),
        new HddSystemDriveRule(),
        // Startup
        new StartupManyItemsRule(),
        new StartupBrokenEntriesRule(),
        new SlowBootRule(),
        new FastStartupUptimeRule(),
        // Stability
        new UnexpectedShutdownRule(),
        new BlueScreenRule(),
        new WheaErrorRule(),
        new AppCrashRule(),
        new GpuDriverResetRule(),
        new ServiceFailureRule(),
        new LongUptimeRule(),
        // Updates & drivers
        new UpdateFailuresRule(),
        new UpdatesStaleRule(),
        new DeviceProblemRule(),
        new GpuDriverRule(),
        // Disk health
        new DiskHealthRule(),
        new DiskErrorEventsRule(),
        // Network
        new NetworkConfigRule(),
        // Gaming & display
        new RefreshRateRule(),
        new DisplayOnIntegratedGpuRule(),
        new GamingConfigRule(),
        new GpuThermalRule(),
        new MemoryConfigurationRule(),
        // Power & thermal
        new PowerPlanRule(),
        new ThrottlingRule(),
        // Security & processes
        new SecurityPostureRule(),
        new SuspiciousProcessRule(),
    };
}
