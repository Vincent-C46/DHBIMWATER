using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public sealed class StraightPipeSpecRow : ViewModelBase
{
    private double _diameterMm, _outerDiameterMm, _water1, _water2, _water3, _water4, _sewer1, _sewer2, _sewer3;
    // 직관부 M무게(kg/m) — 관종(등급)별. "직관 무게" 탭이 같은 행을 다른 열로 보여 준다(docs/178 § A-2).
    private double _weightWater1, _weightWater2, _weightWater3, _weightWater4, _weightSewer1, _weightSewer2, _weightSewer3;
    // 소켓 무게(kg) — DN 단일값(관종 무관). OD와 같은 자리다.
    private double _socketWeightKpKg, _socketWeightTytonKg;
    public double DiameterMm { get => _diameterMm; set => SetProperty(ref _diameterMm, value); }
    public double OuterDiameterMm { get => _outerDiameterMm; set => SetProperty(ref _outerDiameterMm, value); }
    public double Water1 { get => _water1; set => SetProperty(ref _water1, value); }
    public double Water2 { get => _water2; set => SetProperty(ref _water2, value); }
    public double Water3 { get => _water3; set => SetProperty(ref _water3, value); }
    public double Water4 { get => _water4; set => SetProperty(ref _water4, value); }
    public double Sewer1 { get => _sewer1; set => SetProperty(ref _sewer1, value); }
    public double Sewer2 { get => _sewer2; set => SetProperty(ref _sewer2, value); }
    public double Sewer3 { get => _sewer3; set => SetProperty(ref _sewer3, value); }
    public double WeightWater1 { get => _weightWater1; set => SetProperty(ref _weightWater1, value); }
    public double WeightWater2 { get => _weightWater2; set => SetProperty(ref _weightWater2, value); }
    public double WeightWater3 { get => _weightWater3; set => SetProperty(ref _weightWater3, value); }
    public double WeightWater4 { get => _weightWater4; set => SetProperty(ref _weightWater4, value); }
    public double WeightSewer1 { get => _weightSewer1; set => SetProperty(ref _weightSewer1, value); }
    public double WeightSewer2 { get => _weightSewer2; set => SetProperty(ref _weightSewer2, value); }
    public double WeightSewer3 { get => _weightSewer3; set => SetProperty(ref _weightSewer3, value); }
    public double SocketWeightKpKg { get => _socketWeightKpKg; set => SetProperty(ref _socketWeightKpKg, value); }
    public double SocketWeightTytonKg { get => _socketWeightTytonKg; set => SetProperty(ref _socketWeightTytonKg, value); }

    public IEnumerable<StraightPipeSpec> ToSpecs()
    {
        yield return Spec(PipeKindCatalog.Water1, Water1, WeightWater1);
        yield return Spec(PipeKindCatalog.Water2, Water2, WeightWater2);
        yield return Spec(PipeKindCatalog.Water3, Water3, WeightWater3);
        yield return Spec(PipeKindCatalog.Water4, Water4, WeightWater4);
        yield return Spec(PipeKindCatalog.Sewer1, Sewer1, WeightSewer1);
        yield return Spec(PipeKindCatalog.Sewer2, Sewer2, WeightSewer2);
        yield return Spec(PipeKindCatalog.Sewer3, Sewer3, WeightSewer3);
    }

    private StraightPipeSpec Spec(string pipeKind, double thicknessMm, double unitWeightKgPerM)
        => new(pipeKind, DiameterMm, OuterDiameterMm, thicknessMm, unitWeightKgPerM, SocketWeightKpKg, SocketWeightTytonKg);
}

