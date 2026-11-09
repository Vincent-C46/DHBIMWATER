using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

// 격점 1개 시트 요청. 뷰 이름 = 평면·드래프팅 UseCase 규약, 3D는 시트 Repo가 만듦.
// TitleBlockId 0 = A1 자동 선택. ViewScale = 3D 축척(평면은 평면 UseCase가 같은 값 씀)
// ThreeDTopViewName = 3D를 위에서 내려다본 뷰(좌하단). 시트 Repo가 만듦. 비면 안 만듦
public sealed record JunctionDetailSheetRequest(JunctionDetailGroup Group, string SheetName, string SheetNumber,
    string PlanViewName, string DraftingViewName, string ThreeDViewName, long TitleBlockId = 0,
    int ViewScale = JunctionDetailParameters.SheetScale, string ThreeDTopViewName = "");

public enum JunctionDetailSheetStatus { Created, Updated, Unchanged }

public sealed record JunctionDetailSheetChange(JunctionDetailSheetStatus Status, IReadOnlyList<string> Warnings);

/// <summary>도곽(제목 블록) 유형 1개. Name = "패밀리 : 유형".</summary>
public sealed record JunctionDetailTitleBlock(long Id, string Name);

public interface IJunctionDetailSheetRepo
{
    // 같은 이름 시트에 대상 뷰 다 있고 축척 같으면 손대지 않음(Unchanged). 빠진 뷰 추가·축척 변경 = Updated
    JunctionDetailSheetChange CreateOrUpdateSheet(JunctionDetailSheetRequest request);

    // 문서에 로드된 제목 블록 유형. 이름순
    IReadOnlyList<JunctionDetailTitleBlock> GetTitleBlocks() => [];
}
