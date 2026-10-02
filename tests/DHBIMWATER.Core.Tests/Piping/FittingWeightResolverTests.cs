using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>밸브실 배관 부재 카탈로그 무게 규칙(docs/260 § B). 기대값은 핸드북 PDF 인쇄값이다.</summary>
public sealed class FittingWeightResolverTests
{
    private static readonly BendSettings Settings = BendSettings.Default.WithSteelDefaults();

    private static FittingWeight Resolve(FittingWeightPart part, PipeMaterial material, BendConnection connection,
        double dn, double? branch = null, double? lengthMm = null, double? deflection = null, double? wyeAngle = null,
        string? pipeKind = PipeKindCatalog.Water1, SteelFittingClass steelClass = SteelFittingClass.F12)
        => FittingWeightResolver.Resolve(
            new FittingWeightInput(part, material, connection, steelClass, pipeKind, dn, branch, lengthMm, deflection, wyeAngle), Settings);

    [Fact]
    public void 직관_소켓은_단위중량x연장에_KP_소켓무게를_더한다()
    {
        var spec = Settings.StraightPipes.Find(PipeKindCatalog.Water1, 300d)!;
        var weight = Resolve(FittingWeightPart.Straight, PipeMaterial.DuctileIron, BendConnection.Socket, 300d, lengthMm: 6000d);

        Assert.Equal(spec.UnitWeightKgPerM * 6d + spec.SocketWeightKpKg, weight.WeightKg!.Value, 6);
        Assert.Equal(spec.UnitWeightKgPerM, weight.UnitWeightKgPerM);
    }

    [Fact]
    public void 직관_플랜지는_소켓무게를_더하지_않는다()
    {
        var spec = Settings.StraightPipes.Find(PipeKindCatalog.Water1, 300d)!;
        var weight = Resolve(FittingWeightPart.Straight, PipeMaterial.DuctileIron, BendConnection.Flanged, 300d, lengthMm: 1500d);

        Assert.Equal(spec.UnitWeightKgPerM * 1.5d, weight.WeightKg!.Value, 6);
    }

