using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 펌프장 도해(PNG) 매개변수 글자 영역. 좌표 = PNG 픽셀(종단 2480×1458, 평면 2480×2027/1404).
    /// 토출조(DischargeBasinDiagramLabels.g.cs)와 같은 Path 경로 문자열. PNG는 그대로, 글자 위치만 실측.
    /// 경로표(Sheets)는 PumpingStationDiagramLabels.g.cs — tools/generate_pump_diagram_labels.py 가 생성.
    /// 시트 이름 = "종단-Type1/2/3", "평면-좌안부/우안부/측면부".
    /// </summary>
    internal static partial class PumpingStationDiagramLabels
    {
        /// <summary>시트·매개변수의 강조 경로. 없으면 null.</summary>
        public static string? Find(string sheet, string parameter)
            => Sheets.TryGetValue(sheet, out var labels) && labels.TryGetValue(parameter, out var path) ? path : null;

        /// <summary>도해 좌표(PNG 픽셀) 점을 덮는 매개변수. 없으면 null. 도해 클릭 → 입력칸 포커스용.</summary>
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
