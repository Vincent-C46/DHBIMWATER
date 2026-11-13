using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using GC = DHBIMWATER.Infrastructure.Converters.RevitGeometryConverter;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using Wall = Autodesk.Revit.DB.Wall;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    internal class RevitWallCommandRepo : IWallCommandRepo
    {
        #region Fields
        private readonly Func<Document?> _doc;  // Revit Document에 접근하기 위한 람다식
        private readonly IDialogService _dialog;
        #endregion

        #region Properties
        #endregion

        #region Constructor
        public RevitWallCommandRepo(Func<Document?> doc, IDialogService dialog)
        {
            _doc = doc;
            _dialog = dialog;
        }
        #endregion

        #region Methods
        public int CreateLinearWall(LinearWallDefinition linearWallDefinition, long levelId, int wallTypeId)
        {
            Document? doc = _doc();

            if (doc == null)
            {
                _dialog.Warn("Error", "Active document is not available.");
                return 0;
            }
            var elementId = 0;

            XYZ startPt = new XYZ(UC.MmToFt(linearWallDefinition.StartPoint.X),
                                  UC.MmToFt(linearWallDefinition.StartPoint.Y),
                                  UC.MmToFt(linearWallDefinition.StartPoint.Z));
            XYZ endPt = new XYZ(UC.MmToFt(linearWallDefinition.EndPoint.X),
                                UC.MmToFt(linearWallDefinition.EndPoint.Y),
                                UC.MmToFt(linearWallDefinition.EndPoint.Z));
            Curve wallCurve = Line.CreateBound(startPt, endPt);


            //if (linearWallDefinition.Height <= 0)
            //{
            //    _dialog.Warn("Error", $"벽체 높이가 0이하 입니다.\nElementCode{linearWallDefinition.ElementCode}\nHeight: {linearWallDefinition.Height}");
            //}

            Wall wall;

            try
            {
                wall = Wall.Create(doc, wallCurve, new ElementId((long)wallTypeId), new ElementId(levelId),
                        UC.MmToFt(linearWallDefinition.Height),
                        UC.MmToFt(linearWallDefinition.BaseOffset),
                        linearWallDefinition.IsFlipped, true);
            }
            catch (Exception ex)
            {
                _dialog.Warn("Error", $"벽체 생성 실패\nElementCode: {linearWallDefinition.ElementCode}\nException: {ex.Message}");
                return 0;
            }

            SetStructural(wall);

            // 기본은 양 끝 결합 끊음. JoinAtStart/End 켠 끝만 결합 허용 — 이미 닿은 이웃 벽 있으면 Revit이 그 자리에서 결합함
            if (linearWallDefinition.JoinAtStart) WallUtils.AllowWallJoinAtEnd(wall, 0);
            else WallUtils.DisallowWallJoinAtEnd(wall, 0);
            if (linearWallDefinition.JoinAtEnd) WallUtils.AllowWallJoinAtEnd(wall, 1);
            else WallUtils.DisallowWallJoinAtEnd(wall, 1);

            // 상부 레벨 구속. TopLevelName 비면 기존 높이값 벽 그대로(docs/289 § B)
            if (!string.IsNullOrEmpty(linearWallDefinition.TopLevelName))
                ApplyTopConstraint(doc, wall, linearWallDefinition);

            // 위치선은 벽 형상이 생긴 뒤에 바꿈 — 생성 직후엔 면이 없어 "Can not get face for the specified wall object"(SYS-999)
            ApplyHoldFace(doc, wall, linearWallDefinition);

            //wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(linearWallDefinition.ElementCode);
            wall.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            wall.LookupParameter("DH_Category")?.Set(linearWallDefinition.Category);
            wall.LookupParameter("DH_ElementCode")?.Set(linearWallDefinition.ElementCode);
            wall.LookupParameter("DH_Part")?.Set(linearWallDefinition.Part);
            wall.LookupParameter("DH_Zone")?.Set(linearWallDefinition.Zone);
            // 외벽/내벽에 따라 거푸집 유형이 달라져 수량산출이 이 값을 쓴다(docs/155 § B).
            // 배수지·펌프장은 생성 직후 ClassifyExteriorWallsUseCase가 다시 판정해 덮어쓴다.
            wall.LookupParameter("DH_IsExterior")?.Set(linearWallDefinition.IsExterior ? 1 : 0);
            wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("");

            //_dialog.Info("RevitWallCommandRepo", $"CreateWall - Revit Implementation\n 벽체 높이: {linearWallDefinition.Height}mm");

            return (int)wall.Id.Value;
        }
        public int CreateProfileWall(ProfileWallDefinition profileWallDefinition, long levelId, int wallTypeId)
        {
            Document? doc = _doc();

            if (profileWallDefinition.Points.Count < 3)
            {
                _dialog.Warn("Error", "벽체 프로파일 점 개수가 3개 미만입니다");
                return 0;
            }


            var profiles = new List<Curve>();
            int numPoints = profileWallDefinition.Points.Count;

            for (int i = 0; i < numPoints; i++)
            {
                Point3D startPt = profileWallDefinition.Points[i];
                Point3D endPt = profileWallDefinition.Points[(i + 1) % numPoints];

                XYZ startXYZ = new XYZ(UC.MmToFt(startPt.X), UC.MmToFt(startPt.Y), UC.MmToFt(startPt.Z));
                XYZ endXYZ = new XYZ(UC.MmToFt(endPt.X), UC.MmToFt(endPt.Y), UC.MmToFt(endPt.Z));

                Curve line = Line.CreateBound(startXYZ, endXYZ);
                profiles.Add(line);
            }

            Wall profileWall;
            try
            {
                profileWall = Wall.Create(doc, profiles, new ElementId((long)wallTypeId), new ElementId(levelId), true);
            }
            catch( Exception ex)
            {
                _dialog.Warn("Error", $"프로파일 벽체 생성 실패\nElementCode: {profileWallDefinition.ElementCode}\nException: {ex.Message}");
                return 0;
            }

            profileWall.LookupParameter("DH_ElementCode")?.Set(profileWallDefinition.ElementCode);
            profileWall.LookupParameter("DH_Category")?.Set(profileWallDefinition.Category);
            profileWall.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            profileWall.LookupParameter("DH_Part")?.Set(profileWallDefinition.Part);
            profileWall.LookupParameter("DH_Zone")?.Set(profileWallDefinition.Zone);
            profileWall.LookupParameter("DH_IsExterior")?.Set(profileWallDefinition.IsExterior ? 1 : 0);

            SetStructural(profileWall);
            if (profileWallDefinition.IsFlipped) profileWall.Flip();

            AlignProfileWallBase(doc!, profileWall, profileWallDefinition, levelId);

            WallUtils.DisallowWallJoinAtEnd(profileWall, 0);
            WallUtils.DisallowWallJoinAtEnd(profileWall, 1);

            return (int)(profileWall.Id.Value);
        }
        #endregion

        #region Private Helpers
        /// <summary>
        /// HoldFace 면을 위치선으로 지정(docs/325 § F). 위치선 매개변수만 바꾸면 Revit은 벽 형상 그대로 두고 위치선만 그 면으로 옮김 →
        /// 이후 Revit에서 두께 바꿔도 그 면 고정, 반대쪽으로만 늘어남.
        /// Revit 내부/외부 면은 Orientation(외부면 방향)으로 정해져 그리는 방향마다 달라짐 → 고정할 쪽 법선과 내적으로 2·3 고름.
        /// Center거나 매개변수 읽기 전용이면 손대지 않음.
        /// 위치선 변경은 Revit이 벽 면을 읽어 계산 → Regenerate로 형상 만든 뒤 바꿈. 그래도 실패하면 중심선 그대로 두고 기록만(생성은 계속).
        /// </summary>
        private static void ApplyHoldFace(Document doc, Wall wall, LinearWallDefinition def)
        {
            if (def.HoldFace == WallHoldFace.Center) return;
            var keyRef = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            if (keyRef == null || keyRef.IsReadOnly) return;

            double dx = def.EndPoint.X - def.StartPoint.X;
            double dy = def.EndPoint.Y - def.StartPoint.Y;
            // 진행 방향 왼쪽 법선 (-dy, dx), 오른쪽은 반대
            var hold = def.HoldFace == WallHoldFace.Left ? new XYZ(-dy, dx, 0) : new XYZ(dy, -dx, 0);

            try
            {
                doc.Regenerate();
                // WallLocationLine: 2 = 마감면 외부, 3 = 마감면 내부
                keyRef.Set(hold.DotProduct(wall.Orientation) > 0
                    ? (int)WallLocationLine.FinishFaceExterior
                    : (int)WallLocationLine.FinishFaceInterior);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DHBIMWATER.Infrastructure.Logging.LogManager.Logger.Error(
                    $"벽 위치선 변경 실패(중심선 유지) ElementCode={def.ElementCode} Id={wall.Id.Value}: {ex.Message}");
            }
        }

        /// <summary>
        /// 프로파일 벽 Base Offset을 "프로파일 최저 Z − level.Elevation"으로 다시 씀. 슬래브(RevitSlabCommandRepo:70)와 같은 기준.
        /// 프로파일 점은 내부 원점 기준 절대 Z로 들어가고 Revit은 Base Offset을 내부 원점 기준 레벨 높이로 계산함.
        /// 레벨 Elevation 기준점(프로젝트 기준점/측량점)이 내부 원점과 다른 문서면 벽만 그 차이만큼 위아래로 밀림
        /// (2026-11-11 Base Offset -3000 사례). 슬래브·직선 벽은 level.Elevation 기준이라 안 밀림 → 같은 기준으로 맞춤.
        /// 차이 1mm 미만이면 손대지 않음.
        /// </summary>
        private static void AlignProfileWallBase(Document doc, Wall wall, ProfileWallDefinition def, long levelId)
        {
            if (doc.GetElement(new ElementId(levelId)) is not Level level) return;
            var offsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (offsetParam == null || offsetParam.IsReadOnly) return;

            double minZ = UC.MmToFt(def.Points.Min(p => p.Z));
            double expected = minZ - level.Elevation;
            if (Math.Abs(offsetParam.AsDouble() - expected) < UC.MmToFt(1)) return;

            offsetParam.Set(expected);
        }

        /// <summary>인스턴스 "구조" 체크(WALL_STRUCTURAL_SIGNIFICANT) 켬. Wall.Create structural 인자만으론 False로 남는 경우 있어 한 번 더 씀. 읽기 전용이면 건너뜀.</summary>
        private static void SetStructural(Wall wall)
        {
            var structural = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            if (structural is { IsReadOnly: false }) structural.Set(1);
        }

        /// <summary>벽 상부를 레벨+간격띄우기로 구속. 레벨 못 찾으면 경고 후 높이값 벽으로 남김(작업 중단 안 함).</summary>
        private void ApplyTopConstraint(Document doc, Wall wall, LinearWallDefinition def)
        {
            var topLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(level => level.Name == def.TopLevelName);
            if (topLevel == null)
            {
                _dialog.Warn("Error", $"벽 상부 레벨을 찾을 수 없습니다: {def.TopLevelName}\nElementCode: {def.ElementCode}");
                return;
            }

            wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(topLevel.Id);
            wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(UC.MmToFt(def.TopOffset));
        }
        #endregion
    }
}
