using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    public class RevitSlabCommandRepo : ISlabCommandRepo
    {
        private readonly Func<Document?> _doc;

        public RevitSlabCommandRepo(Func<Document?> doc)
        {
            _doc = doc;
        }

        public int CreateSlab(SlabDefinition slabDef, long levelId, int slabTypeId)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var elementId = 0;
            List<XYZ> boundaryPoints = new List<XYZ>();
            CurveLoop curveLoop = new CurveLoop();
            IList<CurveLoop> curveLoopList = new List<CurveLoop>();

            foreach (Point2D pt in slabDef.Points)
            {
                boundaryPoints.Add(new XYZ(UC.MmToFt(pt.X), UC.MmToFt(pt.Y), 0));
            }

            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                var startPt = boundaryPoints[i];
                var endPt = boundaryPoints[(i + 1) % boundaryPoints.Count];
                Line line = Line.CreateBound(startPt, endPt);
                curveLoop.Append(line);
            }
            curveLoopList.Add(curveLoop);

            var subPoints = new List<XYZ>();
            CurveLoop subCurveLoop = new CurveLoop();

            if (slabDef.SubPoints != null)
            {
                foreach (Point2D pt in slabDef.SubPoints)
                {
                    subPoints.Add(new XYZ(UC.MmToFt(pt.X), UC.MmToFt(pt.Y), 0));
                }

                for (int i = 0; i < subPoints.Count; i++)
                {
                    var startPt = subPoints[i];
                    var endPt = subPoints[(i + 1) % subPoints.Count];
                    Line line = Line.CreateBound(startPt, endPt);
                    subCurveLoop.Append(line);
                }

                if (subPoints.Count > 0)
                    curveLoopList.Add(subCurveLoop);
            }

            var level = doc.GetElement(new ElementId(levelId)) as Level;
            if (level == null) throw new DhbimwaterException(ErrorCodes.VR_LevelNotFound, $"레벨(Id {levelId})을 찾을 수 없습니다.");

            var floor = Floor.Create(doc, curveLoopList, new ElementId((long)slabTypeId), level.Id);
            // Z값 조정
            floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(UC.MmToFt(slabDef.ElevationZ) - level.Elevation); ;
            //floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(slabDef.ElementCode);
            floor.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            floor.LookupParameter("DH_Category")?.Set(slabDef.Category);
            floor.LookupParameter("DH_ElementCode")?.Set(slabDef.ElementCode);
            floor.LookupParameter("DH_Part")?.Set(slabDef.Part);
            floor.LookupParameter("DH_Zone")?.Set(slabDef.Zone);

            // 만든 바닥 ID 줘야 함. 전에 0 줬음 → 드레인 피트가 엉뚱한 요소 자르려 함.
            elementId = (int)floor.Id.Value;
            return elementId;
        }

        /// <summary>레거시 배수지 결합 이식(RH:2940-2975). 슬래브와 교차하는 보·헌치·벽을 결합.
        /// 트랜잭션은 호출 UseCase 것을 씀. 쌍별 실패는 대화상자 대신 경고로 모음.</summary>
        public IReadOnlyList<string> JoinWithIntersectingFramingAndWalls(int slabId)
        {
            var warnings = new List<string>();
            var doc = _doc();
            if (doc == null || slabId == 0) return warnings;
            if (doc.GetElement(new ElementId((long)slabId)) is not Floor slab) return warnings;

            // 방금 만든 부재는 재생성 전 형상이 비어 교차 판정 못 함
            doc.Regenerate();
            var intersects = new ElementIntersectsElementFilter(slab);
            var targets = new FilteredElementCollector(doc)
                .WherePasses(new LogicalOrFilter(
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming),
                    new ElementCategoryFilter(BuiltInCategory.OST_Walls)))
                .WhereElementIsNotElementType()
                .WherePasses(intersects)
                .ToElements();

            foreach (var target in targets)
            {
                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, slab, target)) continue;
                    JoinGeometryUtils.JoinGeometry(doc, slab, target);
                }
                catch (Exception ex)
                {
                    warnings.Add($"슬래브-부재 결합 실패 — 슬래브 {slab.Id.Value}, 부재 {target.Id.Value}: {ex.Message}");
                }
            }
            return warnings;
        }
    }
}
