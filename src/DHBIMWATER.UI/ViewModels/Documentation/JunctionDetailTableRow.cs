namespace DHBIMWATER.UI.ViewModels.Documentation;

public sealed record JunctionDetailTableRow(int ItemNumber, string Name, string Spec, int Count,
    string Remark, string? TypeImagePath);
