using System.Text.Json;
using System.Text.Json.Serialization;

namespace DanbooruTagGen.Core.Models;

/// <summary><see cref="RandomPoolSlot.ChancePercent"/> 전용 JSON 변환기. 기본 int 변환기는
/// <c>70.5</c>·<c>"70"</c>를 만나면 JsonException을 던지고, 레시피 로더는 그 파일을 조용히
/// 건너뛴다 — <c>AlternativeGroup.Weight</c>에 소수가 들어가 레시피가 통째로 시딩에서 빠진
/// 사고가 정확히 이 경로였다. 여기서는 로드를 깨지 않고 <see cref="RandomPoolSlot.UnparsedChance"/>로
/// 표시만 해 두어, 생성 전 검증이 어느 슬롯이 틀렸는지 명시적으로 알려 주게 한다.</summary>
public sealed class ChancePercentJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
            return value;
        // 객체·배열이면 값 전체를 건너뛰어야 다음 속성부터 정상적으로 이어 읽힌다.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
        return RandomPoolSlot.UnparsedChance;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
