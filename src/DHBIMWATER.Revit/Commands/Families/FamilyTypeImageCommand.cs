using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DidasUsage;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.Views.Utilities;

namespace DHBIMWATER.Revit.Commands.Families;

/// <summary>
/// 패밀리 유형 이미지 일괄 지정(docs/309). 진입점 = 애드인 탭 → 외부 도구(DHBIMWATER.addin의 Command 항목, § 6 Q4).
/// <para>모달 창 → 창이 닫힐 때까지 명령 실행 중 = API 컨텍스트. 창 안에서 바로 패밀리 파일을 열고 저장한다.</para>
/// </summary>
[Transaction(TransactionMode.Manual)]
public class FamilyTypeImageCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 리본 버튼은 RibbonBuilder가 사용자 확인 실패 시 비활성화한다. 외부 도구 항목은 그 경로를 안 거치므로 여기서 같은 확인.
        if (!DidasUsageTracker.TryGetUserInfo(out _, out _))
        {
            TaskDialog.Show("유형 이미지 일괄 지정", "사용자 확인에 실패해 실행할 수 없습니다.");
            return Result.Cancelled;
        }

        var view = ServiceContainer.GetService<FamilyTypeImageView>();
        view.ShowDialog();
        return Result.Succeeded;
    }
}

/// <summary>문서가 하나도 안 열린 상태에서도 실행 가능 — 대상은 활성 문서가 아니라 폴더의 .rfa.</summary>
public class FamilyTypeImageCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
