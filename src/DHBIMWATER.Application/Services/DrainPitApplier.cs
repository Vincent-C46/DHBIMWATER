using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Services;

/// <summary>DirectShape 변환 전 솔리드 정의에 드레인 피트 Void를 주입한다. Revit 참조 없음.</summary>
public static class DrainPitApplier
{
    public static void Apply(IReadOnlyList<SolidExtrusionDefinition> solids, IReadOnlyList<DrainPitDefinition> pits)
    {
        foreach (var pit in pits)
        {
            var voidDef = DrainPitGeometry.ToVoidExtrusion(pit);
            foreach (var solid in solids)
            {
                if (!Overlaps(solid, pit)) continue;
                solid.Voids = solid.Voids.Concat(new[] { voidDef }).ToList();
            }
        }
    }

    private static bool Overlaps(SolidExtrusionDefinition solid, DrainPitDefinition pit)
    {
        if (solid.Profile.Count == 0) return false;
        var minX = solid.Profile.Min(p => p.X); var maxX = solid.Profile.Max(p => p.X);
        var minY = solid.Profile.Min(p => p.Y); var maxY = solid.Profile.Max(p => p.Y);
        var minZ = solid.Profile.Min(p => p.Z); var maxZ = solid.Profile.Max(p => p.Z);
        var solidBottom = Math.Min(minZ, maxZ);
        var solidTop = Math.Max(minZ, maxZ) + solid.Distance;
        var pitBottom = pit.TopCenter.Z - pit.Depth;
        return pit.TopCenter.X >= minX && pit.TopCenter.X <= maxX && pit.TopCenter.Y >= minY && pit.TopCenter.Y <= maxY &&
               pitBottom <= solidTop && pit.TopCenter.Z >= solidBottom;
    }
}
