using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

public static class PoolStore
{
    public static void Save(IEnumerable<Pool> pools, string path) => JsonStore.SaveAtomic(pools.ToList(), path);

    public static List<Pool> Load(string path) => JsonStore.LoadOrDefault(path, new List<Pool>());
}
