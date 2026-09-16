using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>생성 훅 목록의 저장/로드. PoolStore와 같은 패턴 — 원자적 쓰기와 손상 시 폴백은
/// JsonStore가 담당한다.</summary>
public static class HookStore
{
    public static void Save(IEnumerable<GenerationHook> hooks, string path) => JsonStore.SaveAtomic(hooks.ToList(), path);

    public static List<GenerationHook> Load(string path) => JsonStore.LoadOrDefault(path, new List<GenerationHook>());
}
