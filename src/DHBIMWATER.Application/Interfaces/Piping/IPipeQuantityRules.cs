using DHBIMWATER.Core.Piping;
using DHBIMWATER.Core.Gis;
using A = DHBIMWATER.Application.DTOs.Piping;

namespace DHBIMWATER.Application.Interfaces.Piping;

public interface IPipeQuantityRules
{
    bool TryEnsureAvailable(out string warning);
    A.PipeMemberCounts CountValveRoomMember(A.PipeMemberCountInput input);
    A.PlannedSegmentCounts CountPlannedSegment(double lengthMm);
    A.PlannedMemberCounts CountPlannedMember(string partName, string? composition,
        string? materialCode, bool useRestraintGland = false);
    string? AdaptivePartFor(int placementPointCount, double lengthMm);
    string DefaultPlannedComposition(string partName);
    int PlannedBendLayingCount { get; }
    int PlannedBendCutCount { get; }
    int PlannedReducerLayingCount { get; }
    int PlannedReducerCutCount { get; }
    A.PipeJointPassResult RunConnectionPass(IReadOnlyList<A.PipeJointMemberInput> members,
        IReadOnlyList<A.PipeJointEndInput> ends, bool isValveRoom, double toleranceMm = 1.0);
    string FlangeJointKind { get; }
    string? JointKindFor(PipeMaterial? material, BendConnection connection, bool useRestraintGland);
    A.PipeJointEnds DecodeComposition(string? value);
    string EncodeComposition(int socket, int flange);
    BendConnection? ParseConnection(string? value);
    BendConnection ResolveConnection(string? familyParameterValue, string? partName, BendConnection fallback);
    bool HasSocketMark(string? partName);
    IReadOnlyList<string> ConnectionParameterNames { get; }
    A.FittingWeight ResolveFittingWeight(IPipeCatalog catalog, A.FittingWeightInput input);
    string? ResolveGrade(PipeMaterial material, string? pipeKind, double diameterMm);
    A.FittingWeightPart PartOf(string? strippedName);
    /// <summary>직관 무게. 강관은 플랜지 단부 수 × 플랜지 개당 무게(<paramref name="steelClass"/> 등급)를 더함.</summary>
    double? StraightWeightKg(IPipeCatalog catalog, string pipeKind, double diameterMm, double lengthM, string? jointType,
        SteelFittingClass steelClass = SteelFittingClass.None, int flangeCount = 0);
    double? DuctileBendWeightKg(IPipeCatalog catalog, BendConnection connection, BendForm form,
        string? jointType, double diameterMm, double angleDeg);
    /// <summary>강관 곡관 무게 + 플랜지 단부 수 × 플랜지 개당 무게.</summary>
    double? SteelBendWeightKg(IPipeCatalog catalog, BendConnection connection,
        SteelFittingClass steelClass, double diameterMm, double angleDeg, int flangeCount = 0);
    double? ReducerWeightKg(IPipeCatalog catalog, PipeMaterial material, BendConnection connection,
        ReducerForm form, string? jointType, SteelFittingClass steelClass, double largeMm, double smallMm);
}
