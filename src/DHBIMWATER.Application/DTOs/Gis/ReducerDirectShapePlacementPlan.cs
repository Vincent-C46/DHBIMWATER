using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.DTOs.Gis;

public sealed record ReducerDirectShapePlacementPlan(
    ReducerPlacement Placement, string SourceFile, string RecordNumber,
    double LargeOuterDiameterMm, double SmallOuterDiameterMm,
    double LargeWallThicknessMm, double SmallWallThicknessMm, double ZOffsetM);

public sealed record ReducerPlacementResult(int Count, IReadOnlyList<string> Warnings);
