using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.DTOs.Gis;

public sealed record ReducerDirectShapePlacementPlan(
    ReducerPlacement Placement, string SourceFile, string RecordNumber,
    double LargeOuterDiameterMm, double SmallOuterDiameterMm,
    double LargeWallThicknessMm, double SmallWallThicknessMm,
    /// <summary>상류 DN 기준 Global Z 오프셋(m).</summary>
    double UpstreamZOffsetM,
    /// <summary>하류 DN 기준 Global Z 오프셋(m).</summary>
    double DownstreamZOffsetM,
    ReducerForm Form = ReducerForm.BType)
{
    public ReducerEndProfiles EndProfiles()
    {
        var upstreamIsLarge = Placement.UpstreamDiameterMm >= Placement.DownstreamDiameterMm;
        var upstream = upstreamIsLarge
            ? new ReducerEndProfile(LargeOuterDiameterMm, LargeWallThicknessMm)
            : new ReducerEndProfile(SmallOuterDiameterMm, SmallWallThicknessMm);
        var downstream = upstreamIsLarge
            ? new ReducerEndProfile(SmallOuterDiameterMm, SmallWallThicknessMm)
            : new ReducerEndProfile(LargeOuterDiameterMm, LargeWallThicknessMm);
        return new ReducerEndProfiles(upstream, downstream);
    }
}

public sealed record ReducerEndProfile(double OuterDiameterMm, double WallThicknessMm);
public sealed record ReducerEndProfiles(ReducerEndProfile Upstream, ReducerEndProfile Downstream);

public sealed record ReducerPlacementResult(int Count, IReadOnlyList<string> Warnings);
