using System.Text;
using System.Text.Json;

namespace DanbooruTagGen.Core.Persistence;

/// <summary>모든 저장/로드가 공유하는 직렬화 설정 + 안전한 저장/불러오기 헬퍼.
/// PoolStore/RecipeLibraryStore/SettingsStore가 각자 File.WriteAllText/Deserialize를
/// 직접 호출하던 걸 여기로 모았다 — 저장 도중 강제종료돼도 기존 파일이 안전하고(원자적 쓰기),
/// 파일이 손상돼 있어도 앱 시작 자체가 죽지 않는다(손상 시 폴백 + .bak 백업).</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>임시 파일에 먼저 쓴 뒤 교체한다 — 쓰는 도중 죽으면 임시 파일만 손상되고
    /// 기존 파일은 그대로 남는다(File.WriteAllText 직접 호출은 쓰다 죽으면 파일 자체가 깨짐).</summary>
    public static void SaveAtomic<T>(T data, string path)
    {
        var json = JsonSerializer.Serialize(data, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    /// <summary>손상된 JSON이면 예외를 던지는 대신 fallback을 반환한다(앱 생성자 단계에서
    /// 언가드로 호출되던 Load들이 파일 하나 깨졌다고 앱을 통째로 못 띄우게 만들던 문제를 막음).
    /// 손상 파일은 .bak로 복사해 둬 원인 파악이 가능하게 한다.</summary>
    public static T LoadOrDefault<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch (JsonException)
        {
            try { File.Copy(path, path + ".bak", overwrite: true); } catch (IOException) { /* 백업 실패는 무시 */ }
            return fallback;
        }
    }
}
