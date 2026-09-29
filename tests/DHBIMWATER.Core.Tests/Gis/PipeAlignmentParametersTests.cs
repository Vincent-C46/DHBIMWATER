using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>docs/178 — 일반모델 매개변수 정리와 두 배치 경로의 값 형태 통일.</summary>
public sealed class PipeAlignmentParametersTests
{
    [Theory]
    [InlineData("DH_접합방법")]
    [InlineData("DH_시점표고")]
    [InlineData("DH_종점표고")]
    [InlineData("DH_경사")]
    [InlineData("DH_절점측점")]
    [InlineData("DH_접합적용")]
    [InlineData("DH_곡률반경")]
    // docs/214 — DirectShape를 측점편집에서 뺀 뒤 읽는 곳이 없어진 5개.
    [InlineData("DH_외경")]
    [InlineData("DH_관두께")]
    [InlineData("DH_레코드번호")]
    [InlineData("DH_곡관형식")]
    [InlineData("DH_부설길이")]
    public void 폐지한_매개변수는_정의와_GUID표에_없다(string name)
    {
        Assert.DoesNotContain(PipeAlignmentParameters.Definitions, x => x.Name == name);
        Assert.False(PipeAlignmentParameters.Guids.ContainsKey(name));
    }

    [Fact]
    public void 무게와_단위중량은_Number_스펙이고_GUID가_서로_다르다()
    {
        // DH_무게는 종전 GUID 그대로(곡관 한 개 무게·직관 총무게), DH_단위중량은 새 GUID(직관 kg/m) — docs/178 § A-2.
        var weight = Assert.Single(PipeAlignmentParameters.Definitions, x => x.Name == PipeAlignmentParameters.Weight);
        var unitWeight = Assert.Single(PipeAlignmentParameters.Definitions, x => x.Name == PipeAlignmentParameters.UnitWeight);
        Assert.Equal(ParameterSpecType.Number, weight.SpecType);
        Assert.Equal(ParameterSpecType.Number, unitWeight.SpecType);
        Assert.Equal(new Guid("a6e1f5b0-9c1e-4d7a-8e2f-1b3c4d5e6f70"), weight.Guid);
        Assert.Equal(new Guid("89b45662-b0e6-4047-9fe7-1d40a8be0359"), unitWeight.Guid);
        Assert.Equal(PipeAlignmentParameters.Guids.Count, PipeAlignmentParameters.Guids.Values.Distinct().Count());
    }

    [Fact]
    public void 정의_이름_집합과_GUID표_키_집합이_같다()
    {
        var names = PipeAlignmentParameters.Definitions.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var keys = PipeAlignmentParameters.Guids.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, names);
        Assert.All(PipeAlignmentParameters.Definitions, x => Assert.Equal(PipeAlignmentParameters.Guids[x.Name], x.Guid));
    }

    [Fact]
    public void 호칭지름_표기는_DN_접두이고_두_구경은_곱표로_잇는다()
    {
        Assert.Equal("DN300", PipeAlignmentParameters.DiameterText(300));
        Assert.Equal("DN300×200", PipeAlignmentParameters.DiameterText(300, 200));
        Assert.Equal("DN82.5×50", PipeAlignmentParameters.DiameterText(82.5, 50));
    }

    [Fact]
    public void 분기규격은_D접두이고_여러_구경은_소문자x로_잇는다()
    {
        // 수량산출 규격2 칸이다. 호칭지름(DN)이 아니라 산출서 규격 표기(D)를 쓴다.
        Assert.Equal("D150", PipeAlignmentParameters.BranchSpecText(150));
        Assert.Equal("D600xD300", PipeAlignmentParameters.BranchSpecText(600, 300));
        Assert.Equal("D82.5", PipeAlignmentParameters.BranchSpecText(82.5));
        // 분기가 없는 부재는 빈칸이다 — 직관·곡관이 규격2를 차지하면 안 된다.
        Assert.Equal(string.Empty, PipeAlignmentParameters.BranchSpecText());
    }

    [Fact]
    public void 절단수는_Integer이고_기본값은_1이다()
    {
        var definition = Assert.Single(PipeAlignmentParameters.Definitions, x => x.Name == PipeAlignmentParameters.CutCount);
        Assert.Equal(ParameterSpecType.Integer, definition.SpecType);
        Assert.Equal(1, PipeAlignmentParameters.DefaultCutCount);
    }

    [Theory]
    // 우리가 짓는 부재명만 절단 대상이다. 접합 표기가 앞에 붙어도 떼고 본다.
    [InlineData("직관", true)]
    [InlineData("단관", true)]
    [InlineData("2S1F 곡관", true)]
    [InlineData("3F T형", true)]
    [InlineData("십자", true)]
    // 밸브·이음관은 완제품이라 자르지 않는다. 패밀리 이름이 그대로 부재명이 된다.
    [InlineData("제수밸브(BFV)", false)]
    [InlineData("공기밸브", false)]
    [InlineData("이음관", false)]
    [InlineData("", false)]
    public void 절단_대상은_관과_이형관뿐이다(string partName, bool expected)
        => Assert.Equal(expected, PipeMaterialSpecText.IsCutTarget(partName));

    // ── 밸브실 배관이 계획관로와 같은 이름·GUID·타입으로 네 칸을 더 갖는지(docs/178 § C-1) ──

    [Theory]
    [InlineData("DH_Part", ParameterSpecType.Text)]
    [InlineData("DH_호칭지름", ParameterSpecType.Text)]
    [InlineData("DH_연장", ParameterSpecType.Length)]
    [InlineData("DH_곡관규격각", ParameterSpecType.Angle)]
    [InlineData("DH_분기규격", ParameterSpecType.Text)]
    [InlineData("DH_절단수", ParameterSpecType.Integer)]
    public void 밸브실_배관도_계획관로와_같은_정보_매개변수를_갖는다(string name, ParameterSpecType spec)
    {
        var alignment = Assert.Single(PipeAlignmentParameters.Definitions, x => x.Name == name);
        var valveRoom = Assert.Single(ValvePipingParameters.Definitions, x => x.Name == name);
        Assert.Equal(spec, alignment.SpecType);
        Assert.Equal(spec, valveRoom.SpecType);
        Assert.Equal(alignment.Guid, valveRoom.Guid);
        Assert.Equal(PipeAlignmentParameters.Guids[name], ValvePipingParameters.Guids[name]);
        // 계획관로는 일반 모델, 밸브실은 배관 밸브류·이음쇠 — EnsureParameters가 한 바인딩에 병합한다.
        Assert.Equal([ParameterCategory.GenericModel], alignment.Categories);
        Assert.Equal([ParameterCategory.PipeAccessory, ParameterCategory.PipeFitting], valveRoom.Categories);
    }

    [Fact]
    public void 밸브실_상수는_계획관로_상수를_그대로_가리킨다()
    {
        Assert.Equal(PipeAlignmentParameters.Part, ValvePipingParameters.Part);
        Assert.Equal(PipeAlignmentParameters.NominalDiameter, ValvePipingParameters.NominalDiameter);
        Assert.Equal(PipeAlignmentParameters.Length, ValvePipingParameters.Length);
        Assert.Equal(PipeAlignmentParameters.StandardAngle, ValvePipingParameters.BendAngle);
    }
}
