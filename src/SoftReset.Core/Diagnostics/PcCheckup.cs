using SoftReset.Core.Models;
using SoftReset.Core.Parsing;

namespace SoftReset.Core.Diagnostics;

/// <summary>What the button next to a check-up line does.</summary>
public enum CheckupFix
{
    None,
    FreeUpSpace,       // Auto optimize: cleanup
    SmootherDisplay,   // Auto optimize: graphics
    StartupApps,       // Auto optimize: startup
    PowerPlan,         // Auto optimize: power
    OpenSecurity,
    OpenUpdates,
    OpenBackup,
    OpenNetwork,
    OpenGraphics,      // the Graphics page (drivers)
}

/// <summary>One line of the PC check-up: a name, its value, a Good/Bad rating, and an optional fix.</summary>
public sealed record CheckupItem(string Group, string Name, string Value, Rating Rating, CheckupFix Fix = CheckupFix.None)
{
    /// <summary>Only offer the button when there's something to improve.</summary>
    public bool ShowFix => Fix != CheckupFix.None && Rating.Level is RatingLevel.Okay or RatingLevel.Warning or RatingLevel.Bad;
}

/// <summary>Builds the plain-language PC check-up shown on Home from one scan.</summary>
public static class PcCheckup
{
    public const string Temperatures = "Temperatures";
    public const string Storage = "Storage";
    public const string Speed = "Speed";
    public const string Safety = "Security";
    public const string Windows = "Windows";

