using System.Globalization;
using System.Text.RegularExpressions;
using SoftReset.Core.Models;

namespace SoftReset.Core.Parsing;

/// <summary>Parses the output of Windows command-line tools. All parsers are tolerant of missing fields.</summary>
public static partial class ToolOutputParsers
{
    // ------------------------------------------------------------- powercfg /list

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.*?)\)\s*(\*)?\s*$")]
    private static partial Regex PowerSchemeLine();

    public static List<PowerScheme> ParsePowercfgList(string output)
    {
        var list = new List<PowerScheme>();
        foreach (var raw in output.Split('\n'))
        {
            var m = PowerSchemeLine().Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            list.Add(new PowerScheme
            {
                Guid = m.Groups[1].Value.ToLowerInvariant(),
                Name = m.Groups[2].Value.Trim(),
                Active = m.Groups[3].Success,
            });
        }
        return list;
    }

    // ------------------------------------------------------------- netsh wlan show interfaces

    public static WifiInfo? ParseNetshWlanInterfaces(string output, out string? problem)
    {
        problem = null;
        if (output.Contains("location permission", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("ms-settings:privacy-location", StringComparison.OrdinalIgnoreCase))
        {
            problem = "Windows requires Location permission for desktop apps to read Wi-Fi details (Windows 11 24H2 and later).";
            return null;
        }
        if (output.Contains("is not running", StringComparison.OrdinalIgnoreCase))
        {
            problem = "The WLAN AutoConfig service is not running (no Wi-Fi adapter, or Wi-Fi is disabled).";
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var idx = line.IndexOf(" : ", StringComparison.Ordinal);
            if (idx < 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 3)..].Trim();
            fields.TryAdd(key, val);
        }
        if (!fields.TryGetValue("SSID", out var ssid) || string.IsNullOrEmpty(ssid))
            return null;

        var wifi = new WifiInfo { Ssid = ssid };
        foreach (var (k, v) in fields)
        {
            if (v.EndsWith('%') && int.TryParse(v.TrimEnd('%'), out var pct)) wifi.SignalPercent = pct;
            else if (k.StartsWith("Radio", StringComparison.OrdinalIgnoreCase)) wifi.RadioType = v;
            else if (k.Equals("Band", StringComparison.OrdinalIgnoreCase)) wifi.Band = v;
            else if (k.Equals("Channel", StringComparison.OrdinalIgnoreCase)) wifi.Channel = v;
            else if (k.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase)) wifi.Authentication = v;
            else if (k.Contains("(Mbps)", StringComparison.OrdinalIgnoreCase) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                if (k.StartsWith("Receive", StringComparison.OrdinalIgnoreCase)) wifi.ReceiveRateMbps = rate;
                else if (k.StartsWith("Transmit", StringComparison.OrdinalIgnoreCase)) wifi.TransmitRateMbps = rate;
            }
        }
        return wifi;
    }

    // ------------------------------------------------------------- nvidia-smi

    public const string NvidiaSmiQuery =
        "--query-gpu=name,driver_version,temperature.gpu,utilization.gpu,utilization.memory,memory.used,memory.total,power.draw,power.limit,fan.speed,clocks.gr,clocks.mem,pstate,pcie.link.gen.current,pcie.link.width.current --format=csv,noheader,nounits";

    public static List<(string Name, string Driver, GpuTelemetry Telemetry)> ParseNvidiaSmi(string output)
    {
        var result = new List<(string, string, GpuTelemetry)>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var c = line.Split(',').Select(s => s.Trim()).ToArray();
            if (c.Length < 13) continue;
            double? D(int i) => i < c.Length && double.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            var t = new GpuTelemetry
            {
                Source = "nvidia-smi",
                TemperatureC = D(2),
                UtilizationPercent = D(3),
                MemoryUtilizationPercent = D(4),
                MemoryUsedBytes = D(5) is { } mu ? (long)(mu * 1024 * 1024) : null,
                MemoryTotalBytes = D(6) is { } mt ? (long)(mt * 1024 * 1024) : null,
                PowerWatts = D(7),
                PowerLimitWatts = D(8),
                FanPercent = D(9) is { } f ? (int)f : null,
                GraphicsClockMHz = D(10) is { } g ? (int)g : null,
                MemoryClockMHz = D(11) is { } m ? (int)m : null,
                PerformanceState = c[12].StartsWith('[') ? null : c[12],
                PcieLink = c.Length > 14 && D(13) is { } gen && D(14) is { } w ? $"PCIe Gen{gen} x{w}" : null,
            };
            result.Add((c[0], c[1], t));
        }
        return result;
    }

    // ------------------------------------------------------------- winget upgrade

    public sealed record WingetUpgrade(string Name, string Id, string Version, string Available, string Source);

    public static List<WingetUpgrade> ParseWingetUpgrade(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .ToList();
        var sep = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(ch => ch == '-'));
        var list = new List<WingetUpgrade>();
        if (sep < 1) return list;

        var header = lines[sep - 1];
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        }
        if (starts.Count < 4) return list;

        for (var i = sep + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) break;
            if (line.Length < starts[^1]) break; // footer such as "3 upgrades available."
            string Col(int idx)
            {
                var s = starts[idx];
                var e = idx + 1 < starts.Count ? starts[idx + 1] : line.Length;
                if (s >= line.Length) return "";
                return line[s..Math.Min(e, line.Length)].Trim();
            }
            var item = new WingetUpgrade(Col(0), Col(1), Col(2), Col(3), starts.Count > 4 ? Col(4) : "");
            if (item.Id.Length == 0 || item.Id.Contains(' ')) continue;
            list.Add(item);
        }
        return list;
    }

    // ------------------------------------------------------------- Bytes

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null) return "Unknown";
        var b = (double)bytes.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var u = 0;
        while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
        return u == 0 ? $"{b:0} {units[u]}" : $"{b:0.#} {units[u]}";
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
        return $"{t.TotalSeconds:0.#} s";
    }
}
