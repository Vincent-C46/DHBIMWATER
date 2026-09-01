using Autodesk.Revit.Attributes;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class PlacementStationCommand : EmbeddedDhBoostCommand
{
    protected override string CommandTypeName => "DHBoost.Revit.Commands.PlacementStationEditCommandForWater";
}
