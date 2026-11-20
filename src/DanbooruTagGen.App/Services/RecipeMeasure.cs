using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.Services;

/// <summary>[지표 재기]·점검·비교가 함께 쓰는 것. 예전엔 편집할 때마다 백그라운드에서 지표를 다시 쟀지만(상시 작업),
/// 이제는 사용자가 누를 때만 잰다.</summary>
public static class RecipeMeasure
{
    /// <summary>[지표 재기]·요약 복사용 표본 크기. 비교 리포트(2,000줄)보다 작게 — 가장 무거운 팩도 2초 안쪽(2026-11-20 실측 2,000줄 4.4초).</summary>
    public const int SampleLines = 1000;

    /// <summary>깊은 복사 — 백그라운드에서 재는 동안 편집해도 안전하게.</summary>
    public static Recipe Clone(Recipe recipe) =>
        System.Text.Json.JsonSerializer.Deserialize<Recipe>(
            System.Text.Json.JsonSerializer.Serialize(recipe, JsonStore.Options), JsonStore.Options)!;
}
