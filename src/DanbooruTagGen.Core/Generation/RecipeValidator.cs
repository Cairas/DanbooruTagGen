using System.Text.RegularExpressions;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

public enum ValidationRule
{
    /// <summary>danbooru 사전에 없는 태그(대개 오탈자). 모델은 모르는 토큰을 조용히 무시하므로
    /// 그림에서는 "그 태그만 빠진" 것처럼 보일 뿐 아무 오류도 나지 않는다 — 그래서 안 세면
    /// 영영 안 드러난다.</summary>
    UnknownTag,

    /// <summary>상대가 있어야 성립하는 행위인데 상대가 화면에 없음(예: <c>fellatio</c>인데
    /// <c>1boy</c>도 <c>tentacles</c>도 없음). 이러면 모델이 상대를 알아서 만들어 내거나
    /// 행위 자체가 뭉개진다.</summary>
    MissingPartner,

    /// <summary>닻 없는 자연어: 랜덤 풀 후보(또는 ALT 그룹) 안의 자연어가 손·팔·입·무릎 꿇기 같은 신체·자세
    /// 동작을 묘사하는데, 같은 후보 안에 실존 danbooru 태그가 하나도 없음. 충돌 엔진은 태그 키만 보므로
    /// 이 동작이 다른 슬롯과 모순돼도(예: 양손이 이미 풀넬슨에 쓰였는데 "그의 손이 가슴을 움켜쥠") 못 잡는다
    /// (n560 "그의 손" 풀에서 실제로 겪음). 뜻이 같은 동반 태그나 <c>implies:태그</c>를 붙이면 풀린다.</summary>
    AnchorlessPhrase,

    /// <summary>괄호·가중치 문법 오류: 괄호 짝 불일치, 숫자로 안 읽히는 가중치(<c>(x:1,2)</c>), 구분자 오타
    /// (<c>(x;1.2)</c>) 등(<see cref="TagKey.FindSyntaxError"/>). 생성은 이런 문자열도 원문 그대로 내보내서
    /// 프롬프트에 깨진 문법이 섞인 줄 모르게 된다 — 짝 없는 괄호는 A1111에서 뒤 태그 전체를 강조 그룹으로 삼킨다.
    /// 이모티콘(<c>:(</c>)·문장 속 콜론(<c>(she whispers: please~)</c>)은 지적하지 않는다.</summary>
    MalformedSyntax,
}

/// <summary>레시피 하나에서 발견한 지적 사항. <see cref="Subject"/>는 원인이 된 태그.</summary>
public sealed record ValidationIssue(ValidationRule Rule, string Subject, string Message);

