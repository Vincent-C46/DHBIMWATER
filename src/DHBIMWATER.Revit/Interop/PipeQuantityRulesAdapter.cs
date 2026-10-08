using DHBoost.Revit.Interop;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Core.Gis;
using RuleDtos = DHBIMWATER.Application.DTOs.Piping;

namespace DHBIMWATER.Revit.Interop;

internal sealed class PipeQuantityRulesAdapter : IPipeQuantityRules
{
    private readonly IPipeCatalogSource _catalog;

    public PipeQuantityRulesAdapter(IPipeCatalogSource catalog) => _catalog = catalog;

    private static T Parse<T>(Enum value) where T : struct, Enum => Enum.Parse<T>(value.ToString());
    private static PipeCatalogHandle Handle(IPipeCatalog catalog)
        => catalog is PipeCatalogAdapter.CatalogHandle handle ? handle.Handle
            : throw new ArgumentException("규격표 핸들이 올바르지 않습니다.", nameof(catalog));
    private static RuleDtos.PipeJointSlotValue Slot(PipeJointSlotDto slot) => new(slot.Kind, slot.Spec, slot.Count);

    public bool TryEnsureAvailable(out string warning) => _catalog.TryEnsureAvailable(out warning);

    public RuleDtos.PipeMemberCounts CountValveRoomMember(RuleDtos.PipeMemberCountInput input)
    {
        var result = PipeQuantityRulesInterop.CountValveRoomMember(new PipeMemberCountInputDto(
            input.PartName, input.Composition, input.MaterialCode, input.IsValve, input.IsFlanged,
            input.UseRestraintGland));
        return new RuleDtos.PipeMemberCounts(result.ValveJointLayingCount, result.LayingCount,
            result.CutCount, result.SocketSlots.Select(Slot).ToArray(), result.MaterialAssumedSteel);
    }

    public RuleDtos.PlannedSegmentCounts CountPlannedSegment(double lengthMm)
    {
        var result = PipeQuantityRulesInterop.CountPlannedSegment(lengthMm);
        return new RuleDtos.PlannedSegmentCounts(result.PartName, result.LayingCount, result.CutCount);
    }

    public RuleDtos.PlannedMemberCounts CountPlannedMember(string partName, string? composition,
        string? materialCode, bool useRestraintGland = false)
    {
        var result = PipeQuantityRulesInterop.CountPlannedMember(partName, composition, materialCode,
            useRestraintGland);
        return new RuleDtos.PlannedMemberCounts(result.LayingCount, result.CutCount,
            result.SocketSlots.Select(Slot).ToArray(), result.MaterialUnknown);
    }

    public string? AdaptivePartFor(int placementPointCount, double lengthMm)
        => PipeQuantityRulesInterop.AdaptivePartFor(placementPointCount, lengthMm);
    public string DefaultPlannedComposition(string partName)
        => PipeQuantityRulesInterop.DefaultPlannedComposition(partName);
    public int PlannedBendLayingCount => PipeQuantityRulesInterop.PlannedBendLayingCount;
    public int PlannedBendCutCount => PipeQuantityRulesInterop.PlannedBendCutCount;
    public int PlannedReducerLayingCount => PipeQuantityRulesInterop.PlannedReducerLayingCount;
    public int PlannedReducerCutCount => PipeQuantityRulesInterop.PlannedReducerCutCount;

    public RuleDtos.PipeJointPassResult RunConnectionPass(IReadOnlyList<RuleDtos.PipeJointMemberInput> members,
        IReadOnlyList<RuleDtos.PipeJointEndInput> ends, bool isValveRoom, double toleranceMm = 1.0)
    {
        var result = PipeQuantityRulesInterop.RunConnectionPass(members.Select(x => new PipeJointMemberDto(
                x.Id, x.IsValve, x.PartName, x.Composition, x.DiameterMm, x.FlangedWhenNoComposition, x.MaterialCode)).ToArray(),
            ends.Select(x => new PipeJointEndDto(x.MemberId, x.Index, x.XMm, x.YMm, x.ZMm,
                x.DiameterMm, x.PartnerMemberId, x.PartnerIndex)).ToArray(), isValveRoom, toleranceMm);
        return new RuleDtos.PipeJointPassResult(
            result.Members.ToDictionary(x => x.MemberId,
                x => (IReadOnlyList<RuleDtos.PipeJointSlotValue>)x.FlangeSlots.Select(Slot).ToArray()),
            result.UnpairedFlangeCount, result.UnresolvedEndCount, result.AmbiguousEndCount,
            result.Warnings);
    }

