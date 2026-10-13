using DHBIMWATER.Core.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Core.Structures
{
    public record LinearWallDefinition
    {
        public Point3D StartPoint { get; set; }
        public Point3D EndPoint { get; set; }
        public double Thickness { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public double Height { get; set; }
        public double BaseOffset { get; set; }
        // 상부 구속 레벨 이름. 비면 높이값(미연결) 벽 그대로 — 기존 호출부 동작 유지(docs/289 § B)
        public string TopLevelName { get; set; } = string.Empty;
        // 상부 간격띄우기(mm). TopLevelName 있을 때만 씀
        public double TopOffset { get; set; }
        public bool IsFlipped { get; set; } = false;
        public bool IsExterior { get; set; } = false;

        public string Category { get; set; } = "벽";
        public string ElementCode { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public string Part { get; set; } = string.Empty;

        public Curve3D WallCurve
        {
            get { return new Line3D(StartPoint, EndPoint); }
        }
    }
}
