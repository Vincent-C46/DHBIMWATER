using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Structures
{
    public record GenericModelPlacementDefinition
    {
        public string SymbolName { get; set; } = string.Empty;
        public Point3D Origin { get; set; } = new Point3D(0, 0, 0);
        public string LevelName { get; set; } = string.Empty;
        public double Rotation { get; set; } = 0.0;
        public string Category { get; set; } = "일반 모델";
        public string ElementCode { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public string Part { get; set; } = string.Empty;

        // true면 비구조로 배치. 기본 false = 기존 Footing 배치 유지(docs/289 § G)
        public bool IsNonStructural { get; set; }
        // 레벨로부터 높이(mm). 값 있으면 배치 후 INSTANCE_ELEVATION_PARAM 설정. null이면 안 건드림
        public double? ElevationFromLevel { get; set; }

        // 패밀리별 인스턴스 매개변수 (Text: string, Length: double, Number: Dimensionless, YesNo: int)
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}

