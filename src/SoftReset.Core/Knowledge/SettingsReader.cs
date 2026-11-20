using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Knowledge;

public enum SettingAvailability { Available, NotOnThisBuild, NotOnThisEdition, NotWindows }

public sealed class SettingState
{
    public SettingDefinition Definition { get; init; } = null!;
    public SettingAvailability Availability { get; init; }
    public string AvailabilityNote { get; init; } = "";
    public SettingReadResult Current { get; init; } = SettingReadResult.Unreadable("Not read yet");
    public string? PolicyNote { get; init; }
}

/// <summary>Determines whether a catalog setting applies to this PC and reads its current value.</summary>
public sealed class SettingsReader
{
    private readonly ISystemPlatform _platform;
    private readonly string _edition;

    public SettingsReader(ISystemPlatform platform, string edition)
    {
        _platform = platform;
        _edition = edition ?? "";
    }

    public SettingState Evaluate(SettingDefinition def)
    {
        var (availability, note) = CheckAvailability(def);
        if (availability != SettingAvailability.Available)
            return new SettingState { Definition = def, Availability = availability, AvailabilityNote = note, Current = SettingReadResult.Unreadable(note) };

        SettingReadResult current;
        try { current = Read(def.Read); }
        catch (UnauthorizedAccessException) { current = SettingReadResult.Unreadable("Reading this value requires administrator access."); }
        catch (Exception ex) { current = SettingReadResult.Unreadable($"Could not read: {ex.Message}"); }

        string? policy = null;
        if (def.Read.PolicyOverrides is { Count: > 0 })
        {
            foreach (var p in def.Read.PolicyOverrides)
            {
                try
                {
                    var r = Read(p);
                    if (r.Readable && r.Raw is not null)
                    {
                        policy = $"Managed by Group Policy / MDM: {r.Display}. Changes made in Settings may be overridden.";
                        break;
                    }
                }
                catch { /* policy keys are optional */ }
            }
        }

        return new SettingState { Definition = def, Availability = availability, AvailabilityNote = note, Current = current, PolicyNote = policy };
    }

    public (SettingAvailability, string) CheckAvailability(SettingDefinition def)
    {
        if (!_platform.IsWindows && def.Read.Type != "none")
        {
            // Simulated platforms still evaluate settings so the UI can be exercised.
        }
        var build = _platform.OsBuild;
        if (def.MinBuild > 0 && build < def.MinBuild)
            return (SettingAvailability.NotOnThisBuild, $"Requires Windows build {def.MinBuild} or later (this PC: {build}).");
        if (def.MaxBuild is { } max && build > max)
            return (SettingAvailability.NotOnThisBuild, $"Removed after Windows build {max} (this PC: {build}).");
        if (def.Editions is { Count: > 0 } && !string.IsNullOrEmpty(_edition) &&
            !def.Editions.Any(e => _edition.Contains(e, StringComparison.OrdinalIgnoreCase)))
            return (SettingAvailability.NotOnThisEdition, $"Only available on {string.Join(", ", def.Editions)} editions (this PC: {_edition}).");
        return (SettingAvailability.Available, "");
    }

    public SettingReadResult Read(SettingReadSpec spec)
    {
        switch (spec.Type)
        {
            case "registry":
            {
                var hive = RegistryBackup.ParseHive(spec.Hive ?? "HKCU");
                var view = Enum.TryParse<RegView>(spec.View, true, out var v) ? v : RegView.Default;
                var value = _platform.Registry.GetValue(hive, spec.Path ?? "", spec.Value ?? "", view);
                if (value is null)
                {
                    return spec.Missing is null
                        ? new SettingReadResult { Readable = true, Display = "Not configured", Raw = null, Note = "No value is stored, so Windows uses its default." }
                        : new SettingReadResult { Readable = true, Display = spec.Missing, Raw = null, Note = "No value is stored, so Windows uses its default." };
                }
                var raw = RegistryBackup.Encode(value);
                if (spec.Map is not null && spec.Map.TryGetValue(raw, out var label))
                    return SettingReadResult.Value(label, raw);
                if (spec.Map is not null && spec.Map.TryGetValue("*", out var other))
                    return SettingReadResult.Value(other.Replace("{value}", raw), raw);
                return SettingReadResult.Value(raw, raw);
            }
            case "service":
            {
                var svc = _platform.Services.Get(spec.Service ?? "");
                if (svc is null) return SettingReadResult.Unreadable("Service is not installed on this PC.");
                var start = svc.StartMode + (svc.DelayedAutoStart && svc.StartMode == "Auto" ? " (Delayed)" : "");
                return SettingReadResult.Value($"{start}, currently {svc.State}", svc.StartMode);
            }
            case "probe":
                return _platform.SettingProbe.Read(spec.Probe ?? "");
            case "task":
            {
                var task = _platform.Tasks.GetTasks().FirstOrDefault(t => t.Path.Equals(spec.Path, StringComparison.OrdinalIgnoreCase));
                if (task is null) return SettingReadResult.Unreadable("This scheduled task does not exist on this PC.");
                var last = task.LastRun is { } lr ? $", last run {lr.LocalDateTime:g} (result {task.LastResult})" : "";
                return SettingReadResult.Value((task.Enabled ? "Enabled" : "Disabled") + last, task.Enabled ? "1" : "0");
            }
            default:
                return SettingReadResult.Unreadable(spec.Reason ?? "Windows does not expose this value through a documented interface. Use the control below to view it.");
        }
    }
}
