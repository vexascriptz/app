using SoftReset.Core.Platform;

namespace SoftReset.Platform.Simulated;

/// <summary>A thread-safe in-memory registry used by tests and demo mode.</summary>
public sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, RegValue>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    public HashSet<string> DeniedWritePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int WriteCount { get; private set; }

    private static string K(RegHive hive, string path) => $"{hive}\\{path.Trim('\\')}";

    public bool KeyExists(RegHive hive, string path, RegView view = RegView.Default)
    {
        lock (_gate)
        {
            var k = K(hive, path);
            return _keys.ContainsKey(k) || _keys.Keys.Any(x => x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
        }
    }

    public RegValue? GetValue(RegHive hive, string path, string name, RegView view = RegView.Default)
    {
        lock (_gate)
            return _keys.TryGetValue(K(hive, path), out var vals) && vals.TryGetValue(name, out var v) ? v : null;
    }

    public void SetValue(RegHive hive, string path, string name, RegValue value, RegView view = RegView.Default)
    {
        lock (_gate)
        {
            if (DeniedWritePaths.Any(d => K(hive, path).StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException($"Access to {K(hive, path)} is denied.");
            var k = K(hive, path);
            if (!_keys.TryGetValue(k, out var vals)) _keys[k] = vals = new Dictionary<string, RegValue>(StringComparer.OrdinalIgnoreCase);
            vals[name] = value;
            WriteCount++;
        }
    }

    public void DeleteValue(RegHive hive, string path, string name, RegView view = RegView.Default)
    {
        lock (_gate)
        {
            if (_keys.TryGetValue(K(hive, path), out var vals)) vals.Remove(name);
            WriteCount++;
        }
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path, RegView view = RegView.Default)
    {
        lock (_gate)
            return _keys.TryGetValue(K(hive, path), out var vals) ? vals.Keys.ToList() : new List<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path, RegView view = RegView.Default)
    {
        lock (_gate)
        {
            var prefix = K(hive, path) + "\\";
            return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k[prefix.Length..].Split('\\')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void Set(RegHive hive, string path, string name, int dword) => SetValue(hive, path, name, RegValue.DWord(dword));
    public void Set(RegHive hive, string path, string name, string s) => SetValue(hive, path, name, RegValue.Str(s));

    /// <summary>Deep copy of all keys — used to assert a rollback restored the exact prior state.</summary>
    public Dictionary<string, Dictionary<string, string>> Dump()
    {
        lock (_gate)
            return _keys.ToDictionary(k => k.Key, k => k.Value.ToDictionary(v => v.Key, v => $"{v.Value.Kind}:{Core.Safety.RegistryBackup.Encode(v.Value)}", StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}
