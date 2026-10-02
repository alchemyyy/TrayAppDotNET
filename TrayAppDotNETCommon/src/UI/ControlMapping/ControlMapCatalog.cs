namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>
/// Process-wide registry of control maps. Each generated map registers a factory from a module initializer, so a map
/// is built from its compiled AXAML only when a control first resolves an id the map owns.
/// </summary>
public static class ControlMapCatalog
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, Func<ControlMap>> Factories = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ControlMap> Maps = new(StringComparer.Ordinal);
    private static ControlMapForest? _forest;

    /// <summary>Registers the factory of a generated map; the map's module initializer calls this.</summary>
    public static void Register(string mapName, Func<ControlMap> factory)
    {
        lock (Sync)
        {
            Factories[mapName] = factory;
            Maps.Remove(mapName);
            _forest = null;
        }
    }

    /// <summary>Registers an already built map, replacing any map of the same name.</summary>
    public static void Register(ControlMap map)
    {
        lock (Sync)
        {
            Factories.Remove(map.Name);
            Maps[map.Name] = map;
            _forest = null;
        }
    }

    /// <summary>Removes a map, so tests can keep the process-wide registry to the maps they build.</summary>
    public static void Unregister(string mapName)
    {
        lock (Sync)
        {
            Factories.Remove(mapName);
            Maps.Remove(mapName);
            _forest = null;
        }
    }

    /// <summary>Returns the map with the given name, building it on first use, or null when none is registered.</summary>
    public static ControlMap? Get(string mapName)
    {
        lock (Sync)
            return GetLocked(mapName);
    }

    /// <summary>Returns the node a generated id names, or null when no registered map holds it.</summary>
    public static ControlMapNode? Find(ControlMapNodeID id) => Get(id.Map)?.Find(id);

    // Every registered map expanded once: templates inlined at their instances, slots filled
    internal static ControlMapForest Forest
    {
        get
        {
            lock (Sync)
            {
                if (_forest != null) return _forest;

                List<ControlMap> maps = [];
                foreach (string mapName in Maps.Keys)
                    maps.Add(Maps[mapName]);

                foreach (string mapName in Factories.Keys)
                {
                    if (Maps.ContainsKey(mapName)) continue;

                    maps.Add(GetLocked(mapName)!);
                }

                _forest = ControlMapForest.Build(maps);
                return _forest;
            }
        }
    }

    private static ControlMap? GetLocked(string mapName)
    {
        if (Maps.TryGetValue(mapName, out ControlMap? map)) return map;
        if (!Factories.TryGetValue(mapName, out Func<ControlMap>? factory)) return null;

        map = factory();
        Maps[mapName] = map;
        return map;
    }
}
