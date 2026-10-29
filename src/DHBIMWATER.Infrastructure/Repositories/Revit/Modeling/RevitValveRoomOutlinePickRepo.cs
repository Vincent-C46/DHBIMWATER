using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using System.Globalization;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    /// <summary>
    /// Revit 뷰에서 기존 객체(외곽 벽체 + 기둥)를 피킹해 <see cref="ValveRoomOutline"/>을 만든다(docs/184).
    /// 반드시 Revit API 컨텍스트(ExternalEvent)에서 호출해야 한다.
    /// </summary>
    internal class RevitValveRoomOutlinePickRepo : IValveRoomOutlinePickRepo
    {
        private readonly Func<UIDocument?> _uiDoc;

        public RevitValveRoomOutlinePickRepo(Func<UIDocument?> uiDoc)
        {
            _uiDoc = uiDoc;
        }

        public ValveRoomOutline? PickExistingObjects()
        {
            // 문서 없음은 취소(null)와 구분해 SYS-101로 알린다(docs/239 § C). 0개 선택은 빈 외곽(IsEmpty)으로 온다.
            var uiDoc = _uiDoc() ?? throw new DHBIMWATER.Shared.Errors.DhbimwaterException(
                DHBIMWATER.Shared.Errors.ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

            IList<Reference> references;
            try
            {
                references = uiDoc.Selection.PickObjects(ObjectType.Element, new ExistingObjectSelectionFilter(),
                    "밸브실 외곽 벽체·기둥·기초를 선택한 뒤 완료를 누르세요");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;    // 사용자가 ESC로 취소
            }

            var elements = references.Select(r => uiDoc.Document.GetElement(r)).Where(e => e != null).ToList();
            // 아무것도 안 고르고 [완료]면 빈 외곽을 돌려준다 — null은 Esc 취소 전용이다(docs/239 § C).
            return BuildOutline(elements) ?? new ValveRoomOutline([], []);
        }

        /// <summary>저장된 ElementId로 외곽을 다시 만든다(docs/204 § C-3). 피킹 UI를 띄우지 않는다.
        /// 삭제된 부재는 제외하고 남은 것만으로 재구성하며, 제외된 개수는 <paramref name="missingCount"/>로 돌려준다.</summary>
        public ValveRoomOutline? ResolveExistingObjects(IReadOnlyList<long> elementIds, out int missingCount)
        {
            missingCount = 0;
            var uiDoc = _uiDoc();
            if (uiDoc == null || elementIds.Count == 0) return null;

            var elements = new List<Element>(elementIds.Count);
            foreach (var id in elementIds)
            {
                var element = uiDoc.Document.GetElement(new ElementId(id));
                if (element is null) { missingCount++; continue; }
                elements.Add(element);
            }
            return elements.Count == 0 ? null : BuildOutline(elements);
        }

        /// <summary>Element 목록 → 외곽. 피킹(<see cref="PickExistingObjects"/>)과
        /// Id 재해석(<see cref="ResolveExistingObjects"/>)이 같은 변환을 쓴다(docs/204 § C-2).</summary>
        private static ValveRoomOutline? BuildOutline(IReadOnlyList<Element> elements)
        {
            var walls = elements
                .OfType<Wall>()
                .Where(w => w.Location is LocationCurve { Curve: Line })
                .ToList();

            // 기둥은 간섭 확인용 참조물이다 — 외곽 치수에는 쓰지 않으므로 벽과 따로 담는다(docs/184 § 2).
            // 한 본이 단면(본체·헌치) 개수만큼 여러 항목으로 쪼개져 들어온다(docs/189 § B-1).
            var columns = elements
                .Where(IsColumn)
                .SelectMany(ToOutlineColumns)
                .ToList();

            if (walls.Count == 0 && columns.Count == 0) return null;

            // 실내측 판정 기준점: 선택한 벽 중심선 중점들의 평균. 기둥을 섞으면 판정이 흔들리므로 벽만 쓴다.
            // 벽이 없으면(기둥만 고른 경우) 벽 목록이 비어 있어 아래 Select가 돌지 않는다.
            var center = walls.Count == 0 ? XYZ.Zero : Average(walls.Select(MidPoint).ToList());

            var outlineWalls = walls.Select(wall => ToOutlineWall(wall, center)).ToList();
            var bounds = new ValveRoomOutline(outlineWalls);
            // 기초는 공기밸브실 IN/OUT 접점용 평면 경계상자만 담음(2026-10-29). 외곽 치수에는 안 씀.
            var foundations = elements.Where(IsFoundation).Select(ToOutlineFoundation).OfType<OutlineFoundation>().ToList();
            return new ValveRoomOutline(outlineWalls, columns, walls.Count == 0 ? null : FindMainPipeVoid(walls[0].Document, bounds), foundations);
        }

        /// <summary>공기밸브실 기초 본관 관통 Void(RevitAirValveVoidCommandRepo가 DH_Part로 표시). 평면 중심이 외곽 경계상자 안인 것 중
        /// 외곽 중심에 가장 가까운 1개. 축 = 인스턴스 Z축(호스트 면 법선), 중심·표고 = 배치점, 지름 = 2r(없으면 상자 높이).
        /// 관 끝 = 중심선 × 호스트 기초 솔리드 교점(2026-10-29).</summary>
        private static MainPipeVoid? FindMainPipeVoid(Document doc, ValveRoomOutline bounds)
        {
            if (bounds.IsEmpty) return null;
            var mid = new Point2D((bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2);
            return new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(x => x.LookupParameter("DH_Part")?.AsString() == MainPipeVoidPart)
                .Select(ToMainPipeVoid)
                .OfType<MainPipeVoid>()
                .Where(v => v.Center.X >= bounds.MinX && v.Center.X <= bounds.MaxX && v.Center.Y >= bounds.MinY && v.Center.Y <= bounds.MaxY)
                .OrderBy(v => v.Center.DistanceTo(mid))
                .FirstOrDefault();

            static MainPipeVoid? ToMainPipeVoid(FamilyInstance instance)
            {
                var box = instance.get_BoundingBox(null);
                // 면기반 Void: 패밀리 Z축 = 호스트 면 법선 = 관통 축, 배치점 = 관 중심(RevitAirValveVoidCommandRepo.AlignToTarget).
                var transform = instance.GetTransform();
                var origin = transform.Origin;
                var axis = new XYZ(transform.BasisZ.X, transform.BasisZ.Y, 0);
                if (axis.GetLength() < 0.5)
                {
                    // 수평 축이 아니면(호스팅 이상) 종전처럼 경계상자로 추정.
                    if (box is null) return null;
                    axis = box.Max.X - box.Min.X >= box.Max.Y - box.Min.Y ? XYZ.BasisX : XYZ.BasisY;
                    origin = (box.Min + box.Max) / 2;
                }
                axis = axis.Normalize();

                var radius = instance.LookupParameter("r") is { StorageType: StorageType.Double } r ? UC.FtToMm(r.AsDouble()) : 0;
                var diameter = radius > 0 ? radius * 2 : box is null ? 0 : UC.FtToMm(box.Max.Z - box.Min.Z);
                if (diameter <= 0) return null;

                var run = instance.Host is { } host ? CrossFoundation(host, origin, axis) : null;
                return new MainPipeVoid(
                    new Point2D(UC.FtToMm(origin.X), UC.FtToMm(origin.Y)),
                    AlongX: Math.Abs(axis.X) >= Math.Abs(axis.Y),
                    Math.Round(diameter, 1),
                    UC.FtToMm(origin.Z),
                    run?.Start, run?.End);
            }
        }

        /// <summary>관 중심선(origin + t·axis)이 기초 솔리드를 지나는 구간의 양 끝(평면 mm). 못 구하면 null — 기초 경계상자 폴백.
        /// <para>절단 전 형상(GetOriginalGeometry)을 쓴다 — 절단 후 형상은 중심선이 Void 구멍 안을 지나 교점이 없다.</para></summary>
        private static (Point2D Start, Point2D End)? CrossFoundation(Element host, XYZ origin, XYZ axis)
        {
            const double HalfLengthFt = 300;   // 약 91 m. 기초보다 충분히 김.
            var options = new Options { DetailLevel = ViewDetailLevel.Fine };
            var geometry = host is FamilyInstance familyInstance
                ? familyInstance.GetOriginalGeometry(options)?.GetTransformed(familyInstance.GetTotalTransform())
                : host.get_Geometry(options);
            if (geometry is null) return null;

            var line = Line.CreateBound(origin - axis * HalfLengthFt, origin + axis * HalfLengthFt);
            var intersectOptions = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
            double? min = null, max = null;
            foreach (var solid in Solids(geometry))
            {
                SolidCurveIntersection hits;
                try { hits = solid.IntersectWithCurve(line, intersectOptions); }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { continue; }
                for (var k = 0; k < hits.SegmentCount; k++)
                {
                    var segment = hits.GetCurveSegment(k);
                    foreach (var t in new[] { (segment.GetEndPoint(0) - origin).DotProduct(axis), (segment.GetEndPoint(1) - origin).DotProduct(axis) })
                    {
                        min = min is null ? t : Math.Min(min.Value, t);
                        max = max is null ? t : Math.Max(max.Value, t);
                    }
                }
            }
            if (min is not { } a || max is not { } b || b - a < 1e-6) return null;
            var start = origin + axis * a;
            var end = origin + axis * b;
            return (new Point2D(UC.FtToMm(start.X), UC.FtToMm(start.Y)), new Point2D(UC.FtToMm(end.X), UC.FtToMm(end.Y)));

            static IEnumerable<Solid> Solids(GeometryElement element)
            {
                foreach (var item in element)
                {
                    if (item is Solid { Volume: > 0 } solid) yield return solid;
                    else if (item is GeometryInstance nested)
                        foreach (var inner in Solids(nested.GetInstanceGeometry())) yield return inner;
                }
            }
        }

        private static bool IsFoundation(Element element)
            => element.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;

        private static OutlineFoundation? ToOutlineFoundation(Element element)
            => element.get_BoundingBox(null) is { } box
                ? new OutlineFoundation(element.Id.Value, UC.FtToMm(box.Min.X), UC.FtToMm(box.Min.Y), UC.FtToMm(box.Max.X), UC.FtToMm(box.Max.Y))
                : null;

        /// <summary>RevitAirValveVoidCommandRepo가 Void에 찍는 DH_Part 값.</summary>
        private const string MainPipeVoidPart = "본관 관통 Void";

        // TODO: 헌치가 기둥과 형상 결합돼 있지 않고 별도 구조 프레임(OST_StructuralFraming)으로 서 있으면
        // 지금은 선택 필터에 안 걸려 단면이 나오지 않는다. 필요해지면 필터에 추가한다(docs/189 § 4).
        private static bool IsColumn(Element element)
        {
            // 구조 기둥과 건축 기둥 둘 다 허용한다 — 프로젝트마다 어느 쪽으로 모델링했는지 다르다(docs/184 § 2-1).
            var categoryId = element.Category?.Id.Value;
            return categoryId == (long)BuiltInCategory.OST_StructuralColumns
                || categoryId == (long)BuiltInCategory.OST_Columns;
        }

        /// <summary>수평면 판정 — 면 법선의 Z 성분 절대값이 이 값 이상이어야 수평으로 본다.</summary>
        private const double HorizontalFaceTolerance = 0.999;

        /// <summary>같은 점으로 볼 거리(mm). 이어지는 곡선의 중복 꼭짓점을 걸러낸다.</summary>
        private const double PointToleranceMm = 0.1;

        /// <summary>기둥 한 본이 그릴 수 있는 단면 선 수의 상한. 캔버스 과밀 방지용이다.</summary>
        // TODO: 상한 8은 임시값 — 실모델 확인 후 조정.
        private const int MaxProfilesPerColumn = 8;

        /// <summary>
        /// 기둥 한 본을 <b>수평 단면별</b> 외곽선으로 쪼갠다(docs/189 § B-2).
        /// 헌치가 붙은 기둥은 본체 단면과 헌치 단면이 각각 나오므로, 배관 설계 시 헌치 최외곽만큼 과다하게 피하지 않아도 된다.
        /// <para>솔리드 전 높이의 점을 한 덩어리로 투영하던 종전 방식(볼록 껍질)은 수평 평면 면이 하나도 없는
        /// 형상에서만 폴백으로 쓴다(docs/189 § B-5).</para>
        /// </summary>
        private static IEnumerable<OutlineColumn> ToOutlineColumns(Element element)
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Medium, ComputeReferences = false, IncludeNonVisibleObjects = false };
            var geometry = element.get_Geometry(options);
            if (geometry == null) return [];

            var profiles = new List<IReadOnlyList<Point2D>>();
            CollectPlanProfiles(geometry, Transform.Identity, profiles);

            var distinct = Deduplicate(profiles);
            if (distinct.Count == 0) return FallbackHull(element, geometry);

            // 면적 오름차순 — 가장 작은 단면이 기둥 본체이고 나머지는 헌치 등 덧붙은 단면이다.
            var ordered = distinct.OrderBy(PolygonArea).ToList();
            if (ordered.Count > MaxProfilesPerColumn)
            {
                // 본체(첫 항목)는 반드시 남기고, 나머지는 큰 것부터 채운 뒤 다시 면적 오름차순으로 되돌린다.
                ordered = ordered.Take(1)
                    .Concat(ordered.Skip(1).OrderByDescending(PolygonArea).Take(MaxProfilesPerColumn - 1))
                    .OrderBy(PolygonArea)
                    .ToList();
            }

            return ordered.Select((profile, index) => new OutlineColumn(element.Id.Value, profile, index == 0)).ToList();
        }

        /// <summary>
        /// 수평 평면 면이 하나도 없는 형상(경사 기둥, 회전체만으로 된 기둥 등)의 폴백이다.
        /// 단면을 나눌 근거가 없으므로 종전처럼 전 높이를 투영한 볼록 껍질 하나로 그린다(docs/189 § B-5).
        /// </summary>
        private static IEnumerable<OutlineColumn> FallbackHull(Element element, GeometryElement geometry)
        {
            var points = new List<Point2D>();
            CollectPlanPoints(geometry, Transform.Identity, points);
            if (points.Count < 3) return [];

            var hull = ConvexHull.Compute(points);
            return hull.Count < 3 ? [] : [new OutlineColumn(element.Id.Value, hull)];
        }

        /// <summary>
        /// 솔리드의 수평 평면 면마다 외곽 루프를 XY로 투영해 단면 폴리곤을 모은다(docs/189 § B-3).
        /// 헌치가 결합된 기둥은 단이 진 레벨마다 수평면이 생기므로 본체 단면과 헌치 단면이 따로 잡힌다.
        /// </summary>
        private static void CollectPlanProfiles(GeometryElement geometry, Transform transform, List<IReadOnlyList<Point2D>> profiles)
        {
            foreach (var geometryObject in geometry)
            {
                switch (geometryObject)
                {
                    case GeometryInstance instance:
                        // CollectPlanPoints와 같은 이유로 instance.Transform을 다시 곱하지 않는다(이중 적용 방지).
                        CollectPlanProfiles(instance.GetInstanceGeometry(), transform, profiles);
                        break;

                    case Solid { Volume: > 0 } solid:
                        foreach (Face face in solid.Faces)
                        {
                            if (face is not PlanarFace planar) continue;
                            // 수평면만 본다 — 경사·수직면은 평면 투영에서 단면을 대표하지 못한다.
                            if (Math.Abs(transform.OfVector(planar.FaceNormal).Normalize().Z) < HorizontalFaceTolerance) continue;

                            // 첫 루프가 외곽이라고 보장되지 않으므로 투영 면적이 가장 큰 루프를 외곽으로 본다.
                            // 구멍(안쪽 루프)은 버린다 — 단 진 면의 안쪽 루프는 본체 단면과 같아 어차피 중복 제거된다.
                            IReadOnlyList<Point2D>? outer = null;
                            var outerArea = 0.0;
                            foreach (var loop in face.GetEdgesAsCurveLoops())
                            {
                                var polygon = ToPolygon(loop, transform);
                                if (polygon.Count < 3) continue;
                                var area = PolygonArea(polygon);
                                if (outer != null && area <= outerArea) continue;
                                outer = polygon; outerArea = area;
                            }

                            if (outer != null) profiles.Add(Counterclockwise(outer));
                        }
                        break;
                }
            }
        }

        /// <summary><see cref="CurveLoop"/>을 XY(mm) 폴리곤으로 편다. 이어지는 곡선의 중복 시작점과 닫는 점은 뺀다.</summary>
        private static List<Point2D> ToPolygon(CurveLoop loop, Transform transform)
        {
            var polygon = new List<Point2D>();
            foreach (Curve curve in loop)
                foreach (var point in curve.Tessellate())
                {
                    var world = transform.OfPoint(point);
                    var projected = new Point2D(UC.FtToMm(world.X), UC.FtToMm(world.Y));
                    if (polygon.Count > 0 && SamePoint(polygon[^1], projected)) continue;
                    polygon.Add(projected);
                }

            // 루프는 닫혀 있어 마지막 점이 첫 점과 같다. Polygon은 자동으로 닫히니 떼어 낸다.
            if (polygon.Count > 1 && SamePoint(polygon[0], polygon[^1])) polygon.RemoveAt(polygon.Count - 1);
            return polygon;
        }

        /// <summary>
        /// 같은 단면을 여러 번 그리지 않도록 중복을 걸러 낸다(docs/189 § B-4).
        /// 부동소수를 그대로 비교할 수 없어 1mm·1mm² 단위로 반올림한 서명으로 가른다.
        /// 단 없는 각기둥은 상·하면 단면이 같으므로 여기서 하나로 합쳐진다.
        /// </summary>
        private static List<IReadOnlyList<Point2D>> Deduplicate(IReadOnlyList<IReadOnlyList<Point2D>> profiles)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<IReadOnlyList<Point2D>>();
            foreach (var profile in profiles)
            {
                var signature = string.Join("|",
                    Key(profile.Min(p => p.X)), Key(profile.Min(p => p.Y)),
                    Key(profile.Max(p => p.X)), Key(profile.Max(p => p.Y)),
                    Key(PolygonArea(profile)), profile.Count.ToString(CultureInfo.InvariantCulture));
                if (seen.Add(signature)) result.Add(profile);
            }
            return result;

            static string Key(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>신발끈 공식의 부호 있는 넓이. 반시계면 양수다.</summary>
        private static double SignedArea(IReadOnlyList<Point2D> polygon)
        {
            var sum = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }

        private static double PolygonArea(IReadOnlyList<Point2D> polygon) => Math.Abs(SignedArea(polygon));

        /// <summary>꼭짓점 순서를 반시계로 통일한다. 면이 아래를 보면 루프가 시계 방향으로 나온다.</summary>
        private static IReadOnlyList<Point2D> Counterclockwise(IReadOnlyList<Point2D> polygon)
            => SignedArea(polygon) < 0 ? Enumerable.Reverse(polygon).ToList() : polygon;

        private static bool SamePoint(Point2D a, Point2D b)
            => Math.Abs(a.X - b.X) < PointToleranceMm && Math.Abs(a.Y - b.Y) < PointToleranceMm;

        /// <summary>전 높이의 솔리드 모서리를 XY로 투영해 한 덩어리로 모은다. 이제는 폴백 전용이다(docs/189 § B-5).</summary>
        private static void CollectPlanPoints(GeometryElement geometry, Transform transform, List<Point2D> points)
        {
            foreach (var geometryObject in geometry)
            {
                switch (geometryObject)
                {
                    case GeometryInstance instance:
                        // GetInstanceGeometry()는 이미 인스턴스 변환이 적용된 좌표를 준다 —
                        // 여기서 instance.Transform을 또 곱하면 이중 적용이 된다. 바깥 변환만 그대로 넘긴다.
                        CollectPlanPoints(instance.GetInstanceGeometry(), transform, points);
                        break;

                    case Solid { Volume: > 0 } solid:
                        foreach (Edge edge in solid.Edges)
                            foreach (var point in edge.Tessellate())
                            {
                                var world = transform.OfPoint(point);
                                points.Add(new Point2D(UC.FtToMm(world.X), UC.FtToMm(world.Y)));
                            }
                        break;
                }
            }
        }

        private static OutlineWall ToOutlineWall(Wall wall, XYZ center)
        {
            var line = (Line)((LocationCurve)wall.Location).Curve;
            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);

            var direction = (end - start).Normalize();
            var normal = new XYZ(-direction.Y, direction.X, 0);      // 평면상 좌측 법선
            var half = wall.Width / 2.0;

            // 중심점에 더 가까운 쪽이 실내측이다.
            var mid = (start + end) / 2.0;
            var towardCenter = mid + normal * half;
            var awayFromCenter = mid - normal * half;
            var innerNormal = towardCenter.DistanceTo(center) <= awayFromCenter.DistanceTo(center) ? normal : -normal;

            var innerStart = start + innerNormal * half;
            var innerEnd = end + innerNormal * half;
            var outerStart = start - innerNormal * half;
            var outerEnd = end - innerNormal * half;

            return new OutlineWall(
                wall.Id.Value,
                ToPoint2D(innerStart),
                ToPoint2D(innerEnd),
                ToPoint2D(outerStart),
                ToPoint2D(outerEnd),
                UC.FtToMm(wall.Width),
                // 밸브실 빌더가 구조부재에 찍어 둔 유형명이다(CreateValveRoomUseCase.cs:122). 없으면 빈 문자열(docs/161 § B).
                wall.LookupParameter("DH_Zone")?.AsString() ?? string.Empty,
                wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId()?.Value ?? 0);
        }

        private static XYZ MidPoint(Wall wall)
        {
            var curve = ((LocationCurve)wall.Location).Curve;
            return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2.0;
        }

        private static XYZ Average(IReadOnlyList<XYZ> points)
            => new(points.Average(p => p.X), points.Average(p => p.Y), 0);

        private static Point2D ToPoint2D(XYZ point) => new(UC.FtToMm(point.X), UC.FtToMm(point.Y));

        /// <summary>벽체와 기둥을 함께 고를 수 있게 한다(docs/184 § C-1). 기둥은 배관과 겹칠 수 있어 같이 가져온다.</summary>
        private sealed class ExistingObjectSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Wall || IsColumn(elem) || IsFoundation(elem);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
