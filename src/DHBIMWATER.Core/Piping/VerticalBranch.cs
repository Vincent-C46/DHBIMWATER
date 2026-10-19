namespace DHBIMWATER.Core.Piping;

/// <summary>수직분기 부품 1개(docs/301). 시작 커넥터에서 +Z로 이 순서대로 쌓는다.</summary>
/// <param name="FamilyTypeName">"패밀리 : 유형". 비면 단관(PipeLayout 관 패밀리).</param>
/// <param name="LengthMm">단관 길이(mm). 밸브는 무시 — 폭은 배치 때 커넥터로 잰다.</param>
public sealed record VerticalBranchPart(string FamilyTypeName, double LengthMm = 0)
{
    public bool IsPipe => string.IsNullOrWhiteSpace(FamilyTypeName);
}

/// <summary>피킹한 시작 부속(docs/301 § A-2). 좌표는 Revit 프로젝트 mm.</summary>
/// <param name="DiameterMm">+Z 커넥터 지름. 쌓는 부품 관경이 이 값을 따른다.</param>
/// <param name="ElevationMm">+Z 커넥터 원점 Z(내부 원점 기준 mm).</param>
public sealed record VerticalBranchStart(long ElementId, string FamilyTypeName, double DiameterMm, double ElevationMm);

/// <summary>배치 요청. 패밀리 지정은 모델 생성과 같은 묶음을 쓴다(docs/301 § D).</summary>
public sealed record VerticalBranchRequest(long StartElementId, IReadOnlyList<VerticalBranchPart> Parts,
    PipeSegmentFamilySelection Families);

/// <summary>배치 결과. 끝 EL은 마지막 부품 위 커넥터(없으면 인스턴스 위치) Z.</summary>
public sealed record VerticalBranchResult(int PlacedCount, double TopElevationMm, IReadOnlyList<long> ElementIds,
    IReadOnlyList<string> Warnings);

/// <summary>Revit 없이 거를 수 있는 입력 규칙(docs/301 § C). 커넥터 수(1포트 맨 끝)는 배치 때 본다.</summary>
public static class VerticalBranchRules
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<VerticalBranchPart> parts)
    {
        var errors = new List<string>();
        if (parts.Count == 0) errors.Add("부품이 없습니다 — 하나 이상 추가하세요.");
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (!part.IsPipe) continue;
            // 관 최소 조각과 같은 문턱. 직결은 행 삭제로 표현한다.
            if (!double.IsFinite(part.LengthMm) || part.LengthMm < PipeSegmentPlan.MinSegmentMm)
                errors.Add(FormattableString.Invariant(
                    $"#{i + 1} 단관 {part.LengthMm:0.#}mm — 관 최소 {PipeSegmentPlan.MinSegmentMm:0}mm입니다. 직결이면 행을 삭제하세요."));
        }
        return errors;
    }

    /// <summary>단관 길이 합(mm). 밸브 폭은 모르므로 빠진다 — 미리보기 끝 EL 하한.</summary>
    public static double PipeLengthSum(IReadOnlyList<VerticalBranchPart> parts)
        => parts.Where(x => x.IsPipe && double.IsFinite(x.LengthMm)).Sum(x => x.LengthMm);
}
