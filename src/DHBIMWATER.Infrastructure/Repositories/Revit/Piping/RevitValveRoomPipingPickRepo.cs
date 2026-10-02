using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping
{
    /// <summary>
    /// Revit 뷰에서 이미 모델링된 배관 부속류·밸브류를 피킹해 캔버스 배관망으로 되돌린다(docs/158 § C).
    /// 반드시 Revit API 컨텍스트(ExternalEvent)에서 호출해야 한다.
    /// <para>여기서는 <b>실측과 판정만</b> 한다. 부재를 엣지로 합치는 계산은 순수 Core
    /// (<see cref="PipeNetworkReconstructor"/>)가 맡는다.</para>
    /// <para>판정 규칙은 재기록 경로(<c>RevitPipeMaterialBackfillRepo.Describe</c>, docs/136)와 같다.
    /// 그쪽을 건드리면 회귀 위험이 있어 일부러 복제했다 — <see cref="PipeMaterialConnectorReader"/>가
    /// 같은 이유로 이미 복제를 선택했다. 규칙이 바뀌면 두 곳을 함께 고쳐야 한다.</para>
    /// </summary>
    internal sealed class RevitValveRoomPipingPickRepo : IValveRoomPipingPickRepo
    {
        /// <summary>연결망 확장 상한. 잘못 고른 한 점에서 문서 전체 관망으로 번지는 것을 막는다.</summary>
        private const int MaxParts = 2000;

        private readonly Func<UIDocument?> _uiDoc;

        public RevitValveRoomPipingPickRepo(Func<UIDocument?> uiDoc)
        {
            _uiDoc = uiDoc;
        }

        public PipeLayoutImport? PickPiping()
        {
            // 결과 구분(docs/239 § C): 문서 없음 = SYS-101 예외 / Esc = null / 배관 0개 = 빈 결과. 종전에는 셋 다 null("취소")이었다.
            var uiDoc = _uiDoc() ?? throw new DHBIMWATER.Shared.Errors.DhbimwaterException(
                DHBIMWATER.Shared.Errors.ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

            IList<Reference> references;
            try
            {
                references = uiDoc.Selection.PickObjects(ObjectType.Element, new PipingSelectionFilter(),
                    "캔버스로 가져올 배관 부속류·밸브류를 선택한 뒤 완료를 누르세요");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;    // 사용자가 ESC로 취소
            }

            var seeds = references
                .Select(x => uiDoc.Document.GetElement(x))
                .OfType<FamilyInstance>()
                .Where(IsPipingInstance)
                .ToList();
            if (seeds.Count == 0) return PipeLayoutImport.Empty;

            var (instances, truncated) = ExpandThroughConnectors(seeds);
            var import = PipeNetworkReconstructor.Build(instances.Select(Describe).ToList());
            if (!truncated) return import;

            return import with
            {
                Warnings = import.Warnings
                    .Append($"연결망이 {MaxParts}개를 넘어 거기까지만 가져왔습니다. 필요한 구간을 나눠서 선택하세요.")
                    .ToList()
            };
        }

        /// <summary>선택한 부재에서 커넥터를 타고 이어진 배관 부속류·이음쇠를 전부 모은다(docs/158 § C-2).</summary>
        private static (List<FamilyInstance> Instances, bool Truncated) ExpandThroughConnectors(IReadOnlyList<FamilyInstance> seeds)
        {
            var visited = new Dictionary<long, FamilyInstance>();
            var queue = new Queue<FamilyInstance>(seeds);
            var truncated = false;

            while (queue.Count > 0)
            {
                var instance = queue.Dequeue();
                if (visited.ContainsKey(instance.Id.Value)) continue;
                if (visited.Count >= MaxParts) { truncated = true; break; }
                visited[instance.Id.Value] = instance;

                foreach (var connector in PipeMaterialConnectorReader.EndConnectors(instance))
                    foreach (var peer in ConnectedInstances(instance, connector))
                        if (!visited.ContainsKey(peer.Id.Value))
                            queue.Enqueue(peer);
            }
            return (visited.Values.ToList(), truncated);
        }

        private static IEnumerable<FamilyInstance> ConnectedInstances(FamilyInstance owner, Connector connector)
        {
            foreach (Connector reference in connector.AllRefs)
            {
                if (reference.Owner is not FamilyInstance peer) continue;
                if (peer.Id.Value == owner.Id.Value) continue;
                if (!IsPipingInstance(peer)) continue;
                yield return peer;
            }
        }

        /// <summary>모집단 — 배관 이음쇠·배관 밸브류 패밀리 인스턴스. `DH_Addin` 태그는 보지 않는다(docs/158 확정 1).</summary>
        private static bool IsPipingInstance(Element element)
            => element is FamilyInstance
            && element.Category is { } category
            && (category.Id.Value == (long)BuiltInCategory.OST_PipeFitting
                || category.Id.Value == (long)BuiltInCategory.OST_PipeAccessory);

        /// <summary>인스턴스 하나를 실측해 순수 DTO로 바꾼다(docs/158 § C-3, § C-4).</summary>
        private static PickedPipePart Describe(FamilyInstance instance)
        {
            var connectors = PipeMaterialConnectorReader.EndConnectors(instance);
            var (role, skipReason) = Classify(instance, connectors);
            // 모델에 놓인 A의 파라미터·커넥터를 남긴다 — 배치 프로브(BypassSet.ProbeFailure)의 로그와 나란히 비교한다(docs/241).
            if (PipeValveBend.IsValveTeeConnectorCount(connectors.Count) && PipeValveBend.IsValveBendFamily(instance.Symbol.Family.Name))
                LogValveTeeDiagnostics(instance, connectors, role, skipReason);
            // 밸브부관은 비대칭이라 어느 커넥터가 밸브 쪽인지가 곧 방향이다. 배치 경계와 같은 규약(긴 레그 = 밸브 쪽, docs/247)으로
            // 순서를 고정해 Connectors[0]을 밸브 쪽으로 넘긴다(docs/225 § E).
            if (role == PipePartRole.ValveBend) connectors = PipeMaterialConnectorReader.ValveBendValveSideFirst(connectors);
            // 밸브부관 A는 수직분기 커넥터가 가시성과 무관하게 남는다. 캔버스는 평면이라 수평 3개(주관 2·수평분기)만 넘겨
            // T형과 같은 절점 계산을 쓴다(docs/227 § E). 수직분기 A는 범위 밖이다.
            if (role == PipePartRole.ValveTee) connectors = connectors.Where(x => !IsVerticalConnector(x)).ToList();
            var origin = (instance.Location as LocationPoint)?.Point;

            return new PickedPipePart(
                instance.Id.Value,
                role,
                // 캔버스 콤보·재배치가 쓰는 "패밀리명 : 타입명" 형식과 글자 하나까지 같아야 한다
                // (RevitElementTypeQueryRepo.GetFamilySymbolNames, RevitPipeSegmentCommandRepo.ResolveSymbol).
                $"{instance.Symbol.Family.Name} : {instance.Symbol.Name}",
                origin is null ? AverageConnectorPoint(connectors) : ToPoint2D(origin),
                origin is null ? 0 : UC.FtToMm(origin.Z),
                connectors.Select(ToPickedConnector).ToList(),
                skipReason,
                // 이 부재에 이미 찍혀 있는 밸브실 유형. 가져오기 왕복에서 잃지 않게 함께 싣는다(docs/161 § G-4).
                instance.LookupParameter(ValvePipingParameters.Zone)?.AsString() ?? string.Empty,
                // 관종도 같은 이유로 싣는다. 이게 없으면 재생성이 창 기본값으로 DH_관종을 덮어쓴다(docs/170 § G).
                instance.LookupParameter(ValvePipingParameters.Material)?.AsString() ?? string.Empty);
        }

        /// <summary>부재 종류 판정. 규칙은 docs/136과 같다 — 밸브류는 무조건 부속,
        /// 이음쇠는 커넥터 2개의 편각으로 관·곡관을 가르고 3개면 T형이다.</summary>
        private static (PipePartRole Role, string? SkipReason) Classify(FamilyInstance instance, IReadOnlyList<Connector> connectors)
        {
            if (connectors.Count == 0) return (PipePartRole.Unknown, "배관 커넥터가 없습니다");
            // 밸브부관은 커넥터 수·편각만 보면 곡관·십자로 잡힌다. 카테고리·커넥터 수 판정보다 먼저 패밀리명 키워드로 뗀다(docs/225 § E).
            if (connectors.Count == 2 && PipeValveBend.IsValveBendFamily(instance.Symbol.Family.Name)
                && PipeMaterialSpecText.DeflectionDeg(PipeMaterialConnectorReader.OutwardDot(connectors[0], connectors[1]))
                    > PipeMaterialSpecText.StraightDeflectionToleranceDeg)
                return (PipePartRole.ValveBend, null);
            // 밸브부관 A — 같은 키워드에 커넥터 4개(주관 2·수평분기·수직분기, docs/227 § 0-6). 수평 커넥터가 3개가 아니면
            // 수직분기로 쓰인 것이라 평면 캔버스로 되돌릴 수 없다.
            if (PipeValveBend.IsValveTeeConnectorCount(connectors.Count) && PipeValveBend.IsValveBendFamily(instance.Symbol.Family.Name))
                return connectors.Count(x => !IsVerticalConnector(x)) == 3
                    ? (PipePartRole.ValveTee, null)
                    : (PipePartRole.Unknown, "밸브부관 A의 수평 커넥터가 3개가 아닙니다(수직분기는 지원하지 않습니다)");
            if (instance.Category?.Id.Value == (long)BuiltInCategory.OST_PipeAccessory) return (PipePartRole.Accessory, null);

            // 이음관은 직관과 같은 이음쇠 카테고리이고 편각도 0°라 편각 판정으로는 직관과 구분되지 않는다.
            // 패밀리명을 편각보다 먼저 본다 — 그러지 않으면 관 패밀리 최빈값 투표까지 오염된다(docs/167 § F-1).
            if (connectors.Count == 2 && InlineJoint.IsJointFamily(instance.Symbol.Family.Name))
                return (PipePartRole.Joint, null);

            return connectors.Count switch
            {
                2 => PipeMaterialSpecText.DeflectionDeg(PipeMaterialConnectorReader.OutwardDot(connectors[0], connectors[1]))
                        > PipeMaterialSpecText.StraightDeflectionToleranceDeg
                    ? (PipePartRole.Bend, (string?)null)
                    : (PipePartRole.Segment, (string?)null),
                // 3구는 분기각으로 T형·Y형을 가른다(docs/187 § 5-1). 재기록(Describe)과 같은 규칙이다.
                3 => (IsWye(connectors) ? PipePartRole.Wye : PipePartRole.Tee, null),
                // 십자(docs/175 § D-3). 재기록(RevitPipeMaterialBackfillRepo.Describe)과 같은 규칙이다.
                4 => (PipePartRole.Cross, null),
                _ => (PipePartRole.Unknown, $"배관 커넥터가 {connectors.Count}개라 부재 종류를 판정할 수 없습니다")
            };
        }

        /// <summary>커넥터 3개짜리 이음쇠가 Y형인지 — 분기 커넥터가 주관과 이루는 각이 30~60°면 Y형이다.
        /// 판정 상수는 <see cref="PipeWyeResolver"/>의 것을 쓴다(docs/187 § 5-1).</summary>
        private static bool IsWye(IReadOnlyList<Connector> connectors)
        {
            if (connectors.Count != 3) return false;
            // 주관 2개는 서로 마주보므로(편각 0°) 내적이 -1에 가장 가까운 짝이다. 남은 하나가 분기다.
            var pairs = new[] { (A: 0, B: 1, Branch: 2), (A: 0, B: 2, Branch: 1), (A: 1, B: 2, Branch: 0) };
            var main = pairs.OrderBy(x => PipeMaterialConnectorReader.OutwardDot(connectors[x.A], connectors[x.B])).First();
            var branchAngleDeg = PipeWyeResolver.BranchAngleDegFromDots(
                PipeMaterialConnectorReader.OutwardDot(connectors[main.Branch], connectors[main.A]),
                PipeMaterialConnectorReader.OutwardDot(connectors[main.Branch], connectors[main.B]));
            return PipeWyeResolver.IsWyeBranchAngle(branchAngleDeg);
        }

        /// <summary>되가져오기 진단(docs/241). 밸브부관 A의 유형·분기/구경 관련 파라미터와 커넥터를 로그 `BYPASS_DIAG`에 남긴다.
        /// 커넥터 좌표는 인스턴스 로컬 좌표(mm)라 배치 프로브(원점·회전 0) 로그와 그대로 비교된다. 로깅 실패는 가져오기를 막지 않는다.</summary>
        private static void LogValveTeeDiagnostics(FamilyInstance instance, IReadOnlyList<Connector> connectors, PipePartRole role, string? skipReason)
        {
            try
            {
                var log = new System.Text.StringBuilder();
                log.AppendLine($"BYPASS_DIAG 되가져오기 밸브부관 A '{instance.Symbol.Family.Name} : {instance.Symbol.Name}' (ID {instance.Id.Value}) — 판정 {role}"
                    + (skipReason is null ? string.Empty : $", {skipReason}"));
                foreach (var (scope, parameters) in new[] { ("인스턴스", instance.Parameters), ("유형", instance.Symbol.Parameters) })
                {
                    foreach (var parameter in parameters.Cast<Parameter>()
                                 .Where(x => x.Definition?.Name is { } name && (name.Contains("분기") || name.Contains("구경") || name.Contains("주관")))
                                 .OrderBy(x => x.Definition.Name))
                        log.AppendLine($"{scope} '{parameter.Definition.Name}' = {DiagnosticValue(parameter)}");
                }
                var toLocal = instance.GetTransform().Inverse;
                log.AppendLine($"배관 커넥터 {connectors.Count}개(인스턴스 로컬 mm, 바깥 방향):");
                foreach (var connector in connectors.OrderBy(x => x.Id))
                {
                    var o = toLocal.OfPoint(connector.Origin);
                    var d = toLocal.OfVector(connector.CoordinateSystem.BasisZ);
                    log.AppendLine(FormattableString.Invariant(
                        $"  #{connector.Id} 위치({UC.FtToMm(o.X):0.#}, {UC.FtToMm(o.Y):0.#}, {UC.FtToMm(o.Z):0.#}) 방향({d.X:0.###}, {d.Y:0.###}, {d.Z:0.###})")
                        + (IsVerticalConnector(connector) ? " [수직]" : "")
                        + (connector.IsConnected ? " [연결됨]" : ""));
                }
                DHBIMWATER.Infrastructure.Logging.LogManager.Logger.Warn(log.ToString().TrimEnd());
            }
            catch
            {
                // 진단 로그는 부가 기능이다 — 실패해도 가져오기는 계속한다.
            }
        }

        private static string DiagnosticValue(Parameter parameter)
        {
            if (parameter.StorageType == StorageType.Integer && parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                return parameter.AsInteger() == 1 ? "예" : "아니오";
            var value = parameter.AsValueString() ?? parameter.AsString() ?? "(값 없음)";
            return parameter.IsReadOnly ? $"{value} (읽기 전용)" : value;
        }

        /// <summary>커넥터가 위·아래를 향하는지(밸브부관 A의 수직분기, docs/227 § E). 배치 경계와 같은 기준(|Z| ≥ 0.7)이다.</summary>
        private static bool IsVerticalConnector(Connector connector)
            => Math.Abs(connector.CoordinateSystem.BasisZ.Z) >= 0.7;

        private static PickedConnector ToPickedConnector(Connector connector)
            => new(
                ToPoint2D(connector.Origin),
                UC.FtToMm(connector.Origin.Z),
                PlanarOutward(connector),
                PipeMaterialConnectorReader.DiameterMm(connector),
                ConnectedElementIds(connector));

        /// <summary>커넥터 바깥 방향의 평면 성분(단위벡터). 수직 커넥터는 (0,0)이 되고, Core가 폴백을 쓴다.</summary>
        private static Point2D PlanarOutward(Connector connector)
        {
            var basis = connector.CoordinateSystem.BasisZ;
            var length = Math.Sqrt(basis.X * basis.X + basis.Y * basis.Y);
            return length <= 1e-9 ? new Point2D(0, 0) : new Point2D(basis.X / length, basis.Y / length);
        }

        private static IReadOnlyList<long> ConnectedElementIds(Connector connector)
        {
            var ids = new List<long>();
            foreach (Connector reference in connector.AllRefs)
                if (reference.Owner is { } owner && owner.Id.Value != connector.Owner.Id.Value)
                    ids.Add(owner.Id.Value);
            return ids;
        }

        private static Point2D AverageConnectorPoint(IReadOnlyList<Connector> connectors)
            => connectors.Count == 0
                ? new Point2D(0, 0)
                : new Point2D(connectors.Average(x => UC.FtToMm(x.Origin.X)), connectors.Average(x => UC.FtToMm(x.Origin.Y)));

        private static Point2D ToPoint2D(XYZ point) => new(UC.FtToMm(point.X), UC.FtToMm(point.Y));

        private sealed class PipingSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => IsPipingInstance(elem);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
