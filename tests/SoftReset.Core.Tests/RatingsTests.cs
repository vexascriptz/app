using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;

namespace SoftReset.Core.Tests;

public class RatingsTests
{
    [Theory]
    [InlineData(45, RatingLevel.Good, "Good")]
    [InlineData(78, RatingLevel.Okay, "Warm")]
    [InlineData(88, RatingLevel.Warning, "Hot")]
    [InlineData(95, RatingLevel.Bad, "Too hot")]
    public void Gpu_temperature(double c, RatingLevel level, string label)
    {
        var r = Ratings.GpuTemperature(c);
        Assert.Equal(level, r.Level);
        Assert.Equal(label, r.Label);
        Assert.False(string.IsNullOrWhiteSpace(r.Meaning));
    }

    [Fact]
    public void Missing_readings_say_unknown_instead_of_guessing()
    {
        Assert.Equal(RatingLevel.Unknown, Ratings.GpuTemperature(null).Level);
        Assert.Equal(RatingLevel.Unknown, Ratings.BoardTemperature(null).Level);
        Assert.Equal(RatingLevel.Unknown, Ratings.DiskTemperature(null, "SSD").Level);
        Assert.Equal(RatingLevel.Unknown, Ratings.Battery(null).Level);
        Assert.Equal(RatingLevel.Unknown, Ratings.DriverAge(null, DateTimeOffset.Now).Level);
        Assert.Equal(RatingLevel.Unknown, Ratings.Antivirus(new SecurityStatus()).Level);
        Assert.Contains("extra drivers", Ratings.BoardTemperature(null).Meaning);
    }

    [Theory]
    [InlineData(50, "HDD", RatingLevel.Okay)]   // hard drives run cooler, so 50 °C is already warm
    [InlineData(50, "SSD", RatingLevel.Good)]
    [InlineData(62, "HDD", RatingLevel.Bad)]
    [InlineData(72, "SSD", RatingLevel.Warning)]
    public void Disk_temperature_depends_on_drive_type(int c, string type, RatingLevel level) =>
        Assert.Equal(level, Ratings.DiskTemperature(c, type).Level);

    [Theory]
    [InlineData(200, 500, RatingLevel.Good)]
    [InlineData(60, 500, RatingLevel.Okay)]
    [InlineData(30, 500, RatingLevel.Warning)]
    [InlineData(4, 500, RatingLevel.Bad)]
    [InlineData(12, 2000, RatingLevel.Warning)]
    [InlineData(90, 2000, RatingLevel.Okay)]    // big drive: 90 GB free is only 4.5 % but not alarming
    [InlineData(20, 64, RatingLevel.Good)]      // small drive: 20 GB of 64 is plenty
    public void Free_space(int freeGb, int totalGb, RatingLevel level) =>
        Assert.Equal(level, Ratings.FreeSpace(freeGb * 1_000_000_000L, totalGb * 1_000_000_000L).Level);

    [Fact]
    public void Disk_health()
    {
        Assert.Equal("Failing", Ratings.DiskHealth(new PhysicalDiskInfo { HealthStatus = "Healthy", PredictFailure = true }).Label);
        Assert.Equal("Worn out", Ratings.DiskHealth(new PhysicalDiskInfo { HealthStatus = "Healthy", WearPercent = 93 }).Label);
        Assert.Equal("Has errors", Ratings.DiskHealth(new PhysicalDiskInfo { HealthStatus = "Healthy", ReadErrorsUncorrected = 3 }).Label);
        Assert.Equal("Wearing out", Ratings.DiskHealth(new PhysicalDiskInfo { HealthStatus = "Healthy", WearPercent = 75 }).Label);
        var ok = Ratings.DiskHealth(new PhysicalDiskInfo { HealthStatus = "Healthy", WearPercent = 4 });
        Assert.Equal(RatingLevel.Good, ok.Level);
        Assert.Contains("4% worn", ok.Meaning);
        Assert.Equal(RatingLevel.Unknown, Ratings.DiskHealth(new PhysicalDiskInfo()).Level);
    }

    [Theory]
    [InlineData(20, "Normal")]
    [InlineData(70, "Busy")]
    [InlineData(97, "Maxed out")]
    public void Cpu_usage(double pct, string label) => Assert.Equal(label, Ratings.CpuUsage(pct).Label);

    [Theory]
    [InlineData(40, "Normal")]
    [InlineData(78, "Busy")]
    [InlineData(92, "Nearly full")]
    public void Memory_usage(double pct, string label) => Assert.Equal(label, Ratings.MemoryUsage(pct).Label);

