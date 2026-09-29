using DHBIMWATER.Core.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Core.Structures
{
    public record RectangularSlabOpeningDefinition
    {
        /// <summary>패밀리 파라미터 <c>W</c> — <b>프로젝트 X 방향</b> 치수 (2026-09-15 Revit 실검증).</summary>
        public double Width { get; set; }

        /// <summary>패밀리 파라미터 <c>L</c> — <b>프로젝트 Y 방향</b> 치수 (2026-09-15 Revit 실검증).</summary>
        public double Length { get; set; }
        public Point2D Position { get; set; }

        public string Name { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string HostElementCode { get; set; } = string.Empty;
        public string Category { get; set; } = "오프닝";
        public string ElementCode { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public string Part { get; set; } = string.Empty;
    }
}
