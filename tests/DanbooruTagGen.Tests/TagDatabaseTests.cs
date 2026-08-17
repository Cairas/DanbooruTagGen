using System.Text;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Tags;
using Xunit;

namespace DanbooruTagGen.Tests;

public class TagDatabaseTests
{
    private static TagDatabase Sample() => TagDatabase.FromTags(new[]
    {
        new Tag("smile", TagCategory.General, 5000, new[] { "미소", "happy_face" }),
        new Tag("smirk", TagCategory.General, 100, Array.Empty<string>()),
        new Tag("grin", TagCategory.General, 3000, new[] { "grinning" }),
        new Tag("serious", TagCategory.General, 200, Array.Empty<string>()),
    });

    [Fact]
    public void PrefixMatchesByName()
    {
        var r = Sample().Autocomplete("sm");
        Assert.Equal(new[] { "smile", "smirk" }, r.Select(t => t.Name));
    }

    [Fact]
    public void OrdersByPostCountDescending()
    {
        var r = Sample().Autocomplete("s");
        // smile(5000) > serious(200) > smirk(100)
        Assert.Equal(new[] { "smile", "serious", "smirk" }, r.Select(t => t.Name));
    }

    [Fact]
    public void MatchesAliasPrefix()
    {
        var r = Sample().Autocomplete("happy");
        Assert.Contains(r, t => t.Name == "smile");
    }

    [Fact]
    public void ShortKoreanAliasMatches()
    {
        var r = Sample().Autocomplete("미소");
        Assert.Single(r);
        Assert.Equal("smile", r[0].Name);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var r = Sample().Autocomplete("SM");
        Assert.Equal(new[] { "smile", "smirk" }, r.Select(t => t.Name));
    }

    [Fact]
    public void RespectsLimit()
    {
        Assert.Single(Sample().Autocomplete("s", limit: 1));
    }

    [Fact]
    public void EmptyPrefixReturnsEmpty()
    {
        Assert.Empty(Sample().Autocomplete(""));
    }

