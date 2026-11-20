using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SoftReset.App.Services;
using SoftReset.App.ViewModels;
using SoftReset.App.Views;
using SoftReset.Core.Storage;

namespace SoftReset.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Overridable for tests: creates the service graph.</summary>
    public static Func<AppServices>? ServicesFactory { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ServicesFactory?.Invoke() ?? CreateDefaultServices();
            var vm = new MainWindowViewModel(services);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            _ = vm.ActivateCurrentAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static AppServices CreateDefaultServices()
    {
        var o = Program.StartupOptions;
        var platform = Program.CreatePlatform(o.Demo);
        var demo = platform is SoftReset.Platform.Simulated.SimulatedPlatform;
        var paths = demo && o.DataDirectory is null
            ? new AppPaths(Path.Combine(Path.GetTempPath(), "softreset-demo-data"), false)
            : AppPaths.Resolve(overrideRoot: o.DataDirectory);
        return new AppServices(platform, paths, demo);
    }
}