/// <summary>강관 직관 제원 행(docs/211 § H-1). 외경은 DN당 고정이고 등급(STWW 290/370/400 A/400 B)별 두께·kg/m만 다르다.
/// 주철관 행(<see cref="StraightPipeSpecRow"/>)과 같은 DN이라도 외경이 달라 같은 행에 열을 더할 수 없어 별도 행이다.
/// 빈 칸(0)은 미수록 — 그 등급으로 배치하면 규격 미매칭이다.</summary>
public sealed class SteelStraightPipeSpecRow : ViewModelBase
{
    private double _diameterMm, _outerDiameterMm;
    private double _t290, _w290, _t370, _w370, _t400A, _w400A, _t400B, _w400B;
    public double DiameterMm { get => _diameterMm; set => SetProperty(ref _diameterMm, value); }
    public double OuterDiameterMm { get => _outerDiameterMm; set => SetProperty(ref _outerDiameterMm, value); }
    public double Thickness290 { get => _t290; set => SetProperty(ref _t290, value); }
    public double Weight290 { get => _w290; set => SetProperty(ref _w290, value); }
    public double Thickness370 { get => _t370; set => SetProperty(ref _t370, value); }
    public double Weight370 { get => _w370; set => SetProperty(ref _w370, value); }
    public double Thickness400A { get => _t400A; set => SetProperty(ref _t400A, value); }
    public double Weight400A { get => _w400A; set => SetProperty(ref _w400A, value); }
    public double Thickness400B { get => _t400B; set => SetProperty(ref _t400B, value); }
    public double Weight400B { get => _w400B; set => SetProperty(ref _w400B, value); }

    public void SetKindValues(string kind, double thicknessMm, double unitWeightKgPerM)
    {
        switch (kind)
        {
            case PipeKindCatalog.Steel290: Thickness290 = thicknessMm; Weight290 = unitWeightKgPerM; break;
            case PipeKindCatalog.Steel370: Thickness370 = thicknessMm; Weight370 = unitWeightKgPerM; break;
            case PipeKindCatalog.Steel400A: Thickness400A = thicknessMm; Weight400A = unitWeightKgPerM; break;
            case PipeKindCatalog.Steel400B: Thickness400B = thicknessMm; Weight400B = unitWeightKgPerM; break;
        }
    }

    /// <summary>두께가 있는 등급만 낸다. 소켓 무게는 강관에 없으므로 0이다.</summary>
    public IEnumerable<StraightPipeSpec> ToSpecs()
    {
        if (Thickness290 > 0) yield return new StraightPipeSpec(PipeKindCatalog.Steel290, DiameterMm, OuterDiameterMm, Thickness290, Weight290);
        if (Thickness370 > 0) yield return new StraightPipeSpec(PipeKindCatalog.Steel370, DiameterMm, OuterDiameterMm, Thickness370, Weight370);
        if (Thickness400A > 0) yield return new StraightPipeSpec(PipeKindCatalog.Steel400A, DiameterMm, OuterDiameterMm, Thickness400A, Weight400A);
        if (Thickness400B > 0) yield return new StraightPipeSpec(PipeKindCatalog.Steel400B, DiameterMm, OuterDiameterMm, Thickness400B, Weight400B);
    }
}

public sealed class JointDeflectionRow : ViewModelBase
{
    private string _jointType = JointTypeCatalog.KpMechanical;
    private double _diameterMm, _allowableDeg;
    public string JointType { get => _jointType; set => SetProperty(ref _jointType, value); }
    public double DiameterMm { get => _diameterMm; set => SetProperty(ref _diameterMm, value); }
    public double AllowableDeg { get => _allowableDeg; set => SetProperty(ref _allowableDeg, value); }
}

