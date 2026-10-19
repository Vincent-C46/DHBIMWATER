using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.DTOs.Gis;

/// <summary>엑셀 선형을 측점·좌표·관Z·지표EL 행으로 읽은 결과(docs/300 § B-2). 읽지 못한 행은 빠지고 경고에 남는다.</summary>
public sealed record AlignmentProfileReadResult(IReadOnlyList<AlignmentProfileRow> Rows, IReadOnlyList<string> Warnings);
