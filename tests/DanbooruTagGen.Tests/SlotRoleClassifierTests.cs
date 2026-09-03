using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using Xunit;

namespace DanbooruTagGen.Tests;

/// <summary>슬롯 역할(Identity/Major/Minor/Cosmetic) 판정. visual_variety_scan.py의 AXIS_TIERS를
/// C# 쪽에 포팅한 것 — 레시피 빌더의 "MAJOR 축 조합 수" 경고가 이 분류를 그대로 쓴다.</summary>
public class SlotRoleClassifierTests
{
    [Fact]
    public void FixedSlotIsIdentity()
        => Assert.Equal(SlotRole.Identity, SlotRoleClassifier.Classify(new FixedSlot { Label = "기본" }));

    [Theory]
    [InlineData("체위", SlotRole.Major)]
    [InlineData("배경", SlotRole.Major)]
    [InlineData("옷 상태", SlotRole.Major)]
    [InlineData("표정", SlotRole.Minor)]
    [InlineData("구도", SlotRole.Cosmetic)]
    [InlineData("조명", SlotRole.Cosmetic)]
    [InlineData("가슴·유두 디테일", SlotRole.Cosmetic)]
    public void LabelDecidesRole(string label, SlotRole expected)
        => Assert.Equal(expected, SlotRoleClassifier.Classify(new RandomPoolSlot { Label = label }));

    [Fact]
    public void UnknownLabelIsReportedNotGuessed()
        => Assert.Equal(SlotRole.Unknown, SlotRoleClassifier.Classify(new RandomPoolSlot { Label = "듣도보도 못한 축" }));

    [Fact]
    public void AltWithDifferentVisualTagsIsMajorEvenIfLabelSaysOtherwise()
    {
        // 인질극의 "심리 상태" ALT는 이름과 달리 그룹마다 sex/after_sex를 품고 있어
        // 사실상 행위 진행 축이다. 라벨만 보면 MINOR로 잘못 세게 된다.
        var alt = new AlternativeSlot
        {
            Label = "심리 상태",
            Groups =
            {
                new AlternativeGroup { Tags = { "scared", "trembling" } },
                new AlternativeGroup { Tags = { "sex", "vaginal" } },
                new AlternativeGroup { Tags = { "after_sex" } },
            },
        };
        Assert.Equal(SlotRole.Major, SlotRoleClassifier.Classify(alt));
    }
}