    [Fact]
    public void KoreanAliasMatchesButInsertsTagName()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("bikini", TagCategory.General, 500000, new[] { "비키니", "水着" }),
            new Tag("smile", TagCategory.General, 2000000, new[] { "미소" }),
        });
        var r = db.Autocomplete("비키니");
        Assert.Single(r);
        Assert.Equal("bikini", r[0].Name); // 검색은 한글, 삽입값은 영문 danbooru 태그
    }

    [Fact]
    public void LoadFromFilesUnionsAliasesForSameTag()
    {
        // 보강 파일(ko-aliases.csv)이 기존 태그에 한국어 별칭을 더하는 핵심 동작.
        var f1 = Path.GetTempFileName();
        var f2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f1, "bikini,0,500000,\"水着\"\n", new UTF8Encoding(false));
            File.WriteAllText(f2, "bikini,0,0,\"비키니\"\n", new UTF8Encoding(false));
            var db = TagDatabase.LoadFromFiles(new[] { f1, f2 });

            Assert.Equal(1, db.Count);                                   // 같은 태그는 하나로 병합
            Assert.Equal("bikini", db.Autocomplete("비키니")[0].Name);   // 뒤 파일의 한국어 별칭으로 검색
            Assert.Equal("bikini", db.Autocomplete("水着")[0].Name);     // 앞 파일의 별칭도 유지
            Assert.Equal(500000, db.Autocomplete("비키니")[0].PostCount); // 빈도는 첫 파일 것 유지
        }
        finally { File.Delete(f1); File.Delete(f2); }
    }

    [Fact]
    public void NsfwFileMarksTagsAndMergesWithOr()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dtg_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var sfw = Path.Combine(dir, "ko-categories.csv");
        var nsfw = Path.Combine(dir, "ko-nsfw.csv");
        try
        {
            File.WriteAllText(sfw, "smile,0,1000,\"미소,표정\"\nskirt,0,1500,\"치마,의상\"\n", new UTF8Encoding(false));
            File.WriteAllText(nsfw, "panties,0,2000,\"팬티,노출\"\nskirt,0,0,\"미니\"\n", new UTF8Encoding(false));
            var db = TagDatabase.LoadFromFiles(new[] { sfw, nsfw });

            Assert.False(db.Autocomplete("미소")[0].IsNsfw);  // sfw 파일 단독
            Assert.True(db.Autocomplete("팬티")[0].IsNsfw);   // nsfw 파일 단독
            Assert.True(db.Autocomplete("치마")[0].IsNsfw);   // 양쪽에 존재 → OR로 성인 처리
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TopByPostCountFiltersByNsfw()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("sfwtag", TagCategory.General, 100, Array.Empty<string>(), "", false),
            new Tag("nsfwtag", TagCategory.General, 200, Array.Empty<string>(), "", true),
        });
        Assert.Equal(new[] { "nsfwtag" }, db.TopByPostCount(10, t => t.IsNsfw).Select(t => t.Name));
        Assert.Equal(new[] { "sfwtag" }, db.TopByPostCount(10, t => !t.IsNsfw).Select(t => t.Name));
    }

    [Fact]
    public void SearchRankedFindsSubstringNotJustPrefix()
    {
        // "hair" is a substring of "long_hair" but not a prefix -> Autocomplete would miss it.
        var db = TagDatabase.FromTags(new[] { new Tag("long_hair", TagCategory.General, 1000, Array.Empty<string>()) });
        Assert.Empty(db.Autocomplete("hair"));
        Assert.Equal(new[] { "long_hair" }, db.SearchRanked("hair").Select(t => t.Name));
    }

    [Fact]
    public void SearchRankedOrdersExactBeforePrefixBeforeContains()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("eyes", TagCategory.General, 1, Array.Empty<string>()),                  // exact
            new Tag("eyes_closed", TagCategory.General, 999999, Array.Empty<string>()),       // prefix (higher freq, still ranks below exact)
            new Tag("blue_eyes", TagCategory.General, 999999, Array.Empty<string>()),         // contains only
        });
        var r = db.SearchRanked("eyes", limit: 10);
        Assert.Equal(new[] { "eyes", "eyes_closed", "blue_eyes" }, r.Select(t => t.Name));
    }

    [Fact]
    public void SearchRankedSkipsContainsScanWhenLimitAlreadyFilled()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("smile", TagCategory.General, 100, Array.Empty<string>()),       // prefix
            new Tag("smirk", TagCategory.General, 50, Array.Empty<string>()),        // prefix
            new Tag("awesomile", TagCategory.General, 999999, Array.Empty<string>()), // contains-only, would win on frequency if included
        });
        // limit=2: 두 접두일치만으로 이미 채워지므로 부분포함(awesomile)까지 갈 필요 없음
        var r = db.SearchRanked("sm", limit: 2);
        Assert.Equal(new[] { "smile", "smirk" }, r.Select(t => t.Name));
    }

    [Fact]
    public void UnderscoreAndSpaceAreEquivalentInSearch()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("long_hair", TagCategory.General, 1000, Array.Empty<string>()),
        });
        Assert.Equal("long_hair", db.Autocomplete("long hair")[0].Name);  // 공백으로 검색해도 매칭
        Assert.Equal("long_hair", db.Autocomplete("long_hair")[0].Name);  // 언더스코어로도 매칭
    }

    [Fact]
    public void SearchThemeMatchesDescriptionUnlikeSearchRanked()
    {
        // 컨셉 빌더 위저드의 핵심 계약: "비" 같은 테마 단어가 이름/별칭엔 없어도
        // 한국어 설명에 있으면 걸려야 한다.
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("umbrella", TagCategory.General, 900, new[] { "우산", "소품" }, "비 올 때 쓰는 우산"),
            new Tag("puddle", TagCategory.General, 300, new[] { "물웅덩이", "배경" }, "비 온 뒤 물웅덩이"),
            new Tag("smile", TagCategory.General, 5000, new[] { "미소", "표정" }, "웃는 얼굴"),
        });
        var r = db.SearchTheme("비");
        Assert.Equal(new[] { "umbrella", "puddle" }, r.Select(t => t.Name)); // 빈도순
        Assert.Empty(db.SearchRanked("비"));                                // 기존 검색은 설명을 안 봄
    }

    [Fact]
    public void SearchThemeAlsoMatchesNameAndAlias()
    {
        var db = TagDatabase.FromTags(new[]
        {
            new Tag("rain", TagCategory.General, 900, new[] { "빗줄기", "배경" }, ""),
            new Tag("onsen", TagCategory.General, 800, new[] { "온천", "배경" }, "노천 온천"),
        });
        Assert.Contains(db.SearchTheme("rain"), t => t.Name == "rain");   // 이름
        Assert.Contains(db.SearchTheme("온천"), t => t.Name == "onsen");  // 별칭
    }
}
