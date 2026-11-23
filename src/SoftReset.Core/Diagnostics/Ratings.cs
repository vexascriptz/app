using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics;

/// <summary>How a reading looks, from best to worst. Unknown = Windows didn't report it.</summary>
public enum RatingLevel { Good, Okay, Warning, Bad, Unknown }

/// <summary>A plain-language verdict for one reading: a one-word label and one short line saying what it means.</summary>
public sealed record Rating(RatingLevel Level, string Label, string Meaning)
{
    public static Rating Unknown(string meaning = "Windows doesn't report this on this PC.") => new(RatingLevel.Unknown, "Unknown", meaning);
    public bool IsProblem => Level is RatingLevel.Warning or RatingLevel.Bad;
}

/// <summary>
/// Turns raw numbers into Good / Warm / Hot / Low / Problem labels with one line of explanation.
/// Thresholds are deliberately conservative and based on common manufacturer guidance; a rating
/// is never a diagnosis on its own (e.g. "Not signed" is not "malware").
/// </summary>
public static class Ratings
{
    private static Rating Good(string label, string meaning) => new(RatingLevel.Good, label, meaning);
    private static Rating Okay(string label, string meaning) => new(RatingLevel.Okay, label, meaning);
    private static Rating Warn(string label, string meaning) => new(RatingLevel.Warning, label, meaning);
    private static Rating Bad(string label, string meaning) => new(RatingLevel.Bad, label, meaning);
    private static Rating Unknown(string meaning = "Windows doesn't report this on this PC.") => Rating.Unknown(meaning);

    // ------------------------------------------------------------------ live use

    public static Rating CpuUsage(double percent) => percent switch
    {
        < 50 => Good("Normal", "Plenty of power left."),
        < 85 => Okay("Busy", "Working hard. Normal while gaming or installing."),
        _ => Warn("Maxed out", "At its limit, so things may feel slow."),
    };

    public static Rating MemoryUsage(double percent) => percent switch
    {
        < 70 => Good("Normal", "Plenty of memory free."),
        < 85 => Okay("Busy", "Fine. Close some apps if things feel slow."),
        _ => Warn("Nearly full", "Close programs, or the PC will slow down."),
    };

    public static Rating ProcessCpu(double percent) => percent switch
    {
        < 25 => Good("Light", "Not using much CPU."),
        < 60 => Okay("Heavy", "Using a lot of CPU right now."),
        _ => Warn("Very heavy", "Using most of the CPU. Close it if you don't need it."),
    };

    public static Rating ProcessMemory(long bytes, long totalBytes)
    {
        var share = totalBytes > 0 ? (double)bytes / totalBytes : 0;
        if (bytes < 1L << 30 && share < 0.10) return Good("Light", "Not using much memory.");
        if (bytes < 3L << 30 && share < 0.25) return Okay("Heavy", "Using a lot of memory.");
        return Warn("Very heavy", "Using a big part of your memory.");
    }

    // ------------------------------------------------------------------ temperatures

    public static Rating GpuTemperature(double? c) => c switch
    {
        null => Unknown("This graphics card doesn't report its temperature."),
        < 75 => Good("Good", "Cool. No problem."),
        < 85 => Okay("Warm", "Normal while gaming."),
        < 92 => Warn("Hot", "Running hot. Check the fans and clean out dust."),
        _ => Bad("Too hot", "So hot it slows itself down. Clean fans, improve airflow."),
    };

    /// <summary>ACPI thermal zone: usually a motherboard sensor, sometimes near the CPU. Approximate.</summary>
    public static Rating BoardTemperature(double? c) => c switch
    {
        null => Unknown("Windows can't read the CPU temperature without extra drivers."),
        < 70 => Good("Good", "Cool. No problem."),
        < 85 => Okay("Warm", "Normal under load."),
        < 95 => Warn("Hot", "Running hot. Check the fans and clean out dust."),
        _ => Bad("Too hot", "Overheating. Clean fans and check airflow."),
    };