/// <summary>저장하기 전에 기계적으로 잡을 수 있는 실수를 점검한다.
///
/// 왜 필요한가: 지금까지 이 점검은 <c>tools/*.py</c>로만 돌 수 있어서, 앱 안에서만 작업하면
/// 존재하지 않는 태그가 팩에 섞여 들어가도 아무도 몰랐다. 모델은 모르는 태그를 조용히
/// 버리므로 오류가 나지 않고, 결과 그림이 "왠지 의도와 다르다"로만 나타난다.
///
/// <para><b>공백이 있으면 태그가 아니다.</b> 실제 danbooru 태그는 전부 언더스코어로 저장되므로,
/// 공백이 들어간 문자열은 작성자가 일부러 쓴 서술 표현이다(짧은 <c>pink smoke</c>부터 한 문장
/// 전체까지). 사전에 없다고 오탈자로 몰면 검사 자체가 못 쓰게 되므로 건너뛴다.</para></summary>
public static class RecipeValidator
{
    /// <summary>서술 표현만으로 상대를 세우는 팩이 실제로 있다("a man holds her head down…").
    /// 그런 팩을 "상대 없음"으로 지적하면 검사를 아무도 안 보게 되므로, 문장에 이 낱말이
    /// 있으면 상대가 있는 것으로 인정한다. 앱 검증 전용 완화다 — 스캐너는 이걸 쓰지 않는다
    /// (data/requires.csv 머리말의 "두 검사기가 일부러 다르게 보는 부분").</summary>
    private static readonly Regex PartnerWords = new(
        @"\b(man|men|male|males|he|his|him|boy|boys|soldier|soldiers|guard|guards|master|husband|father|" +
        @"stranger|strangers|beast|beasts|monster|monsters|orc|orcs|creature|creatures|machine|machines|tentacle|tentacles)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>"닻 없는 자연어"(<see cref="ValidationRule.AnchorlessPhrase"/>)로 볼 신체 부위·자세·손동작 낱말.
    /// 충돌 규칙(conflicts.csv)이 주로 다루는 게 손 사용·입 사용·다리/무릎 자세라, 이 낱말이 든 문장이 태그 없이
    /// 혼자 나가면 충돌 엔진의 사각지대가 된다. <see cref="PartnerWords"/>와 같은 방식(낱말 경계 정규식)이다.
    /// <para>엉덩이·허벅지·무릎 같은 "위치" 낱말은 일부러 뺐다 — 실제 팩에 돌려 보니 문신 위치("tattoo spans her
    /// hip")·땀·체액 묘사가 대부분이라 경고가 소음이 됐다. 동작은 손·팔·다리·입 낱말이나 아래 동사가 같이 잡는다.</para></summary>
    private static readonly Regex BodyPoseWords = new(
        @"\b(hand|hands|finger|fingers|fist|fists|wrist|wrists|arm|arms|leg|legs|mouth|tongue|" +
        @"kneel|kneels|kneeling|knelt|bent|bend|bends|bending|squat|squats|squatting|straddle|straddles|straddling|" +
        @"grind|grinds|grinding|lick|licks|licking|" +
        @"grip|grips|gripping|gripped|grab|grabs|grabbing|grabbed|press|presses|pressing|pressed|" +
        @"pin|pins|pinned|pinning|hold|holds|holding|held|lift|lifts|lifting|lifted)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="suggest">오탈자로 보이는 태그와 비슷한 실제 태그 이름을 찾아 주는 함수
    /// (앱은 TagDatabase 검색을 넘긴다). 없으면 "혹시 이것?" 안내만 빠진다.</param>
    /// <param name="hooks">생성 훅(hooks.json). 런타임에 <see cref="HookApplier"/>가 슬롯으로 끼워 넣으므로 같은 방식으로
    /// 복사본에 끼워 함께 검사한다 — 예전엔 훅이 어떤 검사도 안 받았다. 꺼진 훅은 HookApplier가 건너뛰므로
    /// 검사 대상도 아니다. 훅에서 나온 지적은 메시지 끝에 훅 이름을 붙인다.</param>
    /// <param name="requires">상대 필요 규칙. null이면 <see cref="RequiresRules.Default"/>(내장 data/requires.csv).
    /// strict 줄(사후 흔적 태그)은 이 검사에서 건너뛴다.</param>
    /// <param name="conflicts">있으면 "닻 없는 자연어" 판정에서, 그 문장 자체가 충돌 규칙의 멤버로 올라 있는 경우
    /// (conflicts.csv의 "(his hand gripping her jaw, …:1.2)" 같은 자연어 멤버)를 닻 있음으로 본다 — 엔진이 이미 본다.</param>
    public static IReadOnlyList<ValidationIssue> Validate(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        ITagLookup? tagInfo,
        Func<string, IReadOnlyList<string>>? suggest = null,
        IEnumerable<GenerationHook>? hooks = null,
        RequiresRules? requires = null,
        ConflictRules? conflicts = null)
    {
        var (target, hookSlotNames) = WithHooks(recipe, hooks, poolsById);
        var issues = new List<ValidationIssue>();
        var (tags, phrases, units, raws) = CollectStrings(target, poolsById, hookSlotNames);

        // 괄호·가중치 문법. 문자열(후보) 단위로 보고, 같은 문자열은 한 번만 지적한다(풀 공유 등으로 여러 번 모일 수 있다).
        var syntaxSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (raw, where, hook) in raws)
            if (TagKey.FindSyntaxError(raw) is { } syntaxError && syntaxSeen.Add(raw))
                issues.Add(new ValidationIssue(ValidationRule.MalformedSyntax, raw,
                    $"'{Shorten(raw)}'({where}) — {syntaxError}" + HookNote(hook)));

        if (tagInfo != null)
        {
            foreach (var (tag, hook) in tags)
                if (tagInfo.Lookup(tag.Key) is null)
                    issues.Add(new ValidationIssue(ValidationRule.UnknownTag, tag.Text,
                        UnknownTagMessage(tag.Text, suggest) + HookNote(hook)));
        }

        // 상대 필요 규칙(data/requires.csv). 레시피 전체 기준: 어느 슬롯에서든 상대가 나오면 통과.
        var present = new HashSet<string>(tags.Select(t => t.Fragment.Key), StringComparer.Ordinal);
        bool phrasePartner = phrases.Any(p => PartnerWords.IsMatch(p));
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in (requires ?? RequiresRules.Default).Rules)
        {
            if (rule.StrictOnly || phrasePartner || present.Overlaps(rule.AnyOfKeys)) continue;
            var examples = string.Join("·", rule.AnyOf.Take(3)) + (rule.AnyOf.Count > 3 ? " 등" : "");
            foreach (var (act, hook) in tags.Where(t => rule.TagKeys.Contains(t.Fragment.Key)).DistinctBy(t => t.Fragment.Key))
                if (reported.Add(act.Key))
                    issues.Add(new ValidationIssue(ValidationRule.MissingPartner, act.Text,
                        $"'{act.Text}'는 상대가 있어야 성립하는데 상대({examples})가 없습니다." + HookNote(hook)));
        }

        // 닻 없는 자연어: 같은 후보(풀 후보 문자열 하나 / ALT 그룹 하나) 안에 신체·자세 낱말이 든 자연어만 있고
        // 실존 태그가 하나도 없다. 사전이 없으면 "태그 모양(공백 없음)"이면 닻으로 인정한다(오탐보다 누락이 낫다).
        foreach (var unit in units)
        {
            bool anchored = unit.Fragments.Any(f => !f.IsPhrase && f.Text.Length > 0
                                                    && (tagInfo is null || tagInfo.Lookup(f.Key) is not null));
            if (anchored) continue;
            foreach (var phrase in unit.Fragments.Where(f => f.IsPhrase && conflicts?.IsMember(f.Key) != true).Select(f => f.Text))
            {
                var m = BodyPoseWords.Match(phrase);
                if (!m.Success) continue;
                issues.Add(new ValidationIssue(ValidationRule.AnchorlessPhrase, phrase,
                    $"'{Shorten(phrase)}'({unit.Where}) — 신체·자세 낱말('{m.Value}')이 든 자연어인데 같은 후보에 실존 태그가 없어 " +
                    "충돌 검사가 이 동작을 못 봅니다. 뜻이 같은 동반 태그나 implies:태그를 같이 쓰세요." + HookNote(unit.Hook)));
                break;   // 한 후보에 한 번만
            }
        }

        return issues;
    }

