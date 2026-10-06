using DHBoost.Revit.Interop;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Revit.Interop;

public static class PipeCatalogDataMapper
{
    private static T Parse<T>(Enum value) where T : struct, Enum => Enum.Parse<T>(value.ToString());

    public static (StraightPipeSpecTable Straight, BendFittingCatalog Fittings, JointDeflectionTable Joints)
        ProjectTables(PipeProjectTablesDto tables) => (
            new StraightPipeSpecTable(tables.StraightPipes.Select(x => new StraightPipeSpec(x.PipeKind,
                x.DiameterMm, x.OuterDiameterMm, x.ThicknessMm, x.UnitWeightKgPerM,
                x.SocketWeightKpKg, x.SocketWeightTytonKg)).ToArray()),
            new BendFittingCatalog(tables.Fittings.Select(x => new BendFittingEntry(x.DiameterMm,
                x.AngleDeg, Parse<BendForm>(x.Form), x.LayingLengthMm, x.CenterlineRadiusMm,
                x.WallThicknessMm, x.WeightKpMechanicalKg, x.WeightTytonKg,
                Parse<PipeMaterial>(x.Material), Parse<BendConnection>(x.Connection),
                Parse<SteelFittingClass>(x.FittingClass), x.WeightKg)).ToArray()),
            new JointDeflectionTable(tables.JointDeflections.Select(x =>
                new JointDeflectionSpec(x.JointType, x.DiameterMm, x.AllowableDeg)).ToArray()));

    public static PipeCatalogData Map(PipeCatalogDataDto source)
    {
        var (straight, fittings, joints) = ProjectTables(source.ProjectTables);
        var handbook = ProjectTables(new PipeProjectTablesDto { Fittings = source.BendHandbookEntries }).Fittings.Entries;
        return new PipeCatalogData
        {
            StraightPipes = straight,
            Fittings = fittings,
            JointDeflections = joints,
            SteelStraightDefaults = straight.Entries.Where(x => source.SteelGrades.Contains(x.PipeKind, StringComparer.Ordinal)).ToArray(),
            SteelBendDefaults = handbook.Where(x => x.Material == PipeMaterial.Steel).ToArray(),
            SteelTees = source.SteelTees.Select(x => new SteelTeeSpec(Parse<SteelFittingClass>(x.Class),
                x.MainDn, x.BranchDn, x.MainThicknessMm, x.BranchThicknessMm, x.H, x.I, x.Weight)).ToArray(),
            SteelReducers = source.SteelReducers.Select(x => new SteelReducerSpec(x.LargeDn, x.SmallDn,
                x.C, x.W, x.L, x.WeightF12, x.WeightF15, x.WeightF20)).ToArray(),
            DuctileSocketTees = source.DuctileSocketTees.Select(x => new DuctileSocketTeeSpec(x.MainDn,
                x.BranchDn, x.L, x.H, x.KpA, x.KpB, x.TytonA, x.TytonB)).ToArray(),
            DuctileFlangedTees = source.DuctileFlangedTees.Select(x => new DuctileFlangedTeeSpec(x.MainDn,
                x.BranchDn, x.L, x.H, x.WeightA, x.WeightB)).ToArray(),
            DuctileSocketTapers = source.DuctileSocketTapers.Select(x => new DuctileSocketTaperSpec(x.LargeDn,
                x.SmallDn, x.L, x.KpA, x.KpB, x.KpC, x.TytonB)).ToArray(),
            DuctileFlangedTapers = source.DuctileFlangedTapers.Select(x => new DuctileFlangedTaperSpec(
                x.LargeDn, x.SmallDn, x.L, x.Weight)).ToArray(),
            DuctileWyes = source.DuctileWyes.Select(x => new DuctileWyeSpec(x.MainDn, x.BranchDn,
                x.L, x.KpA, x.KpB, x.FlangeC)).ToArray(),
            DuctileValveTees = source.DuctileValveTees.Select(x => new DuctileValveTeeSpec(x.MainDn,
                x.BranchDn, x.E1, x.E2, x.L, x.S, x.H, x.WeightA, x.WeightBKp, x.WeightCFlange)).ToArray(),
            DuctileValveBends = source.DuctileValveBends.Select(x => new DuctileValveBendSpec(x.MainDn,
                x.BranchDn, x.StdE, x.StdR, x.StdL, x.StdH, x.StdWeight, x.ShortE, x.ShortR,
                x.ShortL, x.ShortH, x.ShortWeight)).ToArray(),
            SteelValveTees = source.SteelValveTees.Select(x => new SteelValveTeeSpec(
                Parse<SteelFittingClass>(x.Class), x.MainDn, x.BranchDn, x.D2, x.d2, x.T, x.t,
                x.ReinforceT1, x.ReinforceB, x.B, x.I, x.J, x.Weight)).ToArray(),
            SteelValveBends = source.SteelValveBends.Select(x => new SteelValveBendSpec(
                Parse<SteelFittingClass>(x.Class), x.MainDn, x.BranchDn, x.d2, x.T1, x.T2,
                x.A, x.F, x.R, x.Weight)).ToArray(),
            DuctileIronGrades = source.DuctileIronGrades,
            SteelGrades = source.SteelGrades,
            AllGrades = source.AllGrades,
            DuctileIronNominalDiameters = source.DuctileIronNominalDiameters,
            SteelNominalDiameters = source.SteelNominalDiameters,
            NominalDiameters = source.NominalDiameters,
            JointTypes = source.JointTypes,
            StandardAngles = source.StandardAngles.ToDictionary(x =>
                (Parse<PipeMaterial>(x.Material), Parse<BendConnection>(x.Connection)),
                x => (IReadOnlyList<double>)x.Angles),
            BendHandbookEntries = handbook,
            SocketBendHandbook = source.SocketBendHandbook.Select(x => new SocketBendHandbookRow(
                x.DiameterMm, x.AngleDeg, Parse<BendForm>(x.Form), x.KpWeightKg, x.TytonWeightKg)).ToArray(),
            FlangedBendHandbook = source.FlangedBendHandbook.Select(x => new FlangedBendHandbookRow(
                x.DiameterMm, x.AngleDeg, Parse<BendForm>(x.Form), x.WeightKg)).ToArray()
        };
    }

    public static PipeProjectTablesDto ProjectTables(BendSettings settings) => new()
    {
        StraightPipes = settings.StraightPipes.Entries.Select(x => new StraightPipeSpecDto(x.PipeKind,
            x.DiameterMm, x.OuterDiameterMm, x.ThicknessMm, x.UnitWeightKgPerM,
            x.SocketWeightKpKg, x.SocketWeightTytonKg)).ToArray(),
        Fittings = settings.Fittings.Entries.Select(x => new BendFittingEntryDto(x.DiameterMm,
            x.AngleDeg, Parse<BendFormCode>(x.Form), x.LayingLengthMm, x.CenterlineRadiusMm,
            x.WallThicknessMm, x.WeightKpMechanicalKg, x.WeightTytonKg,
            Parse<PipeMaterialCode>(x.Material), Parse<JointConnectionCode>(x.Connection),
            Parse<SteelClassCode>(x.FittingClass), x.WeightKg)).ToArray(),
        JointDeflections = settings.JointDeflections.Entries.Select(x => new JointDeflectionSpecDto(
            x.JointType, x.DiameterMm, x.AllowableDeg)).ToArray()
    };
}
