using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Generation;

/// <summary>슬롯이 완성된 그림에서 차지하는 비중.
/// <para>
/// 원래는 개발자 도구(<c>tools/visual_variety_scan.py</c>)에만 있던 분류인데, Anima 출력 모드가
/// "어떤 태그를 남기고 어떤 걸 버릴지" 판단해야 해서 런타임으로 옮겼다. 두 곳의 기준이 갈리면
/// 지표와 실제 출력이 어긋나므로, 분류표를 바꿀 때는 양쪽을 같이 고쳐야 한다.
/// </para></summary>
public enum SlotRole
{
    /// <summary>컨셉의 정체성. 항상 나가는 고정 슬롯.</summary>
    Identity,
    /// <summary>행위 단계·체위·배경·옷·몸 상태·도구 — 이게 바뀌어야 다른 그림이 된다.</summary>
    Major,
    /// <summary>표정·반응·흔적 — 화면에서 차지하는 면적이 작다.</summary>
    Minor,
    /// <summary>구도·조명·유두/생식기 디테일·시선·체액 — 완성된 그림을 거의 안 바꾼다.</summary>
    Cosmetic,
    /// <summary>분류표에 없는 라벨. 조용히 버리면 지표가 거짓말을 하므로 별도로 센다.</summary>
    Unknown,
}

/// <summary>슬롯 라벨(과 ALT의 경우 내용)로 <see cref="SlotRole"/>을 판정한다.
/// <c>tools/visual_variety_scan.py</c>의 AXIS_TIERS와 같은 규칙이다.</summary>
public static class SlotRoleClassifier
{
    // 부분 문자열 매칭이라 순서가 곧 우선순위다. 더 구체적인 키워드를 앞에 둔다
    // (예: "장치 디테일"은 소품이라 MINOR인데, 뒤의 "장치"(MAJOR)보다 먼저 와야 한다).
    private static readonly (string Key, SlotRole Role)[] Table =
    {
        // 카메라는 COSMETIC이 아니다 — "샷 크기"가 배경이 보일지 말지를 정하므로
        // Anima 모드에서도 버리면 안 된다. 아래 ("구도"/"framing", Cosmetic)보다 먼저 온다.
        ("샷 크기", SlotRole.Minor), ("카메라 각도", SlotRole.Minor), ("shot size", SlotRole.Minor),

        // ── COSMETIC: 먼저 걸러내야 MAJOR로 오분류되지 않는다.
        ("가슴·유두", SlotRole.Cosmetic), ("유두", SlotRole.Cosmetic), ("가슴 크기", SlotRole.Cosmetic),
        ("생식기 디테일", SlotRole.Cosmetic), ("구도", SlotRole.Cosmetic), ("framing", SlotRole.Cosmetic),
        ("angle", SlotRole.Cosmetic), ("조명", SlotRole.Cosmetic), ("lighting", SlotRole.Cosmetic),
        ("시선", SlotRole.Cosmetic), ("gaze", SlotRole.Cosmetic), ("체액", SlotRole.Cosmetic),
        ("fluid", SlotRole.Cosmetic), ("cum", SlotRole.Cosmetic), ("촬영", SlotRole.Cosmetic),
        ("환경 디테일", SlotRole.Cosmetic), ("room detail", SlotRole.Cosmetic),
        ("헤어", SlotRole.Cosmetic), ("hair", SlotRole.Cosmetic), ("몸매", SlotRole.Cosmetic),
        ("삽입 연출", SlotRole.Cosmetic),
        ("연출", SlotRole.Minor), ("장치 디테일", SlotRole.Minor), ("설비 디테일", SlotRole.Minor),

        // ── MAJOR
        ("행위 진행", SlotRole.Major), ("행위·고통 진행", SlotRole.Major), ("진행 단계", SlotRole.Major),
        ("progress", SlotRole.Major), ("궤적", SlotRole.Major), ("단계", SlotRole.Major),
        ("moment", SlotRole.Major), ("사육", SlotRole.Major), ("성적 직접도", SlotRole.Major),
        ("captive use", SlotRole.Major), ("취급받는 방식", SlotRole.Major), ("contact focus", SlotRole.Major),
        ("체위", SlotRole.Major), ("자세", SlotRole.Major), ("pose", SlotRole.Major),
        ("posture", SlotRole.Major), ("position", SlotRole.Major),
        ("포박", SlotRole.Major), ("구속", SlotRole.Major), ("bondage", SlotRole.Major),
        ("restraint", SlotRole.Major),
        ("산란 후 반응", SlotRole.Minor),   // '산란'(MAJOR)보다 먼저 — 표정 축이지 산란 축이 아니다.
        ("배경", SlotRole.Major), ("background", SlotRole.Major), ("장소", SlotRole.Major),
        ("setting", SlotRole.Major), ("소굴", SlotRole.Major), ("잠자리", SlotRole.Major),
        ("환경", SlotRole.Major), ("받침", SlotRole.Major),
        ("옷", SlotRole.Major), ("clothing", SlotRole.Major), ("복장", SlotRole.Major),
        ("의상", SlotRole.Major), ("lingerie", SlotRole.Major), ("속옷", SlotRole.Major),
        ("도구", SlotRole.Major), ("device", SlotRole.Major), ("tool", SlotRole.Major),
        ("prop", SlotRole.Major), ("장치", SlotRole.Major), ("경과", SlotRole.Major),
        ("몸에 남은", SlotRole.Major), ("시간", SlotRole.Major),
        ("몸 상태", SlotRole.Major), ("몸의 상태", SlotRole.Major), ("body state", SlotRole.Major),
        ("체격", SlotRole.Major), ("임신", SlotRole.Major), ("번식", SlotRole.Major),
        ("산란", SlotRole.Major), ("결손", SlotRole.Major), ("피어싱", SlotRole.Major),
        ("타투", SlotRole.Major), ("낙인", SlotRole.Major), ("신체 개조", SlotRole.Major),
        ("인원", SlotRole.Major), ("구성", SlotRole.Major), ("손님", SlotRole.Major),
        ("남편", SlotRole.Major), ("야수의 수", SlotRole.Major), ("삽입 방식", SlotRole.Major),

        // ── MINOR
        ("표정", SlotRole.Minor), ("expression", SlotRole.Minor), ("face", SlotRole.Minor),
        ("반응", SlotRole.Minor), ("reaction", SlotRole.Minor), ("마음", SlotRole.Minor),
        ("심리", SlotRole.Minor), ("심경", SlotRole.Minor), ("정신", SlotRole.Minor),
        ("state", SlotRole.Minor), ("상태", SlotRole.Minor), ("행동", SlotRole.Minor),
        ("action", SlotRole.Minor), ("손길", SlotRole.Minor), ("태도", SlotRole.Minor),
        ("tone", SlotRole.Minor), ("pressure", SlotRole.Minor), ("attitude", SlotRole.Minor),
        ("흔적", SlotRole.Minor), ("trace", SlotRole.Minor), ("상처", SlotRole.Minor),
        ("폭행", SlotRole.Minor), ("표식", SlotRole.Minor), ("야수의 몸", SlotRole.Minor), ("괴물의 몸", SlotRole.Minor), ("상대의 몸", SlotRole.Minor), ("체형 대비", SlotRole.Minor), ("딸의 머리", SlotRole.Minor),
        ("금단 증상", SlotRole.Minor), ("약의 흔적", SlotRole.Minor),
        ("resistance", SlotRole.Minor), ("무너짐", SlotRole.Minor), ("약이 퍼진", SlotRole.Minor),
        ("기본", SlotRole.Minor), ("basic", SlotRole.Minor),
    };