    private static string HookNote(string? hook) => hook is null ? "" : $" (훅 '{hook}')";

    private static string Shorten(string s) => s.Length <= 48 ? s : s[..45] + "…";

    /// <summary>훅을 끼운 복사본과, 훅에서 온 슬롯 → 훅 이름. 원본 레시피는 건드리지 않는다
    /// (<see cref="HookApplier.Apply"/>는 넘겨받은 레시피의 슬롯 목록을 제자리에서 고친다 — 목록만 새로 만들면
    /// 슬롯 객체는 공유해도 안전하다. HookApplier는 기존 슬롯을 수정하지 않고 새 슬롯만 끼운다).</summary>
    private static (Recipe Target, Dictionary<Slot, string>? HookSlots) WithHooks(
        Recipe recipe, IEnumerable<GenerationHook>? hooks, IReadOnlyDictionary<string, Pool> poolsById)
    {
        if (hooks is null) return (recipe, null);
        var ordered = hooks.Where(h => h.IsEnabled).OrderBy(h => h.Order).ToList();   // 앱 생성 경로와 같은 순서
        if (ordered.Count == 0) return (recipe, null);

        var copy = new Recipe { Id = recipe.Id, Name = recipe.Name, Slots = new List<Slot>(recipe.Slots) };
        var original = new HashSet<Slot>(recipe.Slots, ReferenceEqualityComparer.Instance);
        HookApplier.Apply(copy, ordered, poolsById);
        var hookSlots = new Dictionary<Slot, string>(ReferenceEqualityComparer.Instance);
        foreach (var s in copy.Slots)
            if (!original.Contains(s)) hookSlots[s] = s.Label;
        return (copy, hookSlots);
    }

