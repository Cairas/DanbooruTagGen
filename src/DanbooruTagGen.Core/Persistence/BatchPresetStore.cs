using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>일괄 생성 선택 프리셋의 저장/로드.</summary>
public static class BatchPresetStore
{
    public static void Save(IEnumerable<BatchSelectionPreset> presets, string path) => JsonStore.SaveAtomic(presets.ToList(), path);

    public static List<BatchSelectionPreset> Load(string path) => JsonStore.LoadOrDefault(path, new List<BatchSelectionPreset>());
}
