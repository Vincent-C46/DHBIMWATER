using DHBIMWATER.Core.Piping;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.DTOs.Piping;

public sealed record PipeJointSlotValue(string Kind, string Spec, int Count);
public sealed record PipeMemberCountInput(string? PartName, string? Composition, string? MaterialCode,
    bool IsValve, bool IsFlanged, bool UseRestraintGland = false);
public sealed record PipeMemberCounts(int? ValveJointLayingCount, int? LayingCount, int? CutCount,
    IReadOnlyList<PipeJointSlotValue> SocketSlots, bool MaterialAssumedSteel);
public sealed record PlannedSegmentCounts(string PartName, int LayingCount, int CutCount);
public sealed record PlannedMemberCounts(int LayingCount, int CutCount,
    IReadOnlyList<PipeJointSlotValue> SocketSlots, bool MaterialUnknown);
public sealed record PipeJointMemberInput(long Id, bool IsValve, string? PartName, string? Composition,
    double DiameterMm, bool FlangedWhenNoComposition = false, string? MaterialCode = null);
public sealed record PipeJointEndInput(long MemberId, int Index, double XMm, double YMm, double ZMm,
    double DiameterMm, long? PartnerMemberId = null, int? PartnerIndex = null);
public sealed record PipeJointPassResult(
    IReadOnlyDictionary<long, IReadOnlyList<PipeJointSlotValue>> FlangeSlots,
    int UnpairedFlangeCount, int UnresolvedEndCount,
    int AmbiguousEndCount, IReadOnlyList<string> Warnings);
public readonly record struct PipeJointEnds(int Socket, int Flange)
{
    public int Total => Socket + Flange;
    public bool IsEmpty => Socket <= 0 && Flange <= 0;
}
public sealed record FittingWeight(double? WeightKg, double? UnitWeightKgPerM = null)
{
    public static FittingWeight None { get; } = new((double?)null);
}
public enum FittingWeightPart { Other, Straight, Bend, Tee, Cross, Wye, ValveTee, ValveBend }
public sealed record FittingWeightInput(FittingWeightPart Part, PipeMaterial? Material,
    BendConnection Connection, SteelFittingClass SteelClass, string? PipeKind, double DiameterMm,
    double? BranchDiameterMm = null, double? LengthMm = null, double? DeflectionDeg = null,
    double? WyeAngleDeg = null, int FlangeCount = 0);