    private static string UnknownTagMessage(string tag, Func<string, IReadOnlyList<string>>? suggest)
    {
        var hits = suggest?.Invoke(tag) ?? Array.Empty<string>();
        return hits.Count > 0
            ? $"'{tag}'는 danbooru 사전에 없습니다. 혹시 이것? {string.Join(", ", hits)}"
            : $"'{tag}'는 danbooru 사전에 없습니다.";
    }

    /// <summary>"닻" 판정 단위: 랜덤 풀 후보 문자열 하나, 또는 ALT 그룹 하나(그룹은 통째로 함께 나가므로
    /// 그룹 안의 다른 문자열에 있는 태그도 같은 후보의 동반 태그다 — 팩들이 실제로 "문장 + 태그 여러 줄"로
    /// 그룹을 짠다). Where: 메시지에 쓸 위치 설명.</summary>
    private sealed record Unit(List<TagFragment> Fragments, string Where, string? Hook);

    /// <summary>레시피가 쓰는 모든 문자열을 태그와 서술 표현으로 갈라 모은다(인라인·대안 그룹·
    /// 참조 풀 전부). 중복은 태그 쪽만 그대로 두고 나중에 필요할 때 걸러 쓴다.
    /// 문자열 하나를 <see cref="TagKey"/>로 조각내 조각 단위로 가른다 — "(문장:1.1), bite_mark"의
    /// bite_mark도 개별적으로 존재 검사·상대 검사 대상이 된다(예전엔 묶음 전체가 공백 때문에 서술
    /// 표현으로 분류돼 동반 태그의 오탈자가 그대로 통과했다). <c>implies:</c> 조각은 출력은 안 되지만
    /// 판정에서는 있는 태그로 보므로 태그 쪽에 들어간다(존재 검사·상대 검사·닻 판정 전부).
    /// 추첨 가중치 0(<c>0::</c>)인 랜덤 풀 후보는 절대 안 뽑히므로 뺀다.</summary>
    private static (List<(TagFragment Fragment, string? Hook)> Tags, List<string> Phrases, List<Unit> Units,
        List<(string Raw, string Where, string? Hook)> Raws) CollectStrings(
        Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById, Dictionary<Slot, string>? hookSlots)
    {
        var tags = new List<(TagFragment, string?)>();
        var phrases = new List<string>();
        var units = new List<Unit>();
        var raws = new List<(string, string, string?)>();

        List<TagFragment> Take(IEnumerable<string> raw, string? hook, string where)
        {
            var frags = new List<TagFragment>();
            foreach (var s in raw)
            {
                raws.Add((s, where, hook));
                foreach (var f in TagKey.Split(s))
                {
                    if (f.Text.Length == 0) continue;
                    frags.Add(f);
                    if (f.IsPhrase && !f.IsImplied) phrases.Add(f.Text);
                    else tags.Add((f, hook));
                }
            }
            return frags;
        }

        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue;   // 꺼둔 슬롯은 출력되지 않으므로 점검 대상도 아니다
            string? hook = hookSlots != null && hookSlots.TryGetValue(slot, out var h) ? h : null;
            switch (slot)
            {
                case FixedSlot f:
                    Take(f.Tags, hook, $"'{slot.Label}' 고정");
                    break;
                case RandomPoolSlot r:
                    if (r.ChancePercent == 0) break;   // 절대 발동 안 하므로 출력되지 않는다
                    var candidates = new List<string>(r.Tags);
                    foreach (var id in r.ExtraPoolIds.Prepend(r.PoolId))
                        if (!string.IsNullOrEmpty(id) && poolsById.TryGetValue(id, out var pool))
                            candidates.AddRange(pool.Candidates);
                    foreach (var c in candidates)
                    {
                        if (TagKey.ParsePickWeight(c).Weight == 0) continue;
                        var where = $"'{slot.Label}' 풀 후보";
                        units.Add(new Unit(Take(new[] { c }, hook, where), where, hook));
                    }
                    break;
                case AlternativeSlot alt:
                    foreach (var g in alt.Groups)
                        if (g.Weight != 0)   // 가중치 0은 절대 안 뽑히므로 지적해도 소용없다
                        {
                            var where = $"'{slot.Label}' 그룹 '{g.Label}'";
                            units.Add(new Unit(Take(g.Tags, hook, where), where, hook));
                        }
                    break;
            }
        }

        return (tags, phrases, units, raws);
    }
}
