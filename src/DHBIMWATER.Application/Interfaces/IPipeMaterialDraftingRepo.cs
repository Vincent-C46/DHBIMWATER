using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>배관재료표를 드래프팅뷰에 직접 그리는 경계(docs/133).
/// <para>일람표(<see cref="IPipeMaterialScheduleRepo"/>)와 나눠 둔 이유: 일람표는 Revit이 표를 그려 주고
/// 드래프팅뷰는 우리가 선·문자를 그린다. 실패 양상도 다르므로 한 인터페이스에 섞지 않는다.</para></summary>
public interface IPipeMaterialDraftingRepo
{
    /// <summary>배치된 배관 밸브류 부재에서 재료표 원자료를 읽는다. 읽기 전용이라 트랜잭션이 필요 없다.</summary>
    IReadOnlyList<PipeMaterialPart> GetParts();

    /// <summary>이름이 같은 뷰가 이미 있는지. Revit 뷰 이름은 일람표까지 포함해 전체에서 유일해야 한다.</summary>
    bool ViewExists(string viewName);

    /// <summary>드래프팅뷰 1개를 만들고 그 안에 6열 배관재료표를 선·문자로 그린다.</summary>
    void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows);
}
