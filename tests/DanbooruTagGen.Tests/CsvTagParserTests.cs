using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Tags;
using Xunit;

namespace DanbooruTagGen.Tests;

public class CsvTagParserTests
{
    [Fact]
    public void ParsesBasicRow()
    {
        Assert.True(CsvTagParser.TryParseLine("1girl,0,5300000,\"1girls,one_girl\"", out var tag));
        Assert.Equal("1girl", tag.Name);
        Assert.Equal(TagCategory.General, tag.Category);
        Assert.Equal(5300000, tag.PostCount);
        Assert.Equal(new[] { "1girls", "one_girl" }, tag.Aliases);
    }

    [Fact]
    public void ParsesRowWithoutAliases()
    {
        Assert.True(CsvTagParser.TryParseLine("solo,0,4000000,", out var tag));
        Assert.Equal("solo", tag.Name);
        Assert.Empty(tag.Aliases);
    }

    [Fact]
    public void PreservesUnderscoresAndParens()
    {
        Assert.True(CsvTagParser.TryParseLine("heart_(symbol),0,12345,", out var tag));
        Assert.Equal("heart_(symbol)", tag.Name);
    }

    [Fact]
    public void MapsCharacterCategory()
    {
        Assert.True(CsvTagParser.TryParseLine("hatsune_miku,4,900000,\"miku\"", out var tag));
        Assert.Equal(TagCategory.Character, tag.Category);
    }

    [Fact]
    public void UnknownCategoryFallsBack()
    {
        Assert.True(CsvTagParser.TryParseLine("weird,99,1,", out var tag));
        Assert.Equal(TagCategory.Unknown, tag.Category);
    }

    [Fact]
    public void ToleratesNonNumericCount()
    {
        // extra-quality-tags.csv 형식: masterpiece,5,Quality tag,,
        Assert.True(CsvTagParser.TryParseLine("masterpiece,5,Quality tag,,", out var tag));
        Assert.Equal("masterpiece", tag.Name);
        Assert.Equal(TagCategory.Meta, tag.Category);
        Assert.Equal(0, tag.PostCount);
    }

    [Fact]
    public void PreservesCjkAliases()
    {
        // 다국어 사전: 한국어 별칭이 검색 키로 보존되어야 함
        Assert.True(CsvTagParser.TryParseLine("bikini,0,500000,\"비키니,水着,泳装\"", out var tag));
        Assert.Contains("비키니", tag.Aliases);
        Assert.Equal("bikini", tag.Name);
    }

    [Fact]
    public void ParsesOptionalDescriptionFifthField()
    {
        // 큐레이션 파일 형식: 태그,카테고리,빈도,"별칭","한국어 설명"
        Assert.True(CsvTagParser.TryParseLine("smile,0,5000,\"미소,웃음,표정\",\"웃는 표정\"", out var tag));
        Assert.Equal("웃는 표정", tag.Description);
        Assert.Contains("표정", tag.Aliases);   // 그룹 키워드가 별칭으로 검색됨
    }

    [Fact]
    public void DescriptionEmptyWhenOnlyFourFields()
    {
        Assert.True(CsvTagParser.TryParseLine("solo,0,4000000,", out var tag));
        Assert.Equal("", tag.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name_only")]
    [InlineData("name,notanumber,1,")]
    [InlineData("tag,category,count,alias")]   // 헤더 행도 거부(category 비숫자)
    public void RejectsMalformedRows(string line)
    {
        Assert.False(CsvTagParser.TryParseLine(line, out _));
    }
}