    /// <summary>ALT 그룹들이 서로 다른 "눈에 보이는 것"을 담고 있으면 라벨과 무관하게 MAJOR다.
    /// 라벨은 거짓말을 한다 — 인질극의 "심리 상태" ALT는 이름과 달리 그룹마다
    /// sex/ejaculation을 품고 있어 사실상 행위 진행 축이었다.</summary>
    private static readonly HashSet<string> VisualTags = new(StringComparer.Ordinal)
    {
        "sex", "vaginal", "anal", "oral", "fellatio", "irrumatio", "deepthroat",
        "imminent_penetration", "deep_penetration", "ejaculation", "cum_in_pussy",
        "cum_in_mouth", "after_sex", "after_vaginal", "after_fellatio", "cumdrip",
        "knotting", "egg_laying", "double_penetration", "triple_penetration",
        "rough_sex", "paizuri", "handjob",
        "on_back", "on_side", "on_stomach", "all_fours", "bent_over", "kneeling",
        "standing", "sitting", "squatting", "spread_legs", "legs_up", "lying",
        "suspension", "doggystyle", "presenting", "spooning", "straddling",
        "sex_from_behind", "arched_back", "upside-down", "against_wall", "on_bed", "on_floor",
        "pregnant", "big_belly", "quadruple_amputee", "cocoon", "bound_together",
    };

    public static SlotRole Classify(Slot slot)
    {
        if (slot is FixedSlot) return SlotRole.Identity;
        if (slot is AlternativeSlot alt && AltIsVisual(alt)) return SlotRole.Major;

        var label = slot.Label ?? "";
        foreach (var (key, role) in Table)
            if (label.Contains(key, StringComparison.OrdinalIgnoreCase))
                return role;
        return SlotRole.Unknown;
    }

    private static bool AltIsVisual(AlternativeSlot alt)
    {
        var sigs = new List<string>();
        foreach (var g in alt.Groups)
        {
            if (g.Weight == 0) continue;   // 안 뽑히는 그룹은 세지 않는다
            var hit = g.Tags.Where(VisualTags.Contains).OrderBy(t => t, StringComparer.Ordinal);
            sigs.Add(string.Join(",", hit));
        }
        int nonEmpty = sigs.Count(s => s.Length > 0);
        return nonEmpty >= 2 && sigs.Distinct(StringComparer.Ordinal).Count() >= 2;
    }
}
