using SoftReset.Core.Platform;

namespace SoftReset.Core.Safety;

/// <summary>Captures and restores individual registry values, including "value did not exist".</summary>
public static class RegistryBackup
{
    public const string Kind = "registry-value";

    public static BackupItem Capture(IRegistryAccess reg, RegHive hive, string path, string name, string description = "", RegView view = RegView.Default)
    {
        var existing = reg.GetValue(hive, path, name, view);
        var item = new BackupItem
        {
            Kind = Kind,
            Description = string.IsNullOrEmpty(description) ? $@"{HiveName(hive)}\{path}\{name}" : description,
            Data =
            {
                ["hive"] = hive.ToString(),
                ["path"] = path,
                ["name"] = name,
                ["view"] = view.ToString(),
                ["existed"] = (existing is not null).ToString(),
            },
        };
        if (existing is not null)
        {
            item.Data["kind"] = existing.Kind.ToString();
            item.Data["data"] = Encode(existing);
        }
        return item;
    }

    public static void Restore(IRegistryAccess reg, BackupItem item)
    {
        if (item.Kind != Kind) throw new ArgumentException($"Not a registry backup: {item.Kind}");
        var hive = Enum.Parse<RegHive>(item.Get("hive"));
        var view = Enum.TryParse<RegView>(item.Get("view"), out var v) ? v : RegView.Default;
        var path = item.Get("path");
        var name = item.Get("name");
        if (!bool.Parse(item.Get("existed")))
        {
            if (reg.GetValue(hive, path, name, view) is not null)
                reg.DeleteValue(hive, path, name, view);
            return;
        }
        var kind = Enum.Parse<RegKind>(item.Get("kind"));
        reg.SetValue(hive, path, name, Decode(kind, item.Get("data")), view);
    }

    /// <summary>True when the live value matches what the backup recorded.</summary>
    public static bool Matches(IRegistryAccess reg, BackupItem item)
    {
        var hive = Enum.Parse<RegHive>(item.Get("hive"));
        var view = Enum.TryParse<RegView>(item.Get("view"), out var v) ? v : RegView.Default;
        var current = reg.GetValue(hive, item.Get("path"), item.Get("name"), view);
        if (!bool.Parse(item.Get("existed"))) return current is null;
        if (current is null) return false;
        return current.Kind.ToString() == item.Get("kind") && Encode(current) == item.Get("data");
    }

    public static string Encode(RegValue v) => v.Kind switch
    {
        RegKind.DWord => Convert.ToString(v.AsInt() ?? 0),
        RegKind.QWord => Convert.ToString(v.AsLong() ?? 0),
        RegKind.Binary => Convert.ToBase64String(v.AsBytes() ?? Array.Empty<byte>()),
        RegKind.MultiString => string.Join("\u001f", (v.Data as string[]) ?? Array.Empty<string>()),
        _ => v.Data?.ToString() ?? "",
    };

    public static RegValue Decode(RegKind kind, string data) => kind switch
    {
        RegKind.DWord => new RegValue(kind, int.Parse(data)),
        RegKind.QWord => new RegValue(kind, long.Parse(data)),
        RegKind.Binary => new RegValue(kind, Convert.FromBase64String(data)),
        RegKind.MultiString => new RegValue(kind, data.Length == 0 ? Array.Empty<string>() : data.Split('\u001f')),
        _ => new RegValue(kind, data),
    };

    public static string HiveName(RegHive h) => h switch
    {
        RegHive.CurrentUser => "HKCU",
        RegHive.LocalMachine => "HKLM",
        RegHive.ClassesRoot => "HKCR",
        RegHive.Users => "HKU",
        _ => h.ToString(),
    };

    public static RegHive ParseHive(string s) => s.ToUpperInvariant() switch
    {
        "HKCU" or "HKEY_CURRENT_USER" or "CURRENTUSER" => RegHive.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE" or "LOCALMACHINE" => RegHive.LocalMachine,
        "HKCR" or "HKEY_CLASSES_ROOT" or "CLASSESROOT" => RegHive.ClassesRoot,
        "HKU" or "HKEY_USERS" or "USERS" => RegHive.Users,
        _ => throw new ArgumentException($"Unknown registry hive '{s}'"),
    };
}