    public static List<CheckupItem> Build(SystemSnapshot s, DateTimeOffset now)
    {
        var items = new List<CheckupItem>();

        // ---- Temperatures
        foreach (var g in s.Gpus.Where(g => g.Telemetry?.TemperatureC is not null))
            items.Add(new(Temperatures, Short(g.Name), Deg(g.Telemetry!.TemperatureC), Ratings.GpuTemperature(g.Telemetry.TemperatureC), CheckupFix.OpenGraphics));
        var board = s.Thermal.Readings.Where(r => !s.Gpus.Any(g => g.Name == r.Sensor)).OrderByDescending(r => r.Celsius).FirstOrDefault();
        items.Add(board is null
            ? new(Temperatures, "CPU", "Not reported", Ratings.BoardTemperature(null))
            : new(Temperatures, "Motherboard sensor", Deg(board.Celsius) + " (approx.)", Ratings.BoardTemperature(board.Celsius)));
        foreach (var d in s.PhysicalDisks.Where(d => d.TemperatureC is not null))
            items.Add(new(Temperatures, Short(d.FriendlyName), Deg(d.TemperatureC), Ratings.DiskTemperature(d.TemperatureC, d.MediaType)));
        items.Add(new(Temperatures, "Heat slowdowns", s.Thermal.ThrottlingEvents == 0 ? "None" : $"{s.Thermal.ThrottlingEvents} recently", Ratings.Throttling(s.Thermal.ThrottlingEvents)));

        // ---- Storage
        foreach (var d in s.Drives.Where(d => d.DriveType == "Fixed" && d.TotalBytes > 0).OrderByDescending(d => d.IsSystemDrive))
            items.Add(new(Storage, $"Drive {d.Name.TrimEnd('\\')}", $"{Size(d.FreeBytes)} free of {Size(d.TotalBytes)}", Ratings.FreeSpace(d.FreeBytes, d.TotalBytes),
                d.IsSystemDrive ? CheckupFix.FreeUpSpace : CheckupFix.None));
        foreach (var d in s.PhysicalDisks)
        {
            var r = Ratings.DiskHealth(d);
            items.Add(new(Storage, Short(d.FriendlyName), r.Level == RatingLevel.Unknown ? "Not reported" : d.MediaType is "SSD" or "HDD" ? d.MediaType : "Drive", r,
                r.Level is RatingLevel.Warning or RatingLevel.Bad ? CheckupFix.OpenBackup : CheckupFix.None));
        }

        // ---- Speed
        var startup = s.StartupEntries.Count(e => e.Enabled && !e.IsMicrosoft && e.Location != StartupLocation.Service);
        items.Add(new(Speed, "Apps starting with Windows", startup.ToString(), Ratings.StartupApps(startup), CheckupFix.StartupApps));
        if (!string.IsNullOrEmpty(s.Power.ActiveSchemeName) || s.Power.ProcessorMaxStateAc is not null)
            items.Add(new(Speed, "Power plan", s.Power.ActiveSchemeName, Ratings.PowerPlan(s.Power, s.Hardware.IsLaptop), CheckupFix.PowerPlan));
        foreach (var d in s.Displays.Where(d => d.RefreshHz > 0))
        {
            var max = Math.Max(d.RefreshHz, d.MaxRefreshHzAtCurrentResolution);
            items.Add(new(Speed, string.IsNullOrWhiteSpace(d.MonitorName) ? "Monitor" : Short(d.MonitorName),
                max > d.RefreshHz ? $"{d.RefreshHz} Hz (can do {max} Hz)" : $"{d.RefreshHz} Hz", Ratings.RefreshRate(d.RefreshHz, max), CheckupFix.SmootherDisplay));
        }
        foreach (var g in s.Gpus.Where(g => g.Vendor is not (GpuVendor.Microsoft or GpuVendor.Unknown)))
            items.Add(new(Speed, $"{Short(g.Name)} driver", g.DriverDate is { } dd ? dd.ToString("MMM yyyy") : "Unknown date", Ratings.DriverAge(g.DriverDate, now), CheckupFix.OpenGraphics));

        // ---- Security
        var av = Ratings.Antivirus(s.Security);
        var avName = s.Security.DefenderRealTimeEnabled == true ? "Windows Security"
            : s.Security.ThirdPartyAntivirus.FirstOrDefault() ?? (av.Level == RatingLevel.Unknown ? "Unknown" : "None running");
        items.Add(new(Safety, "Antivirus", avName, av, CheckupFix.OpenSecurity));
        var fw = s.Security.FirewallProfiles;
        items.Add(new(Safety, "Firewall", fw.Count == 0 ? "Unknown" : fw.Values.All(v => v) ? "All networks" : fw.Values.Any(v => v) ? "Some networks" : "No networks",
            Ratings.Firewall(s.Security), CheckupFix.OpenSecurity));
        var upd = Ratings.Updates(s.Updates, now);
        items.Add(new(Safety, "Windows Update", s.Updates.LastSuccessfulInstall is { } li ? $"Last update {Ago(now - li)}" : "Unknown", upd, CheckupFix.OpenUpdates));

        // ---- Windows
        var up = s.Os.Uptime(now);
        items.Add(new(Windows, "Last restart", up is { } u ? Ago(u) : "Unknown", Ratings.Uptime(up)));
        var net = Ratings.Internet(s.Network);
        var adapter = s.Network.Adapters.FirstOrDefault(a => a.IsUp && a.Gateways.Count > 0);
        var via = s.Network.Wifi is { Ssid.Length: > 0 } w0 ? $"Wi-Fi ({w0.Ssid})" : adapter is null ? "None" : adapter.Type is { Length: > 0 } t ? t : adapter.Name;
        items.Add(new(Windows, "Internet", via, net, CheckupFix.OpenNetwork));
        if (s.Network.Wifi?.SignalPercent is { } sig)
            items.Add(new(Windows, "Wi-Fi signal", $"{sig}%", Ratings.WifiSignal(sig), CheckupFix.OpenNetwork));
        if (s.Hardware.Battery is { } b)
            items.Add(new(Windows, "Battery health", b.WearPercent is { } w ? $"{100 - w:0}% of new" : "Not reported", Ratings.Battery(b)));

        return items;
    }

    /// <summary>Problems first, then the rest, keeping groups together.</summary>
    public static int CountProblems(IEnumerable<CheckupItem> items) => items.Count(i => i.Rating.IsProblem);

    private static string Deg(double? c) => c is { } v ? $"{v:0} °C" : "—";
    private static string Size(long bytes) => ToolOutputParsers.FormatBytes(bytes);

    private static string Ago(TimeSpan t) => t.TotalDays >= 2 ? $"{t.TotalDays:0} days ago"
        : t.TotalHours >= 2 ? $"{t.TotalHours:0} hours ago"
        : "just now";

    /// <summary>Drops vendor noise so names fit: "NVIDIA GeForce RTX 4070" → "GeForce RTX 4070".</summary>
    private static string Short(string name)
    {
        var n = name.Trim();
        foreach (var prefix in new[] { "NVIDIA ", "AMD ", "Intel(R) ", "Microsoft " })
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.Length > prefix.Length + 3) n = n[prefix.Length..];
        return n.Length > 34 ? n[..33] + "…" : n;
    }
}
