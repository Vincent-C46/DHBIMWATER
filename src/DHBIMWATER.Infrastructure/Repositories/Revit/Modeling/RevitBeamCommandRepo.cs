using Autodesk.Revit.DB;
using System.IO;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    public class RevitBeamCommandRepo : IBeamCommandRepo
    {
        private readonly Func<Document?> _doc;
        // 밸브실 보(VR-B) 경로의 경고(docs/238 § C). 종전에는 트랜잭션 도중 TaskDialog로 띄웠다 — 호출부가 TakeWarnings로 가져가 완료 대화상자에 보여준다.
        // VR-B 경로는 밸브실 UseCase만 타므로 배수지·펌프장 동작은 바뀌지 않는다.
        private readonly List<string> _warnings = [];
        public RevitBeamCommandRepo(Func<Document?> doc)
        {
            _doc = doc;
        }

        public IReadOnlyList<string> TakeWarnings()
        {
            var warnings = _warnings.ToList();
            _warnings.Clear();
            return warnings;
        }

        public int CreateBeam(BeamDefinition beamDef, long levelId, int beamTypeId, int joinTargetSlabId = 0)
        {
            var doc = _doc();
            if (doc == null) return 0;

            // 0 = 실패. 밸브실은 UseCase가 Require로 중단한다(docs/207 § B).
            var beamType = doc.GetElement(new ElementId((long)beamTypeId)) as FamilySymbol;
            if (beamType == null) return 0;

            if (!beamType.IsActive)
            {
                beamType.Activate();
                //TaskDialog.Show("Info", $"Beam type '{beamType.Name}' activated. BeamRepo에서 활성화됨");
            }

            var curve = Line.CreateBound(new XYZ(UC.MmToFt(beamDef.StartPoint.X), UC.MmToFt(beamDef.StartPoint.Y), UC.MmToFt(beamDef.StartPoint.Z)),
                                         new XYZ(UC.MmToFt(beamDef.EndPoint.X), UC.MmToFt(beamDef.EndPoint.Y), UC.MmToFt(beamDef.EndPoint.Z)));
            Level level = doc.GetElement(new ElementId(levelId)) as Level;
            if (level == null) return 0;

            var beam = doc.Create.NewFamilyInstance(curve, beamType, level, StructuralType.Beam);
            StructuralFramingUtils.DisallowJoinAtEnd(beam, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);

            // 헌치는 여기서 Z맞춤을 적용한다. 밸브실 보(VR-B)는 AlignBeamTopToCurveZ가 설정·검증까지 맡는다(docs/198 § A).
            // 펌프장 보(GIRDER)도 Z맞춤 명시(상단) — 커브 z = 상부슬래브 상면. 기본값에 기대지 않는다.
            // 그 외(Reservoir 등)는 기존대로 Revit 기본 Z맞춤을 사용한다.
            if (beamDef.Part == "HAUNCH" || beamDef.Part == "GIRDER")
                beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION).Set(beamDef.ZJustification);

            // 단면 회전(Cross-Section Rotation). 내부 단위가 라디안이라 도 → 라디안 변환이 필요하다.
            // 0이면 Revit 기본값을 그대로 두어 기존 부재(배수지·펌프장 보 등)의 동작을 바꾸지 않는다.
            if (beamDef.CrossSectionRotationDeg != 0)
                beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)?.Set(UC.DegToRad(beamDef.CrossSectionRotationDeg));

            // 밸브실 보: 원점(2) 기준에서 보 높이의 절반만큼 내려 보 상단을 상부슬래브 상단(커브 z)에 맞춘다.
            // 맞춤을 결합보다 먼저 끝내야 한다 — 결합하면 형상이 잘려 실측이 어긋난다(docs/198 § C).
            if (beamDef.ElementCode == "VR-B")
            {
                AlignBeamTopToCurveZ(beam, beamType, beamDef.ZJustification, UC.MmToFt(beamDef.StartPoint.Z));
                JoinWithUpperSlab(beam, joinTargetSlabId);
            }
            else if (beamDef.Part == "GIRDER")
            {
                // 펌프장 보: 맞춤 반영 후 상부슬래브와 결합(슬래브가 보를 자름). 맞춤 → 재생성 → 결합 순서.
                doc.Regenerate();
                JoinWithUpperSlab(beam, joinTargetSlabId);
            }

            beam.LookupParameter("DH_ElementCode")?.Set(beamDef.ElementCode);
            beam.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            beam.LookupParameter("DH_Part")?.Set(beamDef.Part);
            beam.LookupParameter("DH_Zone")?.Set(beamDef.Zone);
            beam.LookupParameter("DH_Category")?.Set(beamDef.Part == "HAUNCH" ? "헌치" : beamDef.Category);

            //haunch.get_Parameter(BuiltInParameter.Z_JUSTIFICATION).Set(2);  // Z맞춤: 상단(0), 중심(1), 원점 (2), 하단(3)

            return (int)beam.Id.Value;
        }

        /// <summary>헌치끼리 형상 결합. 코너 4곳과 이토실 중간벽 T접합부에서 헌치 단면이 서로 겹친 채 남기 때문이다(docs/162 § B-3).
        /// <para>경계상자가 겹치는 쌍만 시도하고 <b>예외는 삼킨다</b> — 경계상자는 겹쳐도 솔리드가 안 닿는 쌍이 정상적으로
        /// 존재해서, <see cref="JoinWithUpperSlab"/>처럼 TaskDialog를 띄우면 코너마다 경고가 쏟아진다.</para></summary>
        public void JoinHaunches(IReadOnlyList<int> haunchIds)
        {
            var doc = _doc();
            if (doc == null || haunchIds == null || haunchIds.Count < 2) return;

            // 방금 만든 인스턴스는 재생성 전에는 경계상자가 비어 있을 수 있다.
            doc.Regenerate();
            var haunches = haunchIds.Where(id => id != 0).Distinct()
                .Select(id => doc.GetElement(new ElementId((long)id)))
                .Where(element => element != null)
                .Select(element => (Element: element, Box: element.get_BoundingBox(null)))
                .Where(item => item.Box != null)
                .ToList();

            for (var i = 0; i < haunches.Count; i++)
                for (var j = i + 1; j < haunches.Count; j++)
                {
                    if (!BoundingBoxesOverlap(haunches[i].Box, haunches[j].Box)) continue;
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, haunches[i].Element, haunches[j].Element)) continue;
                        JoinGeometryUtils.JoinGeometry(doc, haunches[i].Element, haunches[j].Element);
                    }
                    catch
                    {
                        // 실제로 맞닿지 않은 쌍이다. 결합하지 않고 넘어간다.
                    }
                }
        }

        /// <summary>보 상단을 커브 z(=상부슬래브 상단)에 맞춘다(docs/198).
        /// <para>Z맞춤 원점(2) + Z 간격띄우기 값이라는 기존 방식을 그대로 쓰되, 유형 파라미터에서 얻은 단면 높이로
        /// 1차 설정한 뒤 <b>실제 형상을 다시 재서 남은 오차만큼 보정</b>한다. 지금까지 보가 h/2 솟아 있었는데
        /// ① 단면 높이 파라미터명이 패밀리마다 달라 0이 나오거나 ② Set()이 거부돼도(맞춤이 "독립"이면 통합 Z
        /// 파라미터가 읽기전용이 된다) 반환값을 버려 알 수 없었기 때문이다. 세 지점을 모두 기록해 원인을 남긴다.</para>
        /// <para>원점 맞춤에서 오프셋은 형상을 1:1로 이동시키므로 보정은 1회면 충분하다.</para></summary>
        private void AlignBeamTopToCurveZ(FamilyInstance beam, FamilySymbol beamType, int zJustification, double targetTopFt)
        {
            var doc = _doc()!;
            var log = new List<string>
            {
                $"[VR-B {beam.Id.Value}] type='{beamType.Family.Name} : {beamType.Name}' targetTop={UC.FtToMm(targetTopFt):F1}mm"
            };

            // (1) 맞춤이 "독립"이면 Revit이 통합 Z_JUSTIFICATION/Z_OFFSET_VALUE를 무시한다. 통합(0)으로 되돌린다.
            var yz = beam.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz == null) log.Add("YZ_JUSTIFICATION=null");
            else
            {
                log.Add($"YZ_JUSTIFICATION before={yz.AsInteger()} readOnly={yz.IsReadOnly}");
                if (yz.AsInteger() != (int)YZJustificationOption.Uniform && !yz.IsReadOnly)
                    log.Add($"YZ->Uniform set={yz.Set((int)YZJustificationOption.Uniform)}");
            }

            // (2) Z맞춤 = 원점(2). 반환값을 버리지 않는다.
            var zj = beam.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            log.Add(zj == null
                ? "Z_JUSTIFICATION=null"
                : $"Z_JUSTIFICATION readOnly={zj.IsReadOnly} set({zJustification})={zj.Set(zJustification)} after={zj.AsInteger()}");

            // (3) 1차 추정: 유형 단면 높이의 절반만큼 내린다. 못 구하면 0에서 시작하고 (4)가 메운다.
            var zo = beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
            var halfHeightFt = GetBeamHeightFt(beamType, log) / 2;
            log.Add(zo == null
                ? "Z_OFFSET_VALUE=null"
                : $"Z_OFFSET_VALUE readOnly={zo.IsReadOnly} set({UC.FtToMm(-halfHeightFt):F1}mm)={zo.Set(-halfHeightFt)}");

            // (4) 실측 보정. 이 시점엔 아직 슬래브와 결합 전이라 경계상자가 보 원형 그대로다.
            doc.Regenerate();
            var box = beam.get_BoundingBox(null);
            if (box == null) log.Add("boundingBox=null");
            else
            {
                var deltaFt = targetTopFt - box.Max.Z;
                log.Add($"measuredTop={UC.FtToMm(box.Max.Z):F1}mm delta={UC.FtToMm(deltaFt):F1}mm");
                if (Math.Abs(deltaFt) > TopAlignToleranceFt && zo != null && !zo.IsReadOnly)
                {
                    log.Add($"correct set={zo.Set(zo.AsDouble() + deltaFt)}");
                    doc.Regenerate();
                    box = beam.get_BoundingBox(null);
                }
            }

            // (5) 남은 오차는 조용히 넘기지 않는다.
            var residualMm = box == null ? double.NaN : UC.FtToMm(targetTopFt - box.Max.Z);
            log.Add($"residual={residualMm:F1}mm finalOffset={(zo == null ? double.NaN : UC.FtToMm(zo.AsDouble())):F1}mm");

            // 정상 보에서는 파일 I/O를 하지 않는다 — 진단 로그는 실패했을 때만 필요하다(docs/207 § C-4).
            if (box == null || Math.Abs(targetTopFt - box.Max.Z) > TopAlignToleranceFt)
            {
                LogZAlignment(log);
                _warnings.Add($"보 상단 맞춤 실패 — 보 {beam.Id.Value}, 잔여 {residualMm:F1}mm (진단 로그: {ZLogPath})");
            }
        }

        /// <summary>보 상단 맞춤 허용오차(0.5mm). MmToFt가 메서드라 const가 아니라 static readonly다.</summary>
        private static readonly double TopAlignToleranceFt = UC.MmToFt(0.5);

        /// <summary>단면 높이 파라미터 후보. 사용자가 고르는 구조 프레임 패밀리마다 이름이 달라 넓게 본다(docs/198 § D).</summary>
        private static readonly string[] HeightParameterNames = { "h", "H", "d", "D", "춤", "보춤", "높이", "단면높이", "Height" };

        /// <summary>보 타입의 단면 높이를 내부 단위(피트)로 반환한다. 어느 후보도 없으면 0 —
        /// 호출부(<see cref="AlignBeamTopToCurveZ"/>)가 실측 보정으로 메우므로 0이어도 배치 결과는 정상이다.
        /// 어디서 값을 얻었는지 <paramref name="log"/>에 남겨 원인 추적이 가능하게 한다.</summary>
        private static double GetBeamHeightFt(FamilySymbol beamType, List<string> log)
        {
            var builtIn = beamType.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
            if (builtIn != null && builtIn.StorageType == StorageType.Double)
            {
                log.Add($"height<-STRUCTURAL_SECTION_COMMON_HEIGHT={UC.FtToMm(builtIn.AsDouble()):F1}mm");
                return builtIn.AsDouble();
            }

            foreach (var name in HeightParameterNames)
            {
                var p = beamType.LookupParameter(name);
                if (p == null || p.StorageType != StorageType.Double) continue;
                log.Add($"height<-'{name}'={UC.FtToMm(p.AsDouble()):F1}mm");
                return p.AsDouble();
            }

            log.Add($"height NOT FOUND (tried STRUCTURAL_SECTION_COMMON_HEIGHT, {string.Join(", ", HeightParameterNames)})");
            return 0;
        }

        private void JoinWithUpperSlab(Element beam, int joinTargetSlabId)
        {
            var doc = _doc();
            IReadOnlyList<Floor> upperSlabs;
            if (joinTargetSlabId != 0 && doc.GetElement(new ElementId((long)joinTargetSlabId)) is Floor target)
            {
                // 호출부(밸브실 UseCase)가 상부슬래브를 알고 있으면 문서 전체 Floor를 훑지 않는다.
                // AlignBeamTopToCurveZ가 직전에 재생성해 두었으므로 여기서 또 하면 보 1개당 3회다(docs/207 § C).
                upperSlabs = [target];
            }
            else
            {
                // 종전 경로 — 호출부가 슬래브를 모르는 경우(배수지·펌프장)만 문서를 훑는다.
                doc.Regenerate();
                var beamBoundingBox = beam.get_BoundingBox(null);
                upperSlabs = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .Where(floor => BoundingBoxesOverlap(beamBoundingBox, floor.get_BoundingBox(null)))
                    .ToList();
            }

            if (upperSlabs.Count == 0)
            {
                _warnings.Add($"보-상부슬래브 겹침 없음 — 보 {beam.Id.Value}");
                return;
            }

            foreach (var slab in upperSlabs)
            {
                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, slab, beam)) continue;
                    JoinGeometryUtils.JoinGeometry(doc, slab, beam);
                }
                catch (Exception ex)
                {
                    _warnings.Add($"보-상부슬래브 결합 실패 — 보 {beam.Id.Value}, 슬래브 {slab.Id.Value}: {ex.Message}");
                }
            }
        }

        private static bool BoundingBoxesOverlap(BoundingBoxXYZ? first, BoundingBoxXYZ? second)
        {
            if (first == null || second == null) return false;

            return first.Min.X <= second.Max.X && first.Max.X >= second.Min.X
                && first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y
                && first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
        }

        /// <summary>보 Z 맞춤 진단 로그 경로. PlacementProfiler(%LOCALAPPDATA%\DHBIMWATER\Logs)와 같은 폴더에 파일만 분리한다.</summary>
        private static string ZLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DHBIMWATER", "Logs", "valveroom-beam-z.log");

        /// <summary>보 하나당 한 줄. 진단 실패가 배치를 막으면 안 되므로 예외를 삼킨다(PlacementProfiler.TryWrite와 같은 원칙).</summary>
        private static void LogZAlignment(IEnumerable<string> lines)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ZLogPath)!);
                File.AppendAllText(ZLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " + string.Join(" | ", lines) + Environment.NewLine);
            }
            catch
            {
                // 무시한다.
            }
        }
    }
}
