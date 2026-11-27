namespace DanbooruTagGen.Core.Generation;

/// <summary>레시피 하나에 요청할 줄 수(잠근 줄 포함).</summary>
public static class RunLineCounts
{
    /// <summary>생성: 요청한 줄 수 그대로(잠근 줄이 그 안을 채우고 나머지만 새로 뽑는다).
    /// 미리보기: 잠근 줄 + 새 줄 최대 <paramref name="previewEach"/>줄 — 미리보기는 "새로 뭐가 나오나" 보는 용도라,
    /// 예전처럼 잠근 줄을 빼고 세면 잠근 줄이 미리보기 줄 수 이상일 때 새 줄이 하나도 안 나왔다.</summary>
    public static int Total(int requested, int locked, bool preview, int previewEach) =>
        preview ? locked + Math.Min(previewEach, requested) : requested;
}
