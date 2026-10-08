using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>배관재료표를 드래프팅뷰에 직접 그리는 경계(docs/133).
/// <para>일람표(<see cref="IPipeMaterialScheduleRepo"/>)와 나눠 둔 이유: 일람표는 Revit이 표를 그려 주고
/// 드래프팅뷰는 우리가 선·문자를 그린다. 실패 양상도 다르므로 한 인터페이스에 섞지 않는다.</para></summary>
public interface IPipeMaterialDraftingRepo
{
    /// <summary>배치된 배관 밸브류 부재에서 재료표 원자료를 읽는다. 읽기 전용이라 트랜잭션이 필요 없다.</summary>
    IReadOnlyList<PipeMaterialPart> GetParts();

    /// <summary>템플릿이 아닌 모든 뷰 이름(일람표 포함). Revit 뷰 이름은 전체에서 유일해야 하므로
    /// 격점마다 전체를 훑는 대신 한 번 받아 둔다(docs/208 § D-3).</summary>
    IReadOnlySet<string> GetExistingViewNames();

    /// <summary>드래프팅뷰 1개를 만들고 그 안에 6열 배관재료표를 선·문자로 그린다.</summary>
    void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true);

    /// <summary>이미 있는 드래프팅뷰의 내용을 <b>전부 지우고</b> 표를 다시 그린다(docs/173).
    /// <para>사용자가 그 뷰에 덧붙인 치수·주석·상세항목도 함께 사라진다 —
    /// 호출 전에 사용자 확인을 받는 것은 호출자 책임이다(<c>JunctionDetailCommand</c>).</para>
    /// <returns><paramref name="viewName"/>을 쓰는 드래프팅뷰를 찾아 다시 그렸으면 true.
    /// 그 이름을 다른 종류의 뷰(일람표·평면도 등)가 쓰고 있으면 <b>아무것도 지우지 않고</b> false.</returns></summary>
    bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true);
}
