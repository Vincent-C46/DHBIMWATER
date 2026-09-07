using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialDraftingRepo : IPipeMaterialDraftingRepo
{
    public IReadOnlyList<PipeMaterialPart> GetParts() => [];
    public bool ViewExists(string viewName) => false;
    public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows) { }
}
