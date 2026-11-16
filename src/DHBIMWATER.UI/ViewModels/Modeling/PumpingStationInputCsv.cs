using System.Globalization;
using System.Reflection;
using System.Text;

namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 펌프장·토출조 입력값 CSV 내보내기·가져오기. 배수지 창 CSV와 같은 열(매개변수명,설명,값,단위,비고).
    /// 행 = 저장본(<see cref="PumpingStationInputSnapshot"/>) 속성 그대로 — 저장본에 칸 추가하면 CSV에도 자동 반영.
    /// 토출조 행은 "토출조_" 접두, 덮어쓴 권고안은 "토출조_권고_{키}".
    /// 가져오기: 없는 행은 현재 값 유지(저장본 null 규칙과 같음). 값 못 읽으면 errors에 남기고 그 칸만 건너뜀.
    /// </summary>
    internal static class PumpingStationInputCsv
    {
        public const string Header = "매개변수명,설명,값,단위,비고";
        private const string BasinPrefix = "토출조_";
        private const string OverridePrefix = "토출조_권고_";

        // 설명·단위. 없으면 빈칸(값 읽기엔 영향 없음)
        private static readonly Dictionary<string, (string Label, string Unit)> PumpLabels = new()
        {
            ["PumpingStationType"] = ("펌프장 타입", ""), ["EntranceType"] = ("진입부 위치", ""),
            ["D"] = ("펌프 구경", "mm"), ["HD"] = ("양정고", "m"), ["N"] = ("펌프 대수", "대"),
            ["LWL"] = ("저수위 (EL)", "m"), ["HWL"] = ("고수위 (EL)", "m"),
            ["HasCheckValve"] = ("추가역지밸브 적용", "TRUE/FALSE"),
            ["SupportBlockWidth"] = ("펌프 받침 폭", "mm"), ["SupportBlockHeight"] = ("펌프 받침 높이", "mm"),
            ["IsRectangularOpening"] = ("펌프 오프닝 사각형", "TRUE/FALSE"),
            ["ValveBaseWidth"] = ("밸브받침 폭", "mm"), ["ValveBaseLength"] = ("밸브받침 길이", "mm"),
            ["ValveBaseHeight"] = ("밸브받침 높이", "mm"), ["ValveBasePlacement"] = ("밸브받침 배치", "mm"),
            ["B1"] = ("제진기 관리공간 폭", "mm"), ["B2"] = ("제진기 오프닝 길이", "mm"), ["B3"] = ("제진기 오프닝~건축격벽", "mm"),
            ["B4"] = ("펌프 유지관리 공간 폭", "mm"), ["B5"] = ("펌프 오프닝 사이즈", "mm"), ["B6"] = ("돌출부 길이", "mm"),
            ["B7"] = ("밸브실 폭", "mm"), ["B7Base"] = ("밸브실 폭 입력값(계단 최소 적용 전)", "mm"),
            ["H1"] = ("제진기 최소 운전 수위", "mm"), ["H3"] = ("여유고", "mm"), ["H4"] = ("펌프 흡입관 잠김 깊이", "mm"),
            ["H5"] = ("H5", "mm"), ["H6"] = ("밸브받침 높이", "mm"), ["H7"] = ("최소 토피", "mm"),
            ["Theta"] = ("기초 경사부 기울기", "°"),
            ["L1"] = ("슬래브 단부 길이1", "mm"), ["L2"] = ("슬래브 단부 길이2", "mm"), ["L3"] = ("기초 경사부 길이", "mm"),
            ["L4"] = ("계단 끝~펌프 중심", "mm"), ["OB1"] = ("벽체 오프닝 폭", "mm"), ["OH1"] = ("벽체 오프닝 높이", "mm"),
            ["NS"] = ("기초 경사부 계단 단수", "단"), ["HS"] = ("계단 높이", "mm"),
            ["NS1"] = ("밸브실 계단 단수", "단"), ["HS1"] = ("밸브실 계단 높이", "mm"),
            ["B8"] = ("제진기 오프닝 폭", "mm"), ["B9"] = ("진입부 폭", "mm"), ["B10"] = ("Toe 길이", "mm"), ["L5"] = ("진입부 길이", "mm"),
            ["T1"] = ("상부슬래브 두께", "mm"), ["T2"] = ("기초슬래브 두께", "mm"), ["T3"] = ("중간슬래브 두께", "mm"),
            ["T4"] = ("외벽 두께", "mm"), ["T5"] = ("진입부 내벽 두께", "mm"), ["T5Prime"] = ("밸브실 하부슬래브 두께", "mm"),
            ["T6"] = ("와류방지벽 두께", "mm"), ["GB1"] = ("거더 폭", "mm"), ["GH1"] = ("거더 높이", "mm"),
            ["HB1"] = ("밸브실 하부 헌치 폭", "mm"), ["HH1"] = ("밸브실 하부 헌치 높이", "mm"),
        };

        private static readonly Dictionary<string, (string Label, string Unit)> BasinLabels = new()
        {
            ["Position"] = ("토출조 토출 위치", ""), ["Step"] = ("토출조 단차", ""), ["Conduit"] = ("토출관로 형식", ""),
            ["TA"] = ("토출조 H.W.L (EL)", "m"), ["Separation"] = ("펌프장과 이격거리", "m"), ["ON"] = ("토출관로 련수", "련"),
            ["OB"] = ("토출암거 폭", "mm"), ["OH"] = ("토출암거 높이", "mm"), ["OD"] = ("토출관 직경", "mm"),
        };

        private static IEnumerable<PropertyInfo> PumpProperties => typeof(PumpingStationInputSnapshot).GetProperties()
            .Where(p => p.Name is not (nameof(PumpingStationInputSnapshot.Version) or nameof(PumpingStationInputSnapshot.DischargeBasin)));

        private static IEnumerable<PropertyInfo> BasinProperties => typeof(DischargeBasinInputSnapshot).GetProperties()
            .Where(p => p.Name != nameof(DischargeBasinInputSnapshot.Overrides));

        public static string ToCsv(PumpingStationInputSnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var p in PumpProperties)
                AppendRow(sb, p.Name, PumpLabels, p.Name, p.GetValue(s));
            if (s.DischargeBasin is { } b)
            {
                foreach (var p in BasinProperties)
                    AppendRow(sb, BasinPrefix + p.Name, BasinLabels, p.Name, p.GetValue(b));
                foreach (var (key, value) in b.Overrides ?? new())
                    sb.AppendLine(string.Join(",", Escape(OverridePrefix + key), Escape($"토출조 권고안 {key} (직접 수정값)"),
                        Format(value), "", "지우면 권고안 계산값 사용"));
            }
            return sb.ToString();
        }

        /// <summary>CSV 줄 → 저장본. 없는 행 = null(현재 값 유지). 토출조 행이 하나라도 있으면 토출조 저장본 만듦
        /// (그때 권고안 행 없으면 덮어쓴 값 모두 해제).</summary>
        public static PumpingStationInputSnapshot FromCsv(IEnumerable<string> lines, List<string> errors)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Contains('\t') ? line.Split('\t').ToList() : ParseLine(line);
                if (cells.Count >= 3 && !string.IsNullOrWhiteSpace(cells[0]))
                    values[cells[0].Trim()] = cells[2].Trim();
            }

            var s = new PumpingStationInputSnapshot();
            foreach (var p in PumpProperties)
                if (values.TryGetValue(p.Name, out var text)) SetValue(s, p, p.Name, text, errors);

            if (values.Keys.Any(k => k.StartsWith(BasinPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var b = new DischargeBasinInputSnapshot { Overrides = new() };
                foreach (var p in BasinProperties)
                    if (values.TryGetValue(BasinPrefix + p.Name, out var text)) SetValue(b, p, BasinPrefix + p.Name, text, errors);
                foreach (var (code, text) in values.Where(kv => kv.Key.StartsWith(OverridePrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (TryParseDouble(text, out var v)) b.Overrides[code.Substring(OverridePrefix.Length)] = v;
                    else errors.Add($"{code} 값 파싱 실패: {text}");
                }
                s.DischargeBasin = b;
            }
            return s;
        }

        private static void AppendRow(StringBuilder sb, string code, Dictionary<string, (string Label, string Unit)> labels, string key, object? value)
        {
            if (value is null) return;
            var (label, unit) = labels.TryGetValue(key, out var l) ? l : (key, "");
            sb.AppendLine(string.Join(",", Escape(code), Escape(label), Escape(Format(value)), Escape(unit), ""));
        }

        private static void SetValue(object target, PropertyInfo p, string code, string text, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object? value = null;
            if (type == typeof(string)) value = text;
            else if (type == typeof(double) && TryParseDouble(text, out var d)) value = d;
            else if (type == typeof(int) && TryParseDouble(text, out var i) && i == Math.Floor(i)) value = (int)i;
            else if (type == typeof(bool) && TryParseBool(text, out var flag)) value = flag;

            if (value is null) errors.Add($"{code} 값 파싱 실패: {text}");
            else p.SetValue(target, value);
        }

        private static string Format(object value) => value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "TRUE" : "FALSE",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        // 엑셀 저장 시 천 단위 쉼표(예 "1,500")도 받음
        private static bool TryParseDouble(string text, out double value)
            => double.TryParse(text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static bool TryParseBool(string text, out bool value)
        {
            switch (text.Trim().ToUpperInvariant())
            {
                case "TRUE" or "1" or "Y" or "YES" or "예": value = true; return true;
                case "FALSE" or "0" or "N" or "NO" or "아니오": value = false; return true;
                default: value = false; return false;
            }
        }

        private static string Escape(string value)
            => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";

        private static List<string> ParseLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }
}
