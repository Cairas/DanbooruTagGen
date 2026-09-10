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
    /// <summary>"(tag:1.2)" 같은 가중치 표기에서 태그 이름만 꺼내기 위한 패턴.</summary>
    private static readonly Regex WeightSyntax = new(@"^\((?<tag>.+):[0-9.]+\)$", RegexOptions.Compiled);

    /// <summary>상대가 <b>지금 화면에</b> 있어야 성립하는 진행 중 행위 태그.
    /// <c>tools/conflict_scan.py</c>의 NEEDS_PARTNER를 가져오되 <b>사후 흔적 태그는 뺐다</b>
    /// (<c>cum_in_pussy</c>·<c>cum_in_mouth</c>·<c>internal_cumshot</c>·<c>ejaculation</c>).
    /// 그것들은 상대가 떠난 뒤에도 몸에 남는 "상태"라서, 사후 장면을 그리는 컨셉을 전부
    /// 잘못 지적하게 된다(실제로 팩 3개가 이 이유로 오탐이었다). 파이썬 스캐너와 이 부분만
    /// 의도적으로 다르므로, 저쪽 표를 고칠 때 이 차이를 지우지 말 것.</summary>
    private static readonly IReadOnlySet<string> NeedsPartner = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sitting_on_person", "hetero", "sex", "vaginal", "anal", "deep_penetration",
        "imminent_penetration", "penis_awe", "fellatio", "irrumatio", "deepthroat",
        "doggystyle", "missionary", "spooning", "sex_from_behind", "mating_press",
        "piledriver_(sex)", "prone_bone", "cowgirl_position", "girl_on_top",
        "standing_sex", "full_nelson", "suspended_congress", "double_penetration",
        "triple_penetration", "multiple_penetration", "spitroast", "knotting",
        "grabbing_another's_hair", "grabbing_another's_breast", "grabbing_another's_ass",
        "hand_on_another's_face", "hand_on_another's_head", "head_grab", "neck_grab",
        "strangling", "spanking", "thigh_grab", "torso_grab", "held_down",
    };

    /// <summary>상대의 존재를 뜻하는 태그(사람·동물·몬스터·기계 어느 쪽이든 상대로 인정).
    /// 위와 같은 이유로 <c>conflict_scan.py</c>의 PARTNER_MARKERS와 같은 표다.</summary>
    private static readonly IReadOnlySet<string> PartnerMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "1boy", "2boys", "3boys", "4boys", "multiple_boys", "faceless_male",
        "2girls", "3girls", "multiple_girls", "multiple_others", "everyone",
        "monster", "monster_boy", "orc", "goblin", "oni", "demon", "minotaur",
        "tentacles", "penis_tentacle", "bestiality", "dog", "horse", "pig",
        "machinery", "sex_machine", "milking_machine", "penis", "huge_penis",
        "large_penis", "veiny_penis",
    };

    /// <summary>서술 표현만으로 상대를 세우는 팩이 실제로 있다("a man holds her head down…").
    /// 그런 팩을 "상대 없음"으로 지적하면 검사를 아무도 안 보게 되므로, 문장에 이 낱말이
    /// 있으면 상대가 있는 것으로 인정한다.</summary>
    private static readonly Regex PartnerWords = new(
        @"\b(man|men|male|males|he|his|him|boy|boys|soldier|soldiers|guard|guards|master|husband|father|" +
        @"stranger|strangers|beast|beasts|monster|monsters|orc|orcs|creature|creatures|machine|machines|tentacle|tentacles)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="suggest">오탈자로 보이는 태그와 비슷한 실제 태그 이름을 찾아 주는 함수
    /// (앱은 TagDatabase 검색을 넘긴다). 없으면 "혹시 이것?" 안내만 빠진다.</param>
    public static IReadOnlyList<ValidationIssue> Validate(
        Recipe recipe,
        IReadOnlyDictionary<string, Pool> poolsById,
        ITagLookup? tagInfo,
        Func<string, IReadOnlyList<string>>? suggest = null)
    {
        var issues = new List<ValidationIssue>();
        var (tags, phrases) = CollectStrings(recipe, poolsById);

        if (tagInfo != null)
        {
            foreach (var tag in tags)
                if (tagInfo.Lookup(tag) is null)
                    issues.Add(new ValidationIssue(ValidationRule.UnknownTag, tag, UnknownTagMessage(tag, suggest)));
        }

        var present = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        if (!present.Overlaps(PartnerMarkers) && !phrases.Any(p => PartnerWords.IsMatch(p)))
        {
            foreach (var act in tags.Where(NeedsPartner.Contains).Distinct(StringComparer.OrdinalIgnoreCase))
                issues.Add(new ValidationIssue(ValidationRule.MissingPartner, act,
                    $"'{act}'는 상대가 있어야 성립하는데 상대(1boy·tentacles·penis 등)가 없습니다."));
        }

        return issues;
    }

    private static string UnknownTagMessage(string tag, Func<string, IReadOnlyList<string>>? suggest)
    {
        var hits = suggest?.Invoke(tag) ?? Array.Empty<string>();
        return hits.Count > 0
            ? $"'{tag}'는 danbooru 사전에 없습니다. 혹시 이것? {string.Join(", ", hits)}"
            : $"'{tag}'는 danbooru 사전에 없습니다.";
    }

    /// <summary>레시피가 쓰는 모든 문자열을 태그와 서술 표현으로 갈라 모은다(인라인·대안 그룹·
    /// 참조 풀 전부). 중복은 태그 쪽만 그대로 두고 나중에 필요할 때 걸러 쓴다.</summary>
    private static (List<string> Tags, List<string> Phrases) CollectStrings(
        Recipe recipe, IReadOnlyDictionary<string, Pool> poolsById)
    {
        var tags = new List<string>();
        var phrases = new List<string>();

        void Take(IEnumerable<string> raw)
        {
            foreach (var s in raw)
            {
                var core = Unwrap(s);
                if (core.Length == 0) continue;
                if (core.Contains(' ')) phrases.Add(core);
                else tags.Add(core);
            }
        }

        foreach (var slot in recipe.Slots)
        {
            if (!slot.IsEnabled) continue;   // 꺼둔 슬롯은 출력되지 않으므로 점검 대상도 아니다
            switch (slot)
            {
                case FixedSlot f:
                    Take(f.Tags);
                    break;
                case RandomPoolSlot r:
                    Take(r.Tags);
                    foreach (var id in r.ExtraPoolIds.Prepend(r.PoolId))
                        if (!string.IsNullOrEmpty(id) && poolsById.TryGetValue(id, out var pool))
                            Take(pool.Candidates);
                    break;
                case AlternativeSlot alt:
                    foreach (var g in alt.Groups)
                        if (g.Weight != 0)   // 가중치 0은 절대 안 뽑히므로 지적해도 소용없다
                            Take(g.Tags);
                    break;
            }
        }

        return (tags, phrases);
    }

    /// <summary>"(tag:1.2)"에서 태그 이름만 꺼낸다. 가중치 표기가 아니면 그대로 돌려준다.</summary>
    private static string Unwrap(string raw)
    {
        var s = raw.Trim();
        var m = WeightSyntax.Match(s);
        return m.Success ? m.Groups["tag"].Value.Trim() : s;
    }
}