    [Fact]
    public void Refresh_rate_flags_a_monitor_not_at_its_max()
    {
        Assert.Equal(RatingLevel.Warning, Ratings.RefreshRate(60, 144).Level);
        Assert.Contains("144", Ratings.RefreshRate(60, 144).Meaning);
        Assert.Equal("Best", Ratings.RefreshRate(144, 144).Label);
    }

    [Fact]
    public void Unsigned_is_never_called_dangerous()
    {
        var r = Ratings.Signature(SignatureState.Unsigned, null);
        Assert.Equal(RatingLevel.Okay, r.Level);
        Assert.DoesNotContain("malware", r.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("virus", r.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Verified", Ratings.Signature(SignatureState.Signed, "Valve").Label);
        Assert.Contains("Valve", Ratings.Signature(SignatureState.Signed, "Valve").Meaning);
    }

    [Fact]
    public void Security_and_updates()
    {
        Assert.Equal(RatingLevel.Bad, Ratings.Antivirus(new SecurityStatus { DefenderRealTimeEnabled = false }).Level);
        Assert.Equal(RatingLevel.Good, Ratings.Antivirus(new SecurityStatus { DefenderRealTimeEnabled = false, ThirdPartyAntivirus = { "Bitdefender" } }).Level);
        Assert.Equal("Partly off", Ratings.Firewall(new SecurityStatus { FirewallProfiles = { ["Domain"] = true, ["Public"] = false } }).Label);
        var now = DateTimeOffset.Now;
        Assert.Equal("Up to date", Ratings.Updates(new UpdateStatus { LastSuccessfulInstall = now.AddDays(-5) }, now).Label);
        Assert.Equal("Out of date", Ratings.Updates(new UpdateStatus { LastSuccessfulInstall = now.AddDays(-120) }, now).Label);
        Assert.Equal("Restart needed", Ratings.Updates(new UpdateStatus { RebootPending = true }, now).Label);
    }

    [Fact]
    public void Power_plan_matches_what_auto_optimize_does()
    {
        Assert.Equal(RatingLevel.Warning, Ratings.PowerPlan(new PowerStatus { ActiveSchemeGuid = WellKnownPowerSchemes.PowerSaver }, isLaptop: false).Level);
        Assert.Equal(RatingLevel.Okay, Ratings.PowerPlan(new PowerStatus { ActiveSchemeGuid = WellKnownPowerSchemes.Balanced }, isLaptop: false).Level);
        Assert.Equal(RatingLevel.Good, Ratings.PowerPlan(new PowerStatus { ActiveSchemeGuid = WellKnownPowerSchemes.Balanced }, isLaptop: true).Level);
        Assert.Equal(RatingLevel.Good, Ratings.PowerPlan(new PowerStatus { ActiveSchemeGuid = WellKnownPowerSchemes.HighPerformance }, isLaptop: false).Level);
        Assert.Equal("Speed capped", Ratings.PowerPlan(new PowerStatus { ActiveSchemeGuid = WellKnownPowerSchemes.HighPerformance, ProcessorMaxStateAc = 50 }, false).Label);
    }

    [Fact]
    public async Task Checkup_from_the_demo_pc_rates_every_line_and_offers_fixes_only_for_problems()
    {
        using var rig = new TestRig();
        var snap = await rig.ScanAsync();
        var items = PcCheckup.Build(snap, DateTimeOffset.Now);

        Assert.Contains(items, i => i.Group == PcCheckup.Temperatures);
        Assert.Contains(items, i => i.Group == PcCheckup.Storage && i.Name.StartsWith("Drive"));
        Assert.Contains(items, i => i.Name == "Antivirus");
        Assert.Contains(items, i => i.Name == "Apps starting with Windows");
        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Name));
            Assert.False(string.IsNullOrWhiteSpace(i.Value));
            Assert.False(string.IsNullOrWhiteSpace(i.Rating.Label));
            Assert.False(string.IsNullOrWhiteSpace(i.Rating.Meaning));
            if (i.ShowFix) Assert.NotEqual(RatingLevel.Good, i.Rating.Level);
        });

        // The demo PC is set up with known problems: power saver on a desktop and a 60 Hz monitor that can do more.
        Assert.Contains(items, i => i.Name == "Power plan" && i.Rating.IsProblem && i.ShowFix);
        Assert.Contains(items, i => i.Fix == CheckupFix.SmootherDisplay && i.ShowFix);
    }

    [Fact]
    public void Checkup_on_a_pc_with_no_sensors_still_works()
    {
        var items = PcCheckup.Build(new SystemSnapshot(), DateTimeOffset.Now);
        Assert.Contains(items, i => i.Name == "CPU" && i.Rating.Level == RatingLevel.Unknown);
        Assert.All(items, i => Assert.False(i.ShowFix && i.Rating.Level == RatingLevel.Unknown));
    }
}

