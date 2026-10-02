using DHBIMWATER.Core.Structures;
using Xunit;

namespace DHBIMWATER.Core.Tests.Structures;

/// <summary>공용 콘크리트 강도 설정(docs/249 § A).</summary>
public sealed class ConcreteSettingsTests
{
    [Fact]
    public void Default_IsPumpingStationDefaults()
    {
        var d = ConcreteSettings.Default;
        foreach (var member in ConcreteSettings.Members)
        {
            var expected = member == ConcreteMember.LeanConcrete ? "25-18-120" : "25-24-120";
            Assert.Equal(expected, d.Get(member).MaterialName);
        }
    }

    [Theory]
    // 밸브실
    [InlineData("버림콘크리트", ConcreteMember.LeanConcrete)]
    [InlineData("기초", ConcreteMember.Foundation)]
    [InlineData("중간슬래브", ConcreteMember.Slab)]
    [InlineData("상부슬래브", ConcreteMember.Slab)]
    // 배수지
    [InlineData("기초콘크리트", ConcreteMember.Foundation)]
    [InlineData("바닥슬래브_덧침콘크리트", ConcreteMember.Slab)]
    [InlineData("중간슬래브_덧침콘크리트", ConcreteMember.Slab)]
    [InlineData("전기실_덧침콘크리트", ConcreteMember.Slab)]
    // 토출조
    [InlineData("바닥슬래브", ConcreteMember.Slab)]
    // 펌프장 DirectShape
    [InlineData("기초버림슬래브", ConcreteMember.LeanConcrete)]
    [InlineData("기초슬래브", ConcreteMember.Foundation)]
    [InlineData("", ConcreteMember.Slab)]
    public void ClassifySlabPart(string part, ConcreteMember expected)
        => Assert.Equal(expected, ConcreteSettings.ClassifySlabPart(part));

    [Fact]
    public void With_ReplacesOnlyThatMember()
    {
        var spec = new ConcreteSpec(25, 30, 150);
        var s = ConcreteSettings.Default.With(ConcreteMember.Column, spec);
        Assert.Equal(spec, s.Column);
        Assert.Equal(ConcreteSettings.DefaultSpec, s.Beam);
    }

    [Fact]
    public void ResolveTypeNames_SingleConcretePerThickness_KeepsPlainName()
    {
        var c24 = new ConcreteSpec(25, 24, 120);
        var c18 = new ConcreteSpec(25, 18, 120);
        var names = ConcreteSettings.ResolveTypeNames(new[] { (200.0, c24), (200.0, c24), (100.0, c18) });
        Assert.Equal("일반 - 200mm", names[(200.0, c24)]);
        Assert.Equal("일반 - 100mm", names[(100.0, c18)]);
    }

    [Fact]
    public void ResolveTypeNames_MixedConcreteSameThickness_AppendsStrength()
    {
        var c24 = new ConcreteSpec(25, 24, 120);
        var c18 = new ConcreteSpec(25, 18, 120);
        var c30 = new ConcreteSpec(25, 30, 150);
        var names = ConcreteSettings.ResolveTypeNames(new[] { (200.0, c24), (200.0, c18), (300.0, c30) });
        Assert.Equal("일반 - 200mm_24MPa", names[(200.0, c24)]);
        Assert.Equal("일반 - 200mm_18MPa", names[(200.0, c18)]);
        Assert.Equal("일반 - 300mm", names[(300.0, c30)]);
    }
}
