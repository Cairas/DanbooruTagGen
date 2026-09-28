using System.Globalization;
using System.Text.RegularExpressions;

namespace DanbooruTagGen.Core.Generation;

/// <summary>가중치 범위 표기 <c>(tag:LOW~HIGH)</c>를 매 줄 그 범위 안의 값으로 바꾼다.
/// 구분자가 <c>-</c>가 아니라 <c>~</c>인 이유: <c>top-down_bottom-up</c>처럼 태그명 자체에
/// 하이픈이 있고, 음수 가중치와도 헷갈린다. <c>~</c>가 없는 문자열은 전혀 건드리지 않으므로
/// 기존 <c>(tag:1.3)</c> 고정 표기는 100% 그대로다.</summary>
public static class WeightRange
{
    /// <summary>":LOW~HIGH)" 꼬리만 잡는다. 여는 괄호부터 짝을 맞추려 들면
    /// <c>(piledriver_(sex):1.1~1.3)</c>처럼 태그 안의 괄호나, 쉼표가 섞인 자연어 문구를
    /// 정규식으로 다룰 수 없다 — 가중치는 늘 닫는 괄호 바로 앞에 오므로 꼬리만 바꾸면 본문은
    /// 손대지 않고, 한 문자열 안의 여러 괄호 그룹도 각각 독립적으로 치환된다.</summary>
    private static readonly Regex RangeTail = new(@":(?<lo>[^:~()]*)~(?<hi>[^:~()]*)\)", RegexOptions.Compiled);

    /// <summary>정수화 배율(소수 둘째 자리까지). IRandomSource가 정수만 주므로 PickWeighted의
    /// 정수 룰렛과 같은 방식으로 정수 도메인에서 뽑는다.</summary>
    private const int Scale = 100;

    /// <summary>|가중치| 상한. 이보다 크면 ×100 정수화가 넘칠 수 있고, 애초에 오타다.</summary>
    private const double MaxMagnitude = 100;

    /// <summary>범위 표기를 전부 뽑은 값으로 치환한다. 범위 하나당 <paramref name="rnd"/>를
    /// 정확히 한 번 소비하고, 범위가 없으면 한 번도 소비하지 않는다 — 그래서 범위를 안 쓰는
    /// 레시피는 같은 시드에서 예전과 완전히 같은 줄이 나온다.</summary>
    public static string Resolve(string tag, IRandomSource rnd)
    {
        if (tag.IndexOf('~') < 0) return tag; // 대부분의 태그는 여기서 끝(정규식 비용 없음)
        return RangeTail.Replace(tag, m =>
        {
            // Validate가 생성 전에 막지만, Generate를 거치지 않는 경로에서도 원문을 보존한다.
            if (TryParse(m, out int lo, out int hi) is not null) return m.Value;
            int v = lo + rnd.Next(hi - lo + 1);
            return ":" + Format(v) + ")";
        });
    }

    /// <summary>문자열 안의 범위 표기 중 잘못된 첫 번째 것에 대한 설명. 없으면 null.</summary>
    public static string? FindError(string tag)
    {
        if (tag.IndexOf('~') < 0) return null;
        foreach (Match m in RangeTail.Matches(tag))
        {
            var err = TryParse(m, out _, out _);
            if (err is not null) return err;
        }
        return null;
    }

    private static string? TryParse(Match m, out int lo100, out int hi100)
    {
        lo100 = hi100 = 0;
        string loText = m.Groups["lo"].Value.Trim(), hiText = m.Groups["hi"].Value.Trim();
        if (!TryParseNumber(loText, out double lo) || !TryParseNumber(hiText, out double hi))
            return $"가중치 범위 '{m.Value.TrimEnd(')')}'를 숫자로 읽을 수 없습니다. (tag:1.1~1.3) 형식으로 쓰세요.";
        if (lo > hi)
            return $"가중치 범위의 최솟값({loText})이 최댓값({hiText})보다 큽니다.";
        lo100 = (int)Math.Round(lo * Scale, MidpointRounding.AwayFromZero);
        hi100 = (int)Math.Round(hi * Scale, MidpointRounding.AwayFromZero);
        return null;
    }

    private static bool TryParseNumber(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && Math.Abs(value) <= MaxMagnitude;

    /// <summary>소수 둘째 자리까지, 끝의 0은 뺀다(120→"1.2", 123→"1.23", 100→"1").</summary>
    private static string Format(int scaled) =>
        ((decimal)scaled / Scale).ToString("0.##", CultureInfo.InvariantCulture);
}
