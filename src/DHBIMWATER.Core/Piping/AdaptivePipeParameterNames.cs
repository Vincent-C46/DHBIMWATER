namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 직접 배치한 계획관로 가변 패밀리에서 값을 읽을 패밀리 매개변수 이름 목록(docs/267 § 2-2). 앞 이름부터 찾는다.
/// <para>패밀리마다 이름이 달라 통일할 수 없다(사용자, 2026-10-02) — 그래서 설정 파일(JSON)로 둔다. UI는 아직 없다.</para>
/// </summary>
public sealed record AdaptivePipeParameterNames(
    IReadOnlyList<string> Length,
    IReadOnlyList<string> Material,
    IReadOnlyList<string> NominalDiameter,
    IReadOnlyList<string> BendAngle)
{
    public static AdaptivePipeParameterNames Default { get; } = new(
        ["직관_길이", "길이"],
        ["관종"],
        ["호칭지름", "공칭 지름"],
        ["각도"]);
}
