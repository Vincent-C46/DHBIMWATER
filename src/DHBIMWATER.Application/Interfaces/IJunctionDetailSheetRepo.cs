namespace DHBIMWATER.Application.Interfaces;

// 격점 1개 시트 요청. 뷰 이름 = 평면·드래프팅 UseCase 규약, 3D는 시트 Repo가 만듦
public sealed record JunctionDetailSheetRequest(JunctionDetailGroup Group, string SheetName, string SheetNumber,
    string PlanViewName, string DraftingViewName, string ThreeDViewName);

public enum JunctionDetailSheetStatus { Created, Updated, Unchanged }

public sealed record JunctionDetailSheetChange(JunctionDetailSheetStatus Status, IReadOnlyList<string> Warnings);

public interface IJunctionDetailSheetRepo
{
    // 같은 이름 시트에 대상 뷰 다 있으면 손대지 않음(Unchanged). 빠진 뷰만 추가(Updated)
    JunctionDetailSheetChange CreateOrUpdateSheet(JunctionDetailSheetRequest request);
}
