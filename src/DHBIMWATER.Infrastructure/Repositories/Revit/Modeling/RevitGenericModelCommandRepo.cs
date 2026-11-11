using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    internal class RevitGenericModelCommandRepo : IGenericModelCommandRepo
    {
        private readonly Func<Document?> _doc;
        // 인스턴스마다 경고창 띄우던 것 대신 모음. 호출부가 TakeWarnings로 가져가 경고창 1번에 목록으로 보여줌.
        private readonly List<string> _warnings = [];

        public RevitGenericModelCommandRepo(Func<Document?> doc)
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

        public int PlaceInstance(GenericModelPlacementDefinition def, long levelId, int symbolId)
        {
            Document? doc = _doc();
            if (doc == null) return 0;

            FamilySymbol? symbol = doc.GetElement(new ElementId((long)symbolId)) as FamilySymbol;

            if (symbol == null)
            {
                AddWarning($"패밀리 없음(배치 안 함): {def.SymbolName}");
                return 0;
            }

            Level? level = doc.GetElement(new ElementId(levelId)) as Level;

            if (level == null)
            {
                AddWarning($"레벨을 찾을 수 없습니다(배치 안 함): {def.LevelName}");
                return 0;
            }

            if (!symbol.IsActive) symbol.Activate();

            var origin = new XYZ(UC.MmToFt(def.Origin.X),
                                 UC.MmToFt(def.Origin.Y),
                                 UC.MmToFt(def.Origin.Z));

            // 배수지 단차버림·피트는 레거시처럼 비구조 배치. 기본은 기존 Footing 유지(docs/289 § G)
            FamilyInstance instance = doc.Create.NewFamilyInstance(
                origin, symbol, level, def.IsNonStructural ? StructuralType.NonStructural : StructuralType.Footing);

            if (instance == null)
            {
                AddWarning($"패밀리 인스턴스가 배치되지 않았습니다: {def.SymbolName}");
                return 0;
            }

            if (instance != null && def.ElevationFromLevel.HasValue)
            {
                var elevation = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                if (elevation == null || elevation.IsReadOnly)
                    AddWarning($"레벨로부터 높이를 설정할 수 없습니다: {def.SymbolName}");
                else
                    elevation.Set(UC.MmToFt(def.ElevationFromLevel.Value));
            }

            if (def.Rotation != 0.0)
            {
                var axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, instance.Id, axis, UC.DegToRad(def.Rotation)); // CCW
            }

            instance.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
            instance.LookupParameter("DH_Category")?.Set(def.Category);
            instance.LookupParameter("DH_ElementCode")?.Set(def.ElementCode);
            if (!string.IsNullOrEmpty(def.Class))
                instance.LookupParameter("DH_Class")?.Set(def.Class);
            instance.LookupParameter("DH_Part")?.Set(def.Part);
            instance.LookupParameter("DH_Zone")?.Set(def.Zone);

            foreach (var (name, value) in def.Parameters)
            {
                var param = instance.LookupParameter(name);
                if (param == null) continue;

                switch (value)
                {
                    case string s:       param.Set(s); break;
                    case Dimensionless n: param.Set(n.Value); break;
                    case double d:       param.Set(UC.MmToFt(d)); break;
                    case int i:          param.Set(i); break;
                    default: break;
                }
            }
            return (int)instance.Id.Value;
        }
    }
}

