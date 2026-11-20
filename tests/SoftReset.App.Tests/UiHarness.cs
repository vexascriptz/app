using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SoftReset.App.Services;
using SoftReset.App.ViewModels;
using SoftReset.App.Views;
using SoftReset.Core.Storage;
using SoftReset.Platform.Simulated;

namespace SoftReset.App.Tests;

/// <summary>A real MainWindow wired to a simulated PC, rendered by the headless Skia backend.</summary>
public sealed class UiHarness : IDisposable
{
    public UiHarness(int width = 1440, int height = 920, bool elevated = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "softreset-ui-" + Guid.NewGuid().ToString("N")[..8]);
        Platform = new SimulatedPlatform(Path.Combine(Root, "sim"), elevated);
        Services = new AppServices(Platform, new AppPaths(Path.Combine(Root, "data"), isPortable: true), demoMode: true);
        Vm = new MainWindowViewModel(Services);
        Window = new MainWindow { DataContext = Vm, Width = width, Height = height };
        Window.Show();
        Pump();
    }

    public string Root { get; }
    public SimulatedPlatform Platform { get; }
    public AppServices Services { get; }
    public MainWindowViewModel Vm { get; }
    public MainWindow Window { get; }

    public static string ScreenshotDir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("SOFTRESET_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public void Pump(int ms = 0)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (ms > 0) Thread.Sleep(15);
        } while (DateTime.UtcNow < until);
        Dispatcher.UIThread.RunJobs();
    }

    public async Task WaitAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs) throw new TimeoutException("Condition not met in time.");
            Pump();
            await Task.Delay(20);
        }
        Pump();
    }

    public async Task NavigateAsync(PageViewModel page)
    {
        Vm.Navigate(page);
        Pump(50);
        await Task.Delay(50);
        Pump(50);
    }

    public string Screenshot(string name)
    {
        Pump(80);
        var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        var path = Path.Combine(ScreenshotDir, name + ".png");
        frame.Save(path);
        return path;
    }

    /// <summary>Clicks a control with the (headless) mouse, exactly as a user would: hit-testing decides what receives the click.</summary>
    public void Click(Control c)
    {
        c.BringIntoView();
        Pump(40);
        if (!c.IsEffectivelyVisible) throw new InvalidOperationException($"{c.Name ?? c.GetType().Name} is not visible.");
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Window) ?? throw new InvalidOperationException("Control is not in the window.");
        Window.MouseMove(p, RawInputModifiers.None);
        Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Pump(20);
    }

    /// <summary>Focuses a text box by clicking it and types text through the keyboard input pipeline.</summary>
    public void Type(TextBox box, string text)
    {
        Click(box);
        box.SelectAll();
        Window.KeyTextInput(text);
        Pump(20);
    }

    public Button FindButton(string content) =>
        Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content is string s && s == content)
        ?? throw new InvalidOperationException($"Button '{content}' not found.");

    /// <summary>Clicks through whatever dialog is open by pressing the named button.</summary>
    public async Task AnswerDialogAsync<TDialog>(string buttonName, int timeoutMs = 15000) where TDialog : DialogViewModel
    {
        await WaitAsync(() => Vm.CurrentDialog is TDialog, timeoutMs);
        var dialog = Vm.CurrentDialog;
        Click(Find<Button>(buttonName));
        await WaitAsync(() => !ReferenceEquals(Vm.CurrentDialog, dialog));
    }

    public T Find<T>(string name) where T : Control =>
        Window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible) ?? throw new InvalidOperationException($"Control {name} not found.");

    public IEnumerable<T> All<T>() where T : Control => Window.GetVisualDescendants().OfType<T>();

    public void Dispose()
    {
        Window.Close();
        Platform.Dispose();
        try { Directory.Delete(Root, true); } catch { }
    }
}
