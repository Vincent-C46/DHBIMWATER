using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using System.Windows.Controls;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    public class RevitOpeningCommandRepo : IOpeningCommandRepo
    {
        private readonly Func<Document?> _doc;
        // 개구부마다 경고창 띄우던 것 대신 모음. 호출부가 TakeWarnings로 가져가 경고창 1번에 목록으로 보여줌.
        private readonly List<string> _warnings = [];

        public RevitOpeningCommandRepo(Func<Document?> doc)
        {
            _doc = doc;
        }

        public IReadOnlyList<string> TakeWarnings()
        {
            var warnings = _warnings.ToList();
            _warnings.Clear();
            return warnings;
        }

        /// <summary>경고 1건 = 1줄. 같은 문구(인스턴스마다 같은 패밀리 누락 등)는 한 번만.</summary>
        private void AddWarning(string message)
        {
            var line = message.Replace("\r", " ").Replace("\n", " ");
            if (!_warnings.Contains(line)) _warnings.Add(line);
        }
        public int CreateSlabOpening(RectangularSlabOpeningDefinition openingDef)
        {
            var doc = _doc();
            if (doc == null) return 0;

            // 슬래브 사각형 오프닝
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .FirstOrDefault(fs => fs.Name.Contains("바닥 개구부_사각형"));

            if (symbol == null)
            {
                AddWarning("패밀리 없음(개구부 생성 안 함): 바닥 개구부_사각형");
                return 0;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            XYZ position = new XYZ(UC.MmToFt(openingDef.Position.X), UC.MmToFt(openingDef.Position.Y), 0);

            var host = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .FirstOrDefault(f => f.LookupParameter("DH_ElementCode").AsValueString() == openingDef.HostElementCode);

            var hostLevel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == openingDef.LevelName);

            // 조용히 건너뛰면 개구부가 왜 없는지 알 수 없다. 둘 중 무엇이 없는지까지 알린다.
            if (host == null || hostLevel == null)
            {
                var missing = host == null
                    ? $"호스트 슬래브(DH_ElementCode = '{openingDef.HostElementCode}')"
                    : $"레벨 '{openingDef.LevelName}'";
                AddWarning($"개구부 생성 실패: {missing} 을(를) 찾지 못해 '{openingDef.ElementCode}' 을 만들지 않았습니다.");
                return 0;
            }

            var opening = doc.Create.NewFamilyInstance(position, symbol, host, hostLevel, StructuralType.NonStructural);
            // 패밀리는 회전 없이 배치되므로 W = 프로젝트 X, L = 프로젝트 Y 다(2026-09-15 Revit 실검증).
            opening.LookupParameter("W").Set(UC.MmToFt(openingDef.Width));
            opening.LookupParameter("L").Set(UC.MmToFt(openingDef.Length));

            //opening.LookupParameter("DH_ElementCode")?.Set(openingDef.ElementCode);
            opening.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            opening.LookupParameter("DH_Category")?.Set(openingDef.Category);
            opening.LookupParameter("DH_Part")?.Set(openingDef.Part);
            opening.LookupParameter("DH_HostElementCode")?.Set(openingDef.HostElementCode);
            opening.LookupParameter("DH_ElementCode")?.Set(openingDef.ElementCode);
            // 다른 부재(슬래브·벽체)는 Repo 에서 DH_Zone 을 찍는데 개구부만 빠져 있었다.
            // 존 기준 집계에서 개구부만 누락되므로 같이 기록한다(정의에 값이 없으면 기존과 같은 빈 문자열).
            opening.LookupParameter("DH_Zone")?.Set(openingDef.Zone);

            return (int)opening.Id.Value;
        }
        public void CreateSlabOpening(CircularSlabOpeningDefinition openingDef)
        {
            var doc = _doc();
            if (doc == null) return;

            var elementId = 0;

            // 슬래브 사각형 오프닝
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .FirstOrDefault(fs => fs.Name.Contains("바닥 개구부_원형"));

            if (symbol == null)
            {
                AddWarning("패밀리 없음(개구부 생성 안 함): 바닥 개구부_원형");
                return;
            }


            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            XYZ position = new XYZ(UC.MmToFt(openingDef.Position.X), UC.MmToFt(openingDef.Position.Y), 0);

            var host = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .FirstOrDefault(f => f.LookupParameter("DH_ElementCode").AsValueString() == openingDef.HostElementCode);
            var hostLevel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == openingDef.LevelName);

            if (host == null || hostLevel == null) return;

            var opening = doc.Create.NewFamilyInstance(position, symbol, host, hostLevel, StructuralType.NonStructural);
            opening.LookupParameter("D").Set(UC.MmToFt(openingDef.Diameter));

            opening.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            opening.LookupParameter("DH_Category")?.Set(openingDef.Category);
            opening.LookupParameter("DH_Part")?.Set(openingDef.Part);
            opening.LookupParameter("DH_HostElementCode")?.Set(openingDef.HostElementCode);
            opening.LookupParameter("DH_ElementCode")?.Set(openingDef.ElementCode);

            return;
        }
        public void CreateWallOpening(RectangularWallOpeningDefinition openingDef)
        {
            var doc = _doc();
            if (doc == null) return;

            // 슬래브 사각형 오프닝
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .FirstOrDefault(fs => fs.Name.Contains("벽 개구부_사각형"));

            if (symbol == null)
            {
                AddWarning("패밀리 없음(개구부 생성 안 함): 벽 개구부_사각형");
                return;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            XYZ position = new XYZ(UC.MmToFt(openingDef.Position.X), UC.MmToFt(openingDef.Position.Y), UC.MmToFt(openingDef.Position.Z));

            var hosts = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(f => f.LookupParameter("DH_ElementCode").AsValueString() == openingDef.HostElementCode)
                    .ToList();

            if (hosts.Count == 0) return;

            var hostLevel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == openingDef.LevelName);

            if (hostLevel == null) return;

            foreach (var host in hosts)
            {
                var opening = doc.Create.NewFamilyInstance(position, symbol, host, hostLevel, StructuralType.NonStructural);
                opening.LookupParameter("W").Set(UC.MmToFt(openingDef.Width));
                opening.LookupParameter("H").Set(UC.MmToFt(openingDef.Height));
                opening.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM).Set(UC.MmToFt(openingDef.OffsetZ));

                opening.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
                opening.LookupParameter("DH_Category")?.Set(openingDef.Category);
                opening.LookupParameter("DH_Part")?.Set(openingDef.Part);
                opening.LookupParameter("DH_HostElementCode")?.Set(openingDef.HostElementCode);
                opening.LookupParameter("DH_ElementCode")?.Set(openingDef.ElementCode);

            }
            return;
        }
        public void CreateWallOpening(CircularWallOpeningDefinition openingDef)
        {
            var doc = _doc();
            if (doc == null) return;

            // 슬래브 사각형 오프닝
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .FirstOrDefault(fs => fs.Name.Contains("벽 개구부_원형"));

            if (symbol == null)
            {
                AddWarning("패밀리 없음(개구부 생성 안 함): 벽 개구부_원형");
                return;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            XYZ position = new XYZ(UC.MmToFt(openingDef.Position.X), UC.MmToFt(openingDef.Position.Y), UC.MmToFt(openingDef.Position.Z));

            // 같은 DH_ElementCode 를 단 벽이 여럿일 수 있다(토출조 외벽은 네 변이 전부 DB-W1).
            // 첫 번째를 집으면 엉뚱한 벽이 뚫리므로 개구부 위치에서 가장 가까운 벽을 고른다.
            // 코드가 유일한 기존 경로(펌프장 W2·W4)에서는 후보가 하나라 결과가 같다.
            var host = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(f => f.LookupParameter("DH_ElementCode").AsValueString() == openingDef.HostElementCode)
                    .OrderBy(w => DistanceToWall(w, position))
                    .FirstOrDefault();

            var hostLevel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == openingDef.LevelName);
            if (host == null || hostLevel == null)
            {
                var missing = host == null
                    ? $"호스트 벽체(DH_ElementCode = '{openingDef.HostElementCode}')"
                    : $"레벨 '{openingDef.LevelName}'";
                AddWarning($"개구부 생성 실패: {missing} 을(를) 찾지 못해 '{openingDef.ElementCode}' 을 만들지 않았습니다.");
                return;
            }

            var opening = doc.Create.NewFamilyInstance(position, symbol, host, hostLevel, StructuralType.NonStructural);
            opening.LookupParameter("D").Set(UC.MmToFt(openingDef.Diameter));
            opening.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM).Set(UC.MmToFt(openingDef.OffsetZ));

            opening.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            opening.LookupParameter("DH_Category")?.Set(openingDef.Category);
            opening.LookupParameter("DH_Part")?.Set(openingDef.Part);
            opening.LookupParameter("DH_HostElementCode")?.Set(openingDef.HostElementCode);
            opening.LookupParameter("DH_ElementCode")?.Set(openingDef.ElementCode);
            // 슬래브 개구부와 같은 이유로 존도 기록한다(정의에 값이 없으면 기존과 같은 빈 문자열).
            opening.LookupParameter("DH_Zone")?.Set(openingDef.Zone);

            return;
        }

        /// <summary>벽 위치선과 점 사이의 평면 거리(ft). 위치선이 없으면 후보에서 밀어낸다.</summary>
        private static double DistanceToWall(Wall wall, XYZ point)
        {
            if (wall.Location is not LocationCurve { Curve: { } curve }) return double.MaxValue;

            var flat = new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z);
            return curve.Distance(flat);
        }
    }
}
