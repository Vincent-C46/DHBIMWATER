using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Helpers;

/// <summary>DH_*와 DHBoost 배치 태그 기록. 기존 이름 정의 재사용.</summary>
internal sealed class PipeParameterWriter
{
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidTypes = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> Missing => _missing;
    public IReadOnlyCollection<string> InvalidTypes => _invalidTypes;

    public void Text(Element element, string name, string? value) => Write(element, name, p => p.Set(value ?? string.Empty), StorageType.String, SpecTypeId.String.Text);
    public void LengthMm(Element element, string name, double? millimeters)
    {
        if (millimeters is not null) Write(element, name, p => p.Set(UC.MmToFt(millimeters.Value)), StorageType.Double, SpecTypeId.Length);
    }
    public void LengthM(Element element, string name, double meters) => Write(element, name, p => p.Set(UC.MToFt(meters)), StorageType.Double, SpecTypeId.Length);
    public void AngleDeg(Element element, string name, double degrees) => Write(element, name, p => p.Set(UC.DegToRad(degrees)), StorageType.Double, SpecTypeId.Angle);
    public void Number(Element element, string name, double value) => Write(element, name, p => p.Set(value), StorageType.Double, SpecTypeId.Number);
    public void Integer(Element element, string name, int value) => Write(element, name, p => p.Set(value), StorageType.Integer, SpecTypeId.Int.Integer);
    public void YesNo(Element element, string name, bool value) => Write(element, name, p => p.Set(value ? 1 : 0), StorageType.Integer, SpecTypeId.Boolean.YesNo);

    private void Write(Element element, string name, Action<Parameter> set, StorageType expectedStorage, ForgeTypeId expectedDataType)
    {
        var parameter = FindParameter(element, name);
        if (parameter is not { IsReadOnly: false }
            || parameter.StorageType != expectedStorage
            || !parameter.Definition.GetDataType().Equals(expectedDataType))
        {
            _missing.Add(name);
            if (parameter is not null
                && (parameter.StorageType != expectedStorage || !parameter.Definition.GetDataType().Equals(expectedDataType)))
                _invalidTypes.Add(name);
            return;
        }
        set(parameter);
    }

    private static Parameter? FindParameter(Element element, string name)
    {
        if (PipeAlignmentParameters.Guids.TryGetValue(name, out var pipeGuid)) return element.get_Parameter(pipeGuid);
        if (!PlacementStationParameters.Guids.TryGetValue(name, out var placementGuid)) return null;
        return element.get_Parameter(placementGuid) ?? element.LookupParameter(name);
    }
}
