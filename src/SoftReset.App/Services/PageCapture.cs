using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using SoftReset.App.ViewModels;

namespace SoftReset.App.Services;

/// <summary>
/// Validation mode for real Windows machines: visits every page (read-only — no buttons are pressed and
/// nothing is changed), renders the window to PNG, writes a log, and exits. Lets CI or a technician
/// confirm the UI loads real data on a given Windows build without anyone clicking through it.
/// </summary>
public static class PageCapture
{
    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm, string dir)
    {
        var log = new List<string>();
        var exitCode = 0;
        try
        {
            Directory.CreateDirectory(dir);
            var window = desktop.MainWindow!;
            window.Width = 1440;
            window.Height = 920;
            await Task.Delay(1500);
            var index = 1;
            foreach (var item in vm.NavItems.ToList())
            {
                try
                {
                    vm.SelectedNav = item;
                    // Pages load asynchronously from the real system; give each time to populate.
                    await Task.Delay(item.Page is ProcessesViewModel or AppsViewModel or SettingsExplorerViewModel or GraphicsViewModel ? 9000 : 3000);
                    var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
                    using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
                    bmp.Render(window);
                    var file = Path.Combine(dir, $"{index++:00}-{Slug(item.Title)}.png");
                    bmp.Save(file);
                    log.Add($"OK   {item.Title} -> {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    exitCode = 2;
                    log.Add($"FAIL {item.Title}: {ex}");
                }
            }
            log.Add($"Toasts shown: {string.Join(" | ", vm.Toasts.Select(t => t.Text))}");
        }
        catch (Exception ex)
        {
            exitCode = 2;
            log.Add("FAIL capture: " + ex);
        }
        try { File.WriteAllLines(Path.Combine(dir, "capture-log.txt"), log); } catch { }
        desktop.Shutdown(exitCode);
    }

    private static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
