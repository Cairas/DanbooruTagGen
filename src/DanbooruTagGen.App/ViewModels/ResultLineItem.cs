using CommunityToolkit.Mvvm.ComponentModel;
using DanbooruTagGen.Core.Generation;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>생성 카드의 미리보기 한 줄. <see cref="Header"/>가 있으면 레시피 이름 머리줄(일괄 생성), 없으면 출력 줄 하나
/// (한 줄로 잘라 보여 주고 툴팁에 전체, 토큰 수가 기준을 넘으면 <see cref="Over"/>).</summary>
public sealed record PreviewLine(string Header, string Text, string FullText, int Tokens, bool Over)
{
    public bool IsHeader => Header.Length > 0;
}

/// <summary>생성 결과 목록의 한 줄. <see cref="LockKey"/>는 어느 레시피의 줄인지(일괄 생성은 레시피 id, 단일 생성은
/// 빌더에 불러온 레시피 기준) — 잠근 줄은 같은 키의 레시피를 다시 뽑을 때만 남는다.
/// <see cref="RawText"/>는 품질 태그를 붙이기 전 원문(다시 쓸 때 그때의 품질 태그 설정으로 다시 붙인다).</summary>
public sealed partial class ResultLineItem : ObservableObject
{
    public ResultLineItem(int number, string lockKey, string recipeName, string headerText,
        string rawText, string displayText, string lineKey, LineOrigin? origin,
        IReadOnlyList<ConflictHit> hits, bool isLocked, int tokens, bool overLimit)
    {
        Number = number;
        LockKey = lockKey;
        RecipeName = recipeName;
        HeaderText = headerText;
        RawText = rawText;
        DisplayText = displayText;
        LineKey = lineKey;
        Origin = origin;
        Hits = hits;
        _isLocked = isLocked;
        Tokens = tokens;
        IsOverLimit = overLimit;
    }

    /// <summary>추정 CLIP 토큰 수(품질 태그 포함, 거친 근사 — LineStats). 75를 넘으면 두 번째 청크로 넘어가 뒤쪽 태그 반영이 약해진다.</summary>
    public int Tokens { get; }
    /// <summary>지금 출력 프로필의 토큰 기준을 넘는가(IL 225 · Anima 512 기본).</summary>
    public bool IsOverLimit { get; }

    public int Number { get; }
    public string LockKey { get; }
    public string RecipeName { get; }
    /// <summary>일괄 생성에서 레시피 묶음의 첫 줄에만 레시피 이름(목록에서 경계가 보이게). 나머지는 빈 문자열.</summary>
    public string HeaderText { get; }
    public string RawText { get; }
    public string DisplayText { get; }
    public string LineKey { get; }
    public LineOrigin? Origin { get; }
    public IReadOnlyList<ConflictHit> Hits { get; }
    public bool HasConflict => Hits.Count > 0;

    [ObservableProperty] private bool _isLocked;
}
