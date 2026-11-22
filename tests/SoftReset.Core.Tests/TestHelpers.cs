using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;
using SoftReset.Platform.Simulated;

namespace SoftReset.Core.Tests;

/// <summary>A simulated PC plus an isolated data directory, cleaned up after each test.</summary>
public sealed class TestRig : IDisposable
{
    public TestRig(bool elevated = false)
    {
        Root = Path.Combine(AppContext.BaseDirectory, "sandbox", "test-" + Guid.NewGuid().ToString("N")[..10]);
        Platform = new SimulatedPlatform(Path.Combine(Root, "sim"), elevated);
        Paths = new AppPaths(Path.Combine(Root, "data"), isPortable: true);
        Paths.EnsureCreated();
        Journal = new ChangeJournal(Paths.Journal);
        Context = new ActionContext { Platform = Platform, Journal = Journal, Paths = Paths };
        Registry = ActionRegistry.CreateDefault();
        Executor = new ActionExecutor(Registry, Context);
    }

    public string Root { get; }
    public SimulatedPlatform Platform { get; }
    public AppPaths Paths { get; }
    public ChangeJournal Journal { get; }
    public ActionContext Context { get; }
    public ActionRegistry Registry { get; }
    public ActionExecutor Executor { get; }

    public Task<Models.SystemSnapshot> ScanAsync() =>
        Platform.Collector.CollectAsync(new CollectionOptions { CpuSampleDuration = TimeSpan.Zero }, null, CancellationToken.None);

    public void Dispose()
    {
        Platform.Dispose();
        try { Directory.Delete(Root, true); } catch { }
    }
}
