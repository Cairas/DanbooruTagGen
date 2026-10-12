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

    /// <summary>잠긴 파일을 다시 읽어 보는 횟수와 간격. data/presets를 스크립트가 쓰는 동안
    /// FileSystemWatcher가 읽으러 들어오는 경로가 실재하고(쓰기 완료까지 수십 ms), 그 순간
    /// 열면 IOException이 난다. 한 번 실패했다고 예외를 올리면 앱 시작·자동 갱신이 통째로
    /// 죽으므로 짧게 기다렸다 다시 시도한다.</summary>
    private const int LoadRetries = 3;
    private const int LoadRetryDelayMs = 80;

    /// <summary>손상된 JSON이면 예외를 던지는 대신 fallback을 반환한다(앱 생성자 단계에서
    /// 언가드로 호출되던 Load들이 파일 하나 깨졌다고 앱을 통째로 못 띄우게 만들던 문제를 막음).
    /// 손상 파일은 .bak로 복사해 둬 원인 파악이 가능하게 한다.
    /// <para>일시적 잠금(IOException)은 <see cref="LoadRetries"/>번까지 다시 시도하고, 그래도
    /// 안 되면 <b>예외를 그대로 올린다</b> — 사용자 데이터를 못 읽었는데 조용히 빈 값을
    /// 돌려주면 그 뒤의 자동 저장이 원본을 덮어써 레시피가 통째로 날아간다. 어디까지
    /// 감당할지는 호출부가 정한다.</para></summary>
    public static T LoadOrDefault<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;
        try
        {
            var json = ReadWithRetry(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch (JsonException)
        {
            try { File.Copy(path, path + ".bak", overwrite: true); } catch (IOException) { /* 백업 실패는 무시 */ }
            return fallback;
        }
    }

    /// <summary><see cref="LoadOrDefault{T}"/>의 엄격판 — <b>번들 프리셋(data/presets) 전용</b>.
    /// 파일이 없으면 fallback을 주지만, 손상(JsonException)·타입 불일치(정수 칸에 소수 등)·
    /// 내용이 null·잠금/권한으로 못 읽음은 전부 <see cref="InvalidDataException"/>("파일명: 사유")으로 올린다.
    /// <para>왜 따로 두나: 번들 쪽에서 손상 파일을 "없는 것"으로 삼키면, 그 결과를 받은
    /// <see cref="PresetSeeder.SyncBundled"/>가 "번들에서 삭제된 항목"으로 오인해 사용자
    /// 데이터에서 지워 버린다(편집 도중 JSON이 잠깐 깨진 순간 워처가 읽으면 실제로 발생 —
    /// "유령 시딩"). 사용자 데이터(AppData) 쪽은 앱 시작을 막지 않는 게 더 중요하므로 계속
    /// <see cref="LoadOrDefault{T}"/>를 쓴다.</para></summary>
    public static T LoadStrict<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;
        var name = Path.GetFileName(path);
        try
        {
            var json = ReadWithRetry(path);
            return JsonSerializer.Deserialize<T>(json, Options)
                   ?? throw new InvalidDataException($"{name}: 내용이 비어 있습니다(null).");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{name}: JSON 오류 — {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            // 다형 슬롯의 type 판별자 누락 등 System.Text.Json이 JsonException이 아닌 걸로 올리는 경우.
            throw new InvalidDataException($"{name}: 역직렬화 실패 — {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{name}: 파일을 읽을 수 없습니다 — {ex.Message}", ex);
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < LoadRetries) { Thread.Sleep(LoadRetryDelayMs); }
        }
    }
}
