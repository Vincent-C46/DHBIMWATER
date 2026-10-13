using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

internal static class JunctionSymbolParameterLookup
{
    // 정확 일치 먼저, 없으면 공백 무시 첫 일치. 인스턴스 매개변수만 (docs/286)
    internal static Parameter? Find(Element instance, string name)
    {
        var exact = instance.LookupParameter(name);
        if (exact is not null) return exact;
        foreach (Parameter parameter in instance.Parameters)
            if (JunctionDetailParameters.NameMatches(parameter.Definition?.Name, name)) return parameter;
        return null;
    }
}
