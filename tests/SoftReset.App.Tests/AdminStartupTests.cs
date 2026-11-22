using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SoftReset.App.Services;

namespace SoftReset.App.Tests;

/// <summary>Asking for admin rights as soon as the app opens.</summary>
public class AdminStartupTests
{
    private static readonly StartupOptions Normal = new();

    [Fact]
    public void Asks_on_windows_when_not_admin_yet()
    {
        Assert.True(AdminStartup.ShouldAsk(Array.Empty<string>(), Normal, isWindows: true, isElevated: false, settingOn: true));
    }

    [Theory]
    [InlineData(false, false, true, "")]                    // not Windows
    [InlineData(true, true, true, "")]                      // already admin
    [InlineData(true, false, false, "")]                    // turned off in About
    [InlineData(true, false, true, "--relaunched-elevated")] // the admin copy never asks again (no loops)
    [InlineData(true, false, true, "--no-admin")]
    public void Does_not_ask_when_it_should_not(bool isWindows, bool isElevated, bool settingOn, string arg)
    {
        var args = arg.Length > 0 ? new[] { arg } : Array.Empty<string>();
        Assert.False(AdminStartup.ShouldAsk(args, Normal, isWindows, isElevated, settingOn));
    }

    [Fact]
    public void Does_not_ask_in_demo_or_page_capture_mode()
    {
        Assert.False(AdminStartup.ShouldAsk(Array.Empty<string>(), new StartupOptions { Demo = true }, true, false, true));
        Assert.False(AdminStartup.ShouldAsk(Array.Empty<string>(), new StartupOptions { CaptureDirectory = "x" }, true, false, true));
    }

    [Fact]
    public void Admin_copy_gets_the_same_arguments_with_absolute_data_path_and_loop_guard()
    {
        var args = AdminStartup.BuildArguments(new[] { "--data", "my data" });
        Assert.EndsWith(AdminStartup.RelaunchedFlag, args);
        Assert.Contains(AdminStartup.Quote(Path.GetFullPath("my data")), args);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\My Folder\", "\"C:\\My Folder\\\\\"")]   // trailing backslash doubled before the closing quote
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Arguments_are_quoted_for_the_windows_command_line(string input, string expected)
    {
        Assert.Equal(expected, AdminStartup.Quote(input));
    }

    [AvaloniaFact]
    public async Task Saying_no_shows_a_banner_and_give_admin_rights_switches()
    {
        using var h = new UiHarness(adminDeclined: true);
        await h.Vm.ActivateCurrentAsync();
        Assert.True(h.Vm.ShowAdminBanner);
        Assert.True(h.Find<Border>("AdminBanner").IsVisible);

        h.Click(h.Find<Button>("GiveAdminButton"));
        Assert.True(h.Platform.IsElevated);
    }

    [AvaloniaFact]
    public async Task No_banner_when_admin_was_not_declined()
    {
        using var h = new UiHarness();
        await h.Vm.ActivateCurrentAsync();
        Assert.False(h.Vm.ShowAdminBanner);
    }

    [AvaloniaFact]
    public async Task Setting_can_turn_the_prompt_off()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Data);
        Assert.True(h.Find<CheckBox>("AskAdminCheck").IsChecked);
        h.Click(h.Find<CheckBox>("AskAdminCheck"));
        h.Click(h.Find<Button>("SaveSettingsButton"));
        Assert.False(h.Services.Settings.AskForAdminOnStart);
        Assert.False(AdminStartup.ReadSetting(new StartupOptions { DataDirectory = h.Services.Paths.Root }));
    }
}