    public string FlangeJointKind => PipeQuantityRulesInterop.FlangeJointKind;
    public string? JointKindFor(PipeMaterial? material, BendConnection connection, bool useRestraintGland)
        => PipeQuantityRulesInterop.JointKindFor(material is null ? null : Parse<PipeMaterialCode>(material.Value),
            Parse<JointConnectionCode>(connection), useRestraintGland);
    public RuleDtos.PipeJointEnds DecodeComposition(string? value)
    {
        var result = PipeQuantityRulesInterop.DecodeComposition(value);
        return new RuleDtos.PipeJointEnds(result.Socket, result.Flange);
    }
    public string EncodeComposition(int socket, int flange)
        => PipeQuantityRulesInterop.EncodeComposition(socket, flange);
    public BendConnection? ParseConnection(string? value)
        => PipeQuantityRulesInterop.ParseConnection(value) is { } result ? Parse<BendConnection>(result) : null;
    public BendConnection ResolveConnection(string? familyParameterValue, string? partName, BendConnection fallback)
        => Parse<BendConnection>(PipeQuantityRulesInterop.ResolveConnection(familyParameterValue,
            partName, Parse<JointConnectionCode>(fallback)));
    public bool HasSocketMark(string? partName) => PipeQuantityRulesInterop.HasSocketMark(partName);
    public IReadOnlyList<string> ConnectionParameterNames => PipeQuantityRulesInterop.ConnectionParameterNames;

    public RuleDtos.FittingWeight ResolveFittingWeight(IPipeCatalog catalog, RuleDtos.FittingWeightInput input)
    {
        var result = PipeQuantityRulesInterop.ResolveFittingWeight(Handle(catalog), new FittingWeightInputDto(
            Parse<WeightPartCode>(input.Part),
            input.Material is null ? null : Parse<PipeMaterialCode>(input.Material.Value),
            Parse<JointConnectionCode>(input.Connection), Parse<SteelClassCode>(input.SteelClass),
            input.PipeKind, input.DiameterMm, input.BranchDiameterMm, input.LengthMm,
            input.DeflectionDeg, input.WyeAngleDeg));
        return new RuleDtos.FittingWeight(result.WeightKg, result.UnitWeightKgPerM);
    }
    public string? ResolveGrade(PipeMaterial material, string? pipeKind, double diameterMm)
        => PipeQuantityRulesInterop.ResolveGrade(Parse<PipeMaterialCode>(material), pipeKind, diameterMm);
    public RuleDtos.FittingWeightPart PartOf(string? strippedName)
        => Parse<RuleDtos.FittingWeightPart>(PipeQuantityRulesInterop.PartOf(strippedName));
    public double? StraightWeightKg(IPipeCatalog catalog, string pipeKind,
        double diameterMm, double lengthM, string? jointType)
        => PipeQuantityRulesInterop.StraightWeightKg(Handle(catalog), pipeKind, diameterMm, lengthM, jointType);
    public double? DuctileBendWeightKg(IPipeCatalog catalog, BendConnection connection, BendForm form,
        string? jointType, double diameterMm, double angleDeg)
        => PipeQuantityRulesInterop.DuctileBendWeightKg(Handle(catalog), Parse<JointConnectionCode>(connection),
            Parse<BendFormCode>(form), jointType, diameterMm, angleDeg);
    public double? SteelBendWeightKg(IPipeCatalog catalog, BendConnection connection,
        SteelFittingClass steelClass, double diameterMm, double angleDeg)
        => PipeQuantityRulesInterop.SteelBendWeightKg(Handle(catalog), Parse<JointConnectionCode>(connection),
            Parse<SteelClassCode>(steelClass), diameterMm, angleDeg);
    public double? ReducerWeightKg(IPipeCatalog catalog, PipeMaterial material, BendConnection connection,
        ReducerForm form, string? jointType, SteelFittingClass steelClass, double largeMm, double smallMm)
        => PipeQuantityRulesInterop.ReducerWeightKg(Handle(catalog), Parse<PipeMaterialCode>(material),
            Parse<JointConnectionCode>(connection), Parse<ReducerFormCode>(form), jointType,
            Parse<SteelClassCode>(steelClass), largeMm, smallMm);
}

