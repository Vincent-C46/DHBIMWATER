using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialDraftingRepo : IPipeMaterialDraftingRepo
{
    public IReadOnlyList<PipeMaterialPart> GetParts() => [];
    public IReadOnlySet<string> GetExistingViewNames() => new HashSet<string>();
    public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) { }
    // GetExistingViewNames가 항상 비어 있어 호출될 일이 없다. 인터페이스를 채우기 위한 것이다(docs/173 § E-1).
    public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) => false;
}
