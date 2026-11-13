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
        // 끝 결합 허용. 기본 false = Repo가 양 끝 결합 끊음(기존 동작). 사선 코너처럼 길이 맞춤으로 못 메우는 끝만 켬
        public bool JoinAtStart { get; set; } = false;
        public bool JoinAtEnd { get; set; } = false;
        // 위치선으로 고정할 면. Center = 벽 중심선(기존 동작). Left/Right = 커브 진행 방향 기준 그쪽 마감면 고정 →
        // Revit에서 두께 바꿔도 그 면은 안 움직이고 반대쪽으로만 늘어남(docs/325 § F)
        public WallHoldFace HoldFace { get; set; } = WallHoldFace.Center;

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

namespace DHBIMWATER.Core.Structures
{
    /// <summary>벽 위치선으로 고정할 면. 커브 진행 방향 기준 왼쪽·오른쪽 마감면.</summary>
    public enum WallHoldFace { Center, Left, Right }
}