public sealed class BendFittingRow : ViewModelBase
{
    private double _diameterMm, _angleDeg = 45d, _layingLengthMm, _centerlineRadiusMm, _wallThicknessMm, _weightKpMechanicalKg, _weightTytonKg, _weightKg;
    private BendForm _form = BendForm.BType;
    private BendConnection _connection = BendConnection.Socket;
    private PipeMaterial _material = PipeMaterial.DuctileIron;
    private SteelFittingClass _fittingClass = SteelFittingClass.None;
    /// <summary>행의 관종. 강관 행은 s(추가 다리)가 0이고 종류(F)·한 개 무게 열을 쓴다(docs/211 § H-2).</summary>
    public PipeMaterial Material { get => _material; set { if (SetProperty(ref _material, value)) { OnPropertyChanged(nameof(ExtraLegLengthMm)); OnPropertyChanged(nameof(LongLegLengthMm)); } } }
    /// <summary>강관 이형관 종류(F12/F15/F20). 주철관 행은 None.</summary>
    public SteelFittingClass FittingClass { get => _fittingClass; set => SetProperty(ref _fittingClass, value); }
    /// <summary>강관 곡관 한 개 무게(kg, 접합 종류 무관). 주철관 행은 0.</summary>
    public double WeightKg { get => _weightKg; set => SetProperty(ref _weightKg, value); }
    public double DiameterMm { get => _diameterMm; set => SetProperty(ref _diameterMm, value); }
    public double AngleDeg { get => _angleDeg; set { if (SetProperty(ref _angleDeg, value)) OnPropertyChanged(nameof(TangentLengthMm)); } }
    /// <summary>기본은 B형(한쪽 소켓). A형이 필요한 자리만 사용자가 바꾸며, 바뀌면 그 형식의 핸드북 무게로 다시 채운다.</summary>
    public BendForm Form { get => _form; set { if (SetProperty(ref _form, value)) { OnPropertyChanged(nameof(LongLegLengthMm)); OnPropertyChanged(nameof(ExtraLegLengthMm)); RefreshWeightDefaults(); } } }
    /// <summary>그리드에는 노출하지 않고 소켓/플랜지 컬렉션을 저장할 때 접합종류를 보존한다.</summary>
    public BendConnection Connection { get => _connection; set => SetProperty(ref _connection, value); }
    public double LayingLengthMm { get => _layingLengthMm; set { if (SetProperty(ref _layingLengthMm, value)) OnPropertyChanged(nameof(LongLegLengthMm)); } }
    public double CenterlineRadiusMm { get => _centerlineRadiusMm; set { if (SetProperty(ref _centerlineRadiusMm, value)) OnPropertyChanged(nameof(TangentLengthMm)); } }
    /// <summary>s — B형(한쪽 소켓)만 200mm, A형(양쪽 소켓)은 0. 접합 종류와 무관한 계산값이며 편집 대상이 아니다. 강관은 소켓이 없어 0.</summary>
    public double ExtraLegLengthMm => Material == PipeMaterial.Steel ? 0d : Form == BendForm.BType ? 200d : 0d;
    public double WallThicknessMm { get => _wallThicknessMm; set => SetProperty(ref _wallThicknessMm, value); }
    /// <summary>KP 메커니컬 조인트 기준 무게(kg). 현재 <see cref="Form"/>에 대응하는 값이다.</summary>
    public double WeightKpMechanicalKg { get => _weightKpMechanicalKg; set => SetProperty(ref _weightKpMechanicalKg, value); }
    /// <summary>타이튼 조인트 기준 무게(kg). 현재 <see cref="Form"/>에 대응하는 값이다.</summary>
    public double WeightTytonKg { get => _weightTytonKg; set => SetProperty(ref _weightTytonKg, value); }
    // 열 제거(2026-08-21) — 배치 계산은 BendResolution.LongLegLengthMm을 쓴다.
    public double LongLegLengthMm => Math.Round(LayingLengthMm + ExtraLegLengthMm, 1);
    public double TangentLengthMm => Math.Round(BendResolver.TangentLength(CenterlineRadiusMm, AngleDeg), 1);
    /// <summary>형식(A/B) 전환 시 그 형식의 핸드북 무게로 다시 채운다. 이후 사용자가 손으로 고치면 그 값을 유지한다.</summary>
    // 형식 열 제거(2026-08-21)로 현재 호출부 없음 — 무게 재계산이 필요해지면 여기서 이어간다.
    public void RefreshWeightDefaults()
    {
        if (Connection == BendConnection.Socket && BendFittingCatalog.TryGetHandbookWeight(DiameterMm, AngleDeg, Form, out var kp, out var tyton))
        {
            WeightKpMechanicalKg = kp; WeightTytonKg = tyton;
        }
    }
}
