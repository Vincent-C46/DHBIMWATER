using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>관로선형 이경관 카탈로그 규격(docs/260 § G). 기대값은 핸드북 10·11항 인쇄값이다.</summary>
public sealed class CatalogReducerSpecSourceTests
{
    private static ReducerSpec? Find(ReducerForm form, BendConnection connection = BendConnection.Socket,
        string jointType = JointTypeCatalog.KpMechanical, string pipeKind = PipeKindCatalog.Water1, double large = 300d, double small = 150d)
        => new CatalogReducerSpecSource(connection, jointType, form, 250d, ReducerAlignment.Concentric).Find(pipeKind, large, small);

    [Theory]
    [InlineData(ReducerForm.AType, 350d, 0d, 0d)]
    [InlineData(ReducerForm.BType, 550d, 0d, 200d)]
    [InlineData(ReducerForm.CType, 550d, 200d, 0d)]
    public void 소켓_편락관은_형식별_길이를_쓴다(ReducerForm form, double body, double large, double small)
    {
        var spec = Find(form)!;

        Assert.Equal(body, spec.BodyLengthMm);
        Assert.Equal(large, spec.LargeStraightMm);
        Assert.Equal(small, spec.SmallStraightMm);
        Assert.Equal(350d, spec.TaperLengthMm);
        Assert.False(spec.IsFallback);
    }

    [Fact]
    public void 플랜지_편락관은_형식을_무시한_단일_L()
    {
        var spec = Find(ReducerForm.BType, BendConnection.Flanged, large: 600d, small: 500d)!;

        Assert.Equal(600d, spec.BodyLengthMm);
        Assert.Equal(0d, spec.LargeStraightMm + spec.SmallStraightMm);
    }

    [Fact]
    public void 표에_없는_조합은_입력_길이로_대체한다()
    {
        var spec = Find(ReducerForm.BType, large: 600d, small: 300d)!;

        Assert.Equal(250d, spec.BodyLengthMm);
        Assert.True(spec.IsFallback);
    }

    [Theory]
    [InlineData(SteelFittingClass.F12)]
    [InlineData(SteelFittingClass.F20)]
    public void 강관_편락관은_C_W_E_형상(SteelFittingClass steelClass)
    {
        // 강관 편락관 600×500: C 200 · W 500 · L 900 → E 200(docs/261). 형식 선택과 무관하다.
        var spec = new CatalogReducerSpecSource(BendConnection.Flanged, JointTypeCatalog.KpMechanical, ReducerForm.AType, 250d,
            ReducerAlignment.Concentric, steelClass).Find(PipeKindCatalog.Steel400A, 600d, 500d)!;

        Assert.Equal(900d, spec.BodyLengthMm);
        Assert.Equal(200d, spec.LargeStraightMm);
        Assert.Equal(200d, spec.SmallStraightMm);
        Assert.Equal(500d, spec.TaperLengthMm);
        Assert.False(spec.IsFallback);
    }

    [Fact]
    public void 강관도_표에_없는_조합은_입력_길이()
        => Assert.True(Find(ReducerForm.BType, pipeKind: PipeKindCatalog.Steel400A, large: 600d, small: 200d)!.IsFallback);

    [Fact]
    public void 강관_표_행수와_L은_C_W_E의_합()
    {
        Assert.Equal(396, SteelFittingSpecTable.Tees.Count);
        Assert.Equal(213, SteelFittingSpecTable.Tees.Count(x => x.Class == SteelFittingClass.F12));
        Assert.Equal(188, SteelFittingSpecTable.Reducers.Count);
        Assert.All(SteelFittingSpecTable.Reducers, x => Assert.True(x.SmallStraightMm > 0d && x.C > 0d && x.W > 0d));
        Assert.Equal(300d, SteelFittingSpecTable.FindReducer(2800d, 2700d)!.SmallStraightMm);   // E 인쇄 3000 → L·C·W로 300
    }

    [Fact]
    public void 강관_표에_중복_조합이_없다()
    {
        Assert.Equal(SteelFittingSpecTable.Tees.Count, SteelFittingSpecTable.Tees.Select(x => (x.Class, x.MainDn, x.BranchDn)).Distinct().Count());
        Assert.Equal(SteelFittingSpecTable.Reducers.Count, SteelFittingSpecTable.Reducers.Select(x => (x.LargeDn, x.SmallDn)).Distinct().Count());
    }

    [Fact]
    public void 대체_길이가_0이면_배치하지_않는다()
        => Assert.Null(new CatalogReducerSpecSource(BendConnection.Socket, JointTypeCatalog.KpMechanical, ReducerForm.BType, 0d, ReducerAlignment.Concentric)
            .Find(PipeKindCatalog.Water1, 600d, 300d));

    [Fact]
    public void 이형관_고정표_행수()
    {
        // 핸드북 쪽별 행 수(2026-10-02 좌표 추출 + 이미지 대조). 전사 누락을 잡는다.
        Assert.Equal(39, DuctileFittingSpecTable.SocketTees.Count);
        Assert.Equal(58, DuctileFittingSpecTable.FlangedTees.Count);
        Assert.Equal(40, DuctileFittingSpecTable.SocketTapers.Count);
        Assert.Equal(17, DuctileFittingSpecTable.FlangedTapers.Count);
        Assert.Equal(17, DuctileFittingSpecTable.Wyes.Count);
    }
}
