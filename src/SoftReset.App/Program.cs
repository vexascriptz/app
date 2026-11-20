using System.Text;
using Avalonia;
using SoftReset.App.Services;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Storage;
using SoftReset.Platform.Simulated;

namespace SoftReset.App;

internal static class Program
{
    /// <summary>
    /// Entry point. Besides the normal UI, the same executable serves three helper roles:
    ///   --elevated-run &lt;payload&gt;   run one approved action with administrator rights (started via UAC)
    ///   --probe nvapi-drs &lt;file&gt;   read NVIDIA driver settings in an isolated process
    ///   --self-test &lt;folder&gt;      run read-only checks and write results (used for Windows validation)
    ///   --capture-pages &lt;folder&gt;  open each page, save screenshots, and exit (used for Windows validation)
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && args[0] == "--elevated-run") return RunElevated(args[1]);
            if (args.Length >= 3 && args[0] == "--probe" && args[1] == "nvapi-drs" && OperatingSystem.IsWindows())
                return SoftReset.Platform.Windows.WindowsPlatform.RunNvApiProbe(args[2]);
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTest.Run(args[1], args.Contains("--demo"));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "softreset-helper-error.txt"), ex.ToString()); } catch { }
            return 1;
        }

        StartupOptions = StartupOptions.Parse(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static StartupOptions StartupOptions { get; set; } = new();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static ISystemPlatform CreatePlatform(bool demo)
    {
        if (!demo && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return new SoftReset.Platform.Windows.WindowsPlatform();
        return new SimulatedPlatform();
    }

    private static int RunElevated(string payload)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        var request = Json.Deserialize<ElevatedRequest>(json) ?? throw new InvalidOperationException("Invalid request.");
        if (string.IsNullOrWhiteSpace(request.DataDirectory) || !Directory.Exists(request.DataDirectory))
            throw new InvalidOperationException("Invalid data directory.");
        var platform = CreatePlatform(demo: false);
        return ElevatedHelper.RunAsync(request, platform).GetAwaiter().GetResult();
    }
}

public sealed class StartupOptions
{
    public bool Demo { get; set; }
    public string? DataDirectory { get; set; }
    /// <summary>--capture-pages &lt;folder&gt;: open every page read-only, save a PNG of each, then exit (Windows validation).</summary>
    public string? CaptureDirectory { get; set; }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions { Demo = args.Contains("--demo") || !OperatingSystem.IsWindows() };
        var i = Array.IndexOf(args, "--data");
        if (i >= 0 && i + 1 < args.Length) o.DataDirectory = args[i + 1];
        var c = Array.IndexOf(args, "--capture-pages");
        if (c >= 0 && c + 1 < args.Length) o.CaptureDirectory = args[c + 1];
        return o;
    }
}
