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

            WallUtils.DisallowWallJoinAtEnd(wall, 0);
            WallUtils.DisallowWallJoinAtEnd(wall, 1);

            // 상부 레벨 구속. TopLevelName 비면 기존 높이값 벽 그대로(docs/289 § B)
            if (!string.IsNullOrEmpty(linearWallDefinition.TopLevelName))
                ApplyTopConstraint(doc, wall, linearWallDefinition);

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

            WallUtils.DisallowWallJoinAtEnd(profileWall, 0);
            WallUtils.DisallowWallJoinAtEnd(profileWall, 1);

            return (int)(profileWall.Id.Value);
        }
        #endregion

        #region Private Helpers
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
