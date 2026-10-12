using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

public static class PoolStore
{
    public static void Save(IEnumerable<Pool> pools, string path) => JsonStore.SaveAtomic(pools.ToList(), path);

    public static List<Pool> Load(string path) => JsonStore.LoadOrDefault(path, new List<Pool>());

    /// <summary>번들 pools.json(data/presets) 전용 엄격 로드 — 손상·타입 불일치·잠금이면
    /// <see cref="InvalidDataException"/>. 사용자 데이터는 <see cref="Load"/>(관대한 폴백)를 쓴다.</summary>
    public static List<Pool> LoadBundled(string path) => JsonStore.LoadStrict(path, new List<Pool>());
}
