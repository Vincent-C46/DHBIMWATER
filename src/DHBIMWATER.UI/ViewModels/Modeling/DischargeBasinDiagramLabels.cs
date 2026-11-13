using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>토출조 도해 글자 영역 — 클릭 위치 역조회. 경로표(Sheets)는 DischargeBasinDiagramLabels.g.cs(자동 생성).</summary>
    internal static partial class DischargeBasinDiagramLabels
    {
        /// <summary>도해 좌표(1800폭 단위) 점을 덮는 매개변수. 없으면 null. 도해 클릭 → 입력칸 포커스용.</summary>
        public static string? FindAt(string sheet, Point point)
        {
            if (!Sheets.TryGetValue(sheet, out var labels)) return null;
            foreach (var (parameter, path) in labels)
                if (Parsed(path).FillContains(point)) return parameter;
            return null;
        }

        // MouseMove마다 불려 경로 파싱 결과 캐시. UI 스레드만 씀
        private static readonly Dictionary<string, Geometry> Cache = new();

        private static Geometry Parsed(string path)
        {
            if (!Cache.TryGetValue(path, out var geometry))
                Cache[path] = geometry = Geometry.Parse(path);
            return geometry;
        }
    }
}
