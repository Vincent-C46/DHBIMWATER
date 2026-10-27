using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Helpers;

/// <summary>
/// 강관 가변 패밀리(`2P_강관직관`·`5P_강관곡관`)에 플랜지·볼트 치수를 인스턴스로 씀.
/// 출처 <see cref="SteelFlangeSpecTable"/>(플랜지), <see cref="SteelFlangeBoltSpecTable"/>(볼트 d, docs/317).
/// 색인일람표 대신 코드 기입 — 표 밖 DN도 경고만 남기고 배치 계속(사용자 결정 2026-10-26).
/// <para>`DN`·`OD`·`thk`는 관로 모델링 창 매개변수가 씀 → 여기서 안 씀.
/// 강관만. 주철관은 패밀리 색인일람표(PN10) 유지 → 건너뜀.
/// 매개변수 없음·읽기전용은 조용히 건너뜀 — 패밀리마다 가진 칸이 다름.</para>
/// </summary>
internal sealed class SteelFlangeParameterWriter
{
    public const string FlangeRadius = "플랜지_반지름";
    public const string GasketRadius = "가스켓_반지름";
    public const string FlangeThickness = "플랜지_두께";
    public const string GasketThickness = "가스켓_두께";
    public const string BoltCircleDiameter = "볼트중심선_D";
    public const string BoltDiameter = "볼트지름_h";

    private readonly HashSet<(SteelFittingClass Class, double Dn)> _missingSpecs = new();
    private readonly HashSet<(SteelFittingClass Class, double Dn)> _missingBoltSpecs = new();

    public void Write(FamilyInstance instance, PipeMaterial? material, SteelFittingClass steelClass, double dnMm)
    {
        if (material != PipeMaterial.Steel) return;
        // 볼트 표는 플랜지 표와 따로 조회 — 플랜지 누락이 볼트 기입 막지 않게 먼저.
        if (SteelFlangeBoltSpecTable.Find(steelClass, dnMm) is { } bolt)
            Length(instance, BoltDiameter, bolt.BoltDiameterMm);
        else
            _missingBoltSpecs.Add((ValveBypassSpecTable.NormalizeSteelClass(steelClass), dnMm));
        if (SteelFlangeSpecTable.Find(steelClass, dnMm) is not { } spec)
        {
            _missingSpecs.Add((ValveBypassSpecTable.NormalizeSteelClass(steelClass), dnMm));
            return;
        }
        Length(instance, FlangeRadius, spec.FlangeDiameterMm / 2d);
        Length(instance, GasketRadius, spec.GasketDiameterMm / 2d);
        Length(instance, FlangeThickness, spec.FlangeThicknessMm);
        Length(instance, GasketThickness, spec.GasketThicknessMm);
        Length(instance, BoltCircleDiameter, spec.BoltCircleDiameterMm);
    }

    /// <summary>표에 없는 (등급, DN) 안내. 플랜지·볼트 누락 문장을 줄바꿈으로 묶은 문자열 하나. 없으면 null.</summary>
    public string? Warning(string partLabel)
    {
        var lines = new List<string>(2);
        if (_missingSpecs.Count > 0)
            lines.Add($"강관 {partLabel} 플랜지 규격표에 없는 등급/DN {_missingSpecs.Count}건은 플랜지 치수를 패밀리 기본값으로 두었습니다: "
                + Join(_missingSpecs));
        if (_missingBoltSpecs.Count > 0)
            lines.Add($"강관 {partLabel} 볼트 규격표에 없는 등급/DN {_missingBoltSpecs.Count}건은 {BoltDiameter}를 패밀리 기본값으로 두었습니다: "
                + Join(_missingBoltSpecs));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string Join(IEnumerable<(SteelFittingClass Class, double Dn)> missing)
        => string.Join(", ", missing.OrderBy(x => x.Class).ThenBy(x => x.Dn).Select(x => $"{x.Class}/DN{x.Dn:0.##}"));

    private static void Length(FamilyInstance instance, string name, double mm)
    {
        if (instance.LookupParameter(name) is { IsReadOnly: false, StorageType: StorageType.Double } parameter
            && parameter.Definition.GetDataType().Equals(SpecTypeId.Length))
            parameter.Set(UC.MmToFt(mm));
    }
}