    [Fact]
    public void 주철_직관_등급이_없으면_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Straight, PipeMaterial.DuctileIron, BendConnection.Flanged, 300d, lengthMm: 1500d, pipeKind: "").WeightKg);

    [Theory]
    [InlineData(300d, PipeKindCatalog.Steel290)]
    [InlineData(400d, PipeKindCatalog.Steel400A)]
    public void 강관_등급이_비면_DN별_기본등급(double dn, string expected)
        => Assert.Equal(expected, FittingWeightResolver.ResolveGrade(PipeMaterial.Steel, "", dn));

    [Fact]
    public void 강관_직관_자동등급은_STWW290_단위중량()
    {
        // 강관은 소켓(=용접) 무게가 0이다. DN300 STWW 290 = 53 kg/m.
        var weight = Resolve(FittingWeightPart.Straight, PipeMaterial.Steel, BendConnection.Socket, 300d, lengthMm: 6000d, pipeKind: "");

        Assert.Equal(53d, weight.UnitWeightKgPerM);
        Assert.Equal(318d, weight.WeightKg!.Value, 6);
    }

    [Fact]
    public void 등급과_관종이_다르면_등급을_쓰지_않는다()
        => Assert.Null(FittingWeightResolver.ResolveGrade(PipeMaterial.DuctileIron, PipeKindCatalog.Steel290, 300d));

    [Fact]
    public void 곡관_허용오차_안이면_표준각_KP_B형_무게()
    {
        BendFittingCatalog.TryGetHandbookWeight(400d, 45d, BendForm.BType, out var kp, out _);
        Assert.Equal(88.5d, kp);
        Assert.Equal(kp, Resolve(FittingWeightPart.Bend, PipeMaterial.DuctileIron, BendConnection.Socket, 400d, deflection: 45.4d).WeightKg);
    }

    [Fact]
    public void 곡관_허용오차_밖이면_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Bend, PipeMaterial.DuctileIron, BendConnection.Socket, 400d, deflection: 40d).WeightKg);

    [Theory]
    [InlineData(90d, 300d, 71.0d)]
    [InlineData(45d, 600d, 218d)]
    public void 주철_플랜지곡관은_핸드북_B형_무게(double angle, double dn, double expected)
        => Assert.Equal(expected, Resolve(FittingWeightPart.Bend, PipeMaterial.DuctileIron, BendConnection.Flanged, dn, deflection: angle).WeightKg);

    [Fact]
    public void 주철_플랜지곡관_22도는_표가_없어_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Bend, PipeMaterial.DuctileIron, BendConnection.Flanged, 300d, deflection: 22.5d).WeightKg);

    [Fact]
    public void 강관_곡관은_F종류별_무게()
        => Assert.Equal(129d, Resolve(FittingWeightPart.Bend, PipeMaterial.Steel, BendConnection.Socket, 600d, deflection: 90d, steelClass: SteelFittingClass.F15).WeightKg);

    [Theory]
    [InlineData(BendConnection.Socket, 333d)]   // 소켓 T 600×600 KP B형
    [InlineData(BendConnection.Flanged, 378d)]  // 플랜지 T 600×600 B형
    public void 주철_T형은_접합별_표(BendConnection connection, double expected)
        => Assert.Equal(expected, Resolve(FittingWeightPart.Tee, PipeMaterial.DuctileIron, connection, 600d, 600d).WeightKg);

    [Fact]
    public void 소켓_T가_없는_DN은_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Tee, PipeMaterial.DuctileIron, BendConnection.Socket, 700d, 700d).WeightKg);

    [Fact]
    public void 십자는_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Cross, PipeMaterial.DuctileIron, BendConnection.Flanged, 300d, 300d).WeightKg);

    [Theory]
    [InlineData(SteelFittingClass.F12, 300d, 51.6d)]     // 부도 6 p.162
    [InlineData(SteelFittingClass.F15, 700d, 177d)]      // p.167 (보강판 포함)
    [InlineData(SteelFittingClass.F12, 1100d, null)]     // F12 1100×1100 행 없음
    [InlineData(SteelFittingClass.F20, 300d, null)]      // F20 표 없음(협의)
    public void 강관_T형은_종류별_T자관_표(SteelFittingClass steelClass, double dn, double? expected)
        => Assert.Equal(expected, Resolve(FittingWeightPart.Tee, PipeMaterial.Steel, BendConnection.Socket, dn, dn, steelClass: steelClass).WeightKg);

    [Fact]
    public void 강관_T형은_종류를_모르면_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Tee, PipeMaterial.Steel, BendConnection.Socket, 300d, 300d, steelClass: SteelFittingClass.None).WeightKg);

    [Theory]
    [InlineData(BendConnection.Socket, 45d, 139d)]
    [InlineData(BendConnection.Flanged, 45.3d, 128d)]
    public void 주철_Y형은_45도일_때_접합별_열(BendConnection connection, double angle, double expected)
        => Assert.Equal(expected, Resolve(FittingWeightPart.Wye, PipeMaterial.DuctileIron, connection, 300d, 300d, wyeAngle: angle).WeightKg);

    [Fact]
    public void Y형_분기각이_다르면_비운다()
        => Assert.Null(Resolve(FittingWeightPart.Wye, PipeMaterial.DuctileIron, BendConnection.Socket, 300d, 300d, wyeAngle: 60d).WeightKg);

    [Theory]
    [InlineData(BendConnection.Socket, 70d)]    // 부관 A 300×100 B형(KP)
    [InlineData(BendConnection.Flanged, 79d)]   // C형(플랜지)
    public void 주철_밸브부관A는_접합별_열(BendConnection connection, double expected)
        => Assert.Equal(expected, Resolve(FittingWeightPart.ValveTee, PipeMaterial.DuctileIron, connection, 300d, 100d).WeightKg);

    [Fact]
    public void 주철_밸브부관B는_표준형_무게()
        => Assert.Equal(15d, Resolve(FittingWeightPart.ValveBend, PipeMaterial.DuctileIron, BendConnection.Flanged, 300d, 100d).WeightKg);

    [Fact]
    public void 강관_밸브부관은_종류를_모르면_비운다()
    {
        Assert.Null(Resolve(FittingWeightPart.ValveTee, PipeMaterial.Steel, BendConnection.Flanged, 400d, 100d, steelClass: SteelFittingClass.None).WeightKg);
        Assert.Equal(60.2d, Resolve(FittingWeightPart.ValveTee, PipeMaterial.Steel, BendConnection.Flanged, 400d, 100d, steelClass: SteelFittingClass.F12).WeightKg);
    }

    [Theory]
    [InlineData("직관", FittingWeightPart.Straight)]
    [InlineData("단관", FittingWeightPart.Straight)]
    [InlineData("곡관", FittingWeightPart.Bend)]
    [InlineData("T형", FittingWeightPart.Tee)]
    [InlineData("십자", FittingWeightPart.Cross)]
    [InlineData("Y형", FittingWeightPart.Wye)]
    [InlineData("제수밸브", FittingWeightPart.Other)]
    public void 품목명으로_종류를_가른다(string name, FittingWeightPart expected)
        => Assert.Equal(expected, FittingWeightResolver.PartOf(name));

    [Fact]
    public void 관종을_모르면_비운다()
        => Assert.Null(FittingWeightResolver.Resolve(new FittingWeightInput(FittingWeightPart.Tee, null, BendConnection.Socket,
            SteelFittingClass.None, null, 300d, 300d), Settings).WeightKg);
}