    public static Rating DiskTemperature(int? c, string mediaType)
    {
        if (c is null) return Unknown("This drive doesn't report its temperature.");
        var hdd = mediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase);
        var (warm, hot, tooHot) = hdd ? (45, 55, 60) : (55, 70, 80);
        if (c < warm) return Good("Good", "Cool. No problem.");
        if (c < hot) return Okay("Warm", "Fine. Normal during heavy use.");
        if (c < tooHot) return Warn("Hot", "Running hot. Improve airflow around the drive.");
        return Bad("Too hot", "Overheating. It can slow down or wear out faster.");
    }

    public static Rating Throttling(int events) => events switch
    {
        0 => Good("Good", "The CPU hasn't slowed down from heat."),
        < 5 => Okay("A few times", $"The CPU slowed down {events} time(s) recently. Usually heat or power limits."),
        _ => Warn("Often", $"The CPU slowed down {events} times recently. Check cooling and the power plan."),
    };

    // ------------------------------------------------------------------ storage

    public static Rating FreeSpace(long freeBytes, long totalBytes)
    {
        if (totalBytes <= 0) return Unknown();
        var pct = 100.0 * freeBytes / totalBytes;
        var gb = freeBytes / 1e9;
        // Free gigabytes matter more than percent: 90 GB free on a 2 TB drive is fine.
        if (gb < 8) return Bad("Almost full", "Free up space now. Windows needs room to run and update.");
        if (gb < 15 || (pct < 8 && gb < 60)) return Warn("Low", "Free up some space soon.");
        if (pct < 15 && gb < 100) return Okay("Getting full", "Fine for now.");
        return Good("Plenty", "Lots of free space.");
    }

    public static Rating DiskHealth(PhysicalDiskInfo d)
    {
        if (d.PredictFailure == true || d.HealthStatus.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
            return Bad("Failing", "Back up your files now and replace this drive.");
        if (d.WearPercent >= 90) return Bad("Worn out", "Near the end of its life. Back up and plan to replace it.");
        if (d.HealthStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase) || d.ReadErrorsUncorrected > 0)
            return Warn("Has errors", "Back up your files soon.");
        if (d.WearPercent >= 70) return Warn("Wearing out", "Still works. Keep backups up to date.");
        if (d.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            return Good("Healthy", d.WearPercent is { } w ? $"Working fine. {w}% worn." : "Working fine.");
        return Unknown("This drive doesn't report its health.");
    }

    // ------------------------------------------------------------------ hardware

    public static Rating Battery(BatteryInfo? b)
    {
        if (b?.WearPercent is not { } wear) return Unknown("Battery health isn't reported.");
        return wear switch
        {
            < 20 => Good("Healthy", $"Holds {100 - wear:0}% of its original charge."),
            < 40 => Okay("Some wear", $"Holds {100 - wear:0}% of its original charge. Normal for an older laptop."),
            _ => Warn("Worn", $"Holds only {100 - wear:0}% of its original charge. Consider a new battery."),
        };
    }

    public static Rating RefreshRate(int currentHz, int maxHz)
    {
        if (currentHz <= 0) return Unknown();
        if (maxHz > currentHz) return Warn("Not at max", $"Your monitor can do {maxHz} Hz. Higher is smoother.");
        return Good("Best", "Already at the smoothest setting.");
    }

    /// <summary>Driver age only. The app can't know if a newer driver exists for every card.</summary>
    public static Rating DriverAge(DateTimeOffset? driverDate, DateTimeOffset now)
    {
        if (driverDate is not { } d) return Unknown("Driver date isn't reported.");
        var days = (now - d).TotalDays;
        if (days < 365) return Good("Recent", "Less than a year old.");
        if (days < 730) return Okay("Old", "Over a year old. Check for a newer driver.");
        return Warn("Very old", "Over two years old. Update it from the maker's website.");
    }

    public static Rating GpuLoad(double? percent) => percent switch
    {
        null => Unknown(),
        < 10 => Good("Idle", "Not doing much right now."),
        < 70 => Good("Normal", "Working normally."),
        _ => Okay("Busy", "Working hard. Normal while gaming."),
    };

    public static Rating VideoMemory(long? used, long? total)
    {
        if (used is not { } u || total is not > 0) return Unknown();
        var pct = 100.0 * u / total.Value;
        if (pct < 80) return Good("Normal", "Plenty of video memory free.");
        if (pct < 95) return Okay("Nearly full", "Lower texture quality if games stutter.");
        return Warn("Full", "Games may stutter. Lower texture quality.");
    }

    public static Rating Signature(SignatureState s, string? publisher) => s switch
    {
        SignatureState.SignedMicrosoftWindows => Good("Windows", "Part of Windows, signed by Microsoft."),
        SignatureState.Signed => Good("Verified", publisher is { Length: > 0 } ? $"Signed by {publisher}." : "Signed by its maker."),
        SignatureState.Unsigned => Okay("Not signed", "Common for small apps. Make sure you know what it is."),
        SignatureState.Expired => Okay("Old signature", "Signed, but the certificate has expired. Usually just an old app."),
        SignatureState.InvalidOrUntrusted or SignatureState.Revoked => Warn("Bad signature", "Its signature doesn't check out. Look into it."),
        _ => Unknown("Couldn't check who made it."),
    };

    // ------------------------------------------------------------------ Windows

    public static Rating HealthScore(int score) => score switch
    {
        >= 85 => Good("Good", "No big problems."),
        >= 65 => Okay("Fair", "A few things to fix."),
        >= 40 => Warn("Needs attention", "Several problems found."),
        _ => Bad("Has problems", "Serious problems found. Fix them first."),
    };

    public static Rating Uptime(TimeSpan? uptime)
    {
        if (uptime is not { } u) return Unknown();
        if (u.TotalDays < 7) return Good("Recent", "Restarted recently.");
        if (u.TotalDays < 30) return Okay("Restart soon", "A restart clears out slowdowns. Shut down doesn't count, use Restart.");
        return Warn("Restart needed", "Not restarted in a month. Restart to finish updates and clear slowdowns.");
    }

    public static Rating StartupApps(int enabledNonMicrosoft) => enabledNonMicrosoft switch
    {
        <= 6 => Good("Normal", "Not many apps start with Windows."),
        <= 12 => Okay("A lot", "Startup could be faster."),
        _ => Warn("Too many", "Lots of apps start with Windows and slow it down."),
    };

    public static Rating Antivirus(SecurityStatus s)
    {
        if (s.DefenderRealTimeEnabled == true) return Good("On", "Windows Security is protecting this PC.");
        if (s.ThirdPartyAntivirus.Count > 0) return Good("On", $"Protected by {s.ThirdPartyAntivirus[0]}.");
        if (s.DefenderRealTimeEnabled == false) return Bad("Off", "Nothing is protecting this PC. Turn on Windows Security.");
        return Unknown("Couldn't read antivirus status.");
    }

    public static Rating Firewall(SecurityStatus s)
    {
        if (s.FirewallProfiles.Count == 0) return Unknown("Couldn't read firewall status.");
        if (s.FirewallProfiles.Values.All(v => v)) return Good("On", "The firewall is on.");
        if (s.FirewallProfiles.Values.Any(v => v)) return Warn("Partly off", "The firewall is off for some networks.");
        return Bad("Off", "The firewall is off. Turn it on in Windows Security.");
    }

    public static Rating Updates(UpdateStatus u, DateTimeOffset now)
    {
        if (u.RebootPending == true) return Okay("Restart needed", "Updates are waiting for a restart.");
        if (u.UpdatesPausedByPolicyOrUser == true) return Okay("Paused", "Updates are paused.");
        if (u.LastSuccessfulInstall is not { } last) return Unknown("Couldn't read update history.");
        var days = (now - last).TotalDays;
        if (days < 45) return Good("Up to date", "Updates installed recently.");
        if (days < 90) return Okay("Behind", "No updates for over a month. Check Windows Update.");
        return Warn("Out of date", "No updates for months. Open Windows Update.");
    }

    public static Rating Internet(NetworkStatus n)
    {
        if (n.Connectivity?.InternetReachable == true) return Good("Connected", "Internet is working.");
        var up = n.Adapters.Any(a => a.IsUp && a.Gateways.Count > 0);
        if (up && n.Connectivity?.InternetReachable == false) return Warn("No internet", "Connected to the network, but the internet isn't reachable.");
        if (up) return Good("Connected", "Connected to a network.");
        return Bad("Not connected", "No network connection.");
    }

    public static Rating WifiSignal(int? percent) => percent switch
    {
        null => Unknown(),
        >= 70 => Good("Strong", "Good Wi-Fi signal."),
        >= 40 => Okay("Fair", "OK signal. Moving closer to the router helps."),
        _ => Warn("Weak", "Weak signal. Expect slow, dropping internet."),
    };

    public static Rating PowerPlan(PowerStatus p, bool isLaptop)
    {
        if (p.ProcessorMaxStateAc is < 100) return Warn("Speed capped", $"The CPU is limited to {p.ProcessorMaxStateAc}% speed.");
        var g = p.ActiveSchemeGuid;
        if (g.Equals(WellKnownPowerSchemes.PowerSaver, StringComparison.OrdinalIgnoreCase))
            return isLaptop ? Okay("Battery saver", "Saves battery but slows the PC.") : Warn("Slow", "Power saver slows a desktop down for no reason.");
        if (g.Equals(WellKnownPowerSchemes.HighPerformance, StringComparison.OrdinalIgnoreCase) || g.Equals(WellKnownPowerSchemes.UltimatePerformance, StringComparison.OrdinalIgnoreCase)
            || p.ActiveSchemeName.Contains("performance", StringComparison.OrdinalIgnoreCase))
            return Good("Fastest", "Set for best speed.");
        return isLaptop ? Good("Balanced", "Best for laptops.") : Okay("Balanced", "Fine. Auto optimize can make it faster.");
    }
}
