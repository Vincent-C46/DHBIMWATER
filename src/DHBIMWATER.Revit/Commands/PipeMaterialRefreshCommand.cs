using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.Views.Documentation;

namespace DHBIMWATER.Revit.Commands;

/// <summary>부재 정보 갱신. 수동 배치 배관 부재·가변 패밀리에 수량산출용 매개변수 채움.
/// <para>모달 창 → 창 닫힐 때까지 명령 실행 중 = API 컨텍스트. 실행 시점 Revit 선택을 범위로 씀.</para></summary>
[Transaction(TransactionMode.Manual)]
public class PipeMaterialRefreshCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var view = ServiceContainer.GetService<PipeMaterialRefreshView>();
        view.ShowDialog();
        return Result.Succeeded;
    }
}
