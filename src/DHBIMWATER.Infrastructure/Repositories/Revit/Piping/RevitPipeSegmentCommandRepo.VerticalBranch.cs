using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

// docs/301 — 수직분기. 기존 부속 +Z 커넥터에서 부품을 순서대로 쌓음. 캔버스 망과 무관한 독립 요소.
// 같은 클래스(partial)인 이유: PlaceAt·파라미터 구동·커넥터 헬퍼를 그대로 씀.
internal sealed partial class RevitPipeSegmentCommandRepo : IVerticalBranchCommandRepo
{
    public VerticalBranchResult Place(VerticalBranchRequest request)
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var families = request.Families;
        if (string.IsNullOrWhiteSpace(families.LengthParameterName))
            throw new DhbimwaterException(ErrorCodes.PL_LengthParameterMissing, "관 길이 파라미터가 지정되지 않았습니다.");
        var start = document.GetElement(new ElementId(request.StartElementId)) as FamilyInstance
            ?? throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchNoUpConnector,
                $"시작 부속(ElementId {request.StartElementId})이 없습니다. 다시 선택하세요.");

        var warnings = new List<string>();
        // 밸브부관 A — 수직분기 형상 켬. 커넥터는 가시성과 무관하게 있음(docs/227 § 0-5).
        if (IsSameFamily(start, families.ValveTeeFamilyTypeName))
        {
            SetOptionalFlagParameter(start, families.ValveTeeVerticalBranchParameterName, true, PipeValveBend.PartNameA, warnings);
            document.Regenerate();
        }
        var anchor = FindOpenUpConnector(start)
            ?? throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchNoUpConnector,
                $"'{start.Symbol.Family.Name} : {start.Symbol.Name}'에 연결되지 않은 위(+Z) 커넥터가 없습니다.");
        var diameterMm = PipeMaterialConnectorReader.DiameterMm(anchor)
            ?? throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed, "시작 커넥터 지름을 읽을 수 없습니다(원형 커넥터가 아님).");

        var symbols = new FamilySymbolCache(document);
        var segment = request.Parts.Any(x => x.IsPipe) ? symbols.Resolve(families.SegmentFamilyTypeName, "관") : null;
        if (segment is not null) ValidateSegmentSymbol(document, segment, families.LengthParameterName);
        var level = ResolveNearestLevel(document, anchor.Origin.Z);

        var placedIds = new List<long>();
        var materialFailed = new List<string>();
        Connector? previous = anchor;
        FamilyInstance? last = null;
        for (var i = 0; i < request.Parts.Count; i++)
        {
            var part = request.Parts[i];
            var label = $"#{i + 1} {(part.IsPipe ? PipeMaterialSpecText.ShortName : part.FamilyTypeName)}";
            // 1포트 뒤에는 붙일 커넥터 없음 — 일부만 남기지 않게 예외로 롤백.
            if (previous is null)
                throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed,
                    $"{label}: 앞 부품이 커넥터 1개라 더 쌓을 수 없습니다. 커넥터 1개 부품(공기밸브 등)은 맨 끝에만 두세요.");

            var instance = part.IsPipe
                ? PlaceVerticalPipe(document, segment!, families, previous.Origin, part.LengthMm, diameterMm, level)
                : PlaceVerticalValve(document, symbols, families, part.FamilyTypeName, previous.Origin, diameterMm, level, warnings);
            placedIds.Add(instance.Id.Value);
            last = instance;

            var connectors = GetMepConnectors(instance).OrderBy(x => x.Id).ToList();
            if (connectors.Count is 0 or > 2)
                throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed,
                    $"{label}: 배관 커넥터가 {connectors.Count}개입니다. 수직분기는 1포트·2포트 부품만 쌓습니다.");
            // 입구 = Id 최소 커넥터. 수평 관 소켓 방향 규칙과 같은 기준(CorrectSegmentSocketDirection).
            var inletId = connectors[0].Id;
            AlignConnector(document, instance, inletId, -XYZ.BasisZ, label);
            var inlet = ConnectorById(instance, inletId);
            var move = previous.Origin - inlet.Origin;
            if (move.GetLength() > UC.MmToFt(ConnectionToleranceMm) / 10)
            {
                ElementTransformUtils.MoveElement(document, instance.Id, move);
                document.Regenerate();
                inlet = ConnectorById(instance, inletId);
            }
            try { inlet.ConnectTo(previous); }
            catch (Exception ex) { warnings.Add($"{label}: 아래 부품과 커넥터를 연결하지 못했습니다 — {ex.Message}"); }

            Connector? outlet = null;
            if (connectors.Count == 2)
            {
                outlet = GetMepConnectors(instance).First(x => x.Id != inletId);
                // 직통 부품만 — 출구가 위를 안 보면 다음 부품이 기울어짐.
                if (outlet.CoordinateSystem.BasisZ.Z < VerticalConnectorMinZ)
                    throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed,
                        $"{label}: 두 커넥터가 일직선이 아닙니다(출구가 위를 향하지 않음). 직통 2포트 부품만 쌓을 수 있습니다.");
            }

            // 재료표 — 명칭·규격만(docs/301 § B-8). 번호는 매기지 않음.
            SetOptionalTextParameter(instance, ValvePipingParameters.ItemName,
                part.IsPipe ? PipeMaterialSpecText.ShortName : FittingNameOf(instance), materialFailed);
            SetOptionalTextParameter(instance, ValvePipingParameters.ItemSpec,
                part.IsPipe ? SegmentSpecText(diameterMm, part.LengthMm) : ValvePipingParameters.DiameterText(diameterMm), materialFailed);
            previous = outlet;
        }

        if (materialFailed.Count > 0)
            warnings.Add($"재료표 매개변수를 쓰지 못한 부품이 있습니다: {string.Join(", ", materialFailed.Distinct())}");
        var topFt = previous?.Origin.Z ?? (last?.Location as LocationPoint)?.Point.Z ?? anchor.Origin.Z;
        return new VerticalBranchResult(placedIds.Count, UC.FtToMm(topFt), placedIds, warnings);
    }

    /// <summary>단관 — 중심을 앵커 위 L/2에 놓고 직경·길이 구동. 축 맞춤은 호출부(AlignConnector).</summary>
    private static FamilyInstance PlaceVerticalPipe(Document document, FamilySymbol segment, PipeSegmentFamilySelection families,
        XYZ anchor, double lengthMm, double diameterMm, Level? level)
    {
        var instance = PlaceAt(document, segment, anchor + XYZ.BasisZ * UC.MmToFt(lengthMm / 2), 0, level);
        // 직경 먼저 — 모델 생성 경로와 같은 순서(RevitPipeSegmentCommandRepo.cs:247-249).
        if (!string.IsNullOrWhiteSpace(families.DiameterParameterName))
            SetDiameterParameterMm(instance, families.DiameterParameterName, diameterMm, "관 직경");
        SetDoubleParameterMm(instance, families.LengthParameterName, lengthMm, "관 길이");
        document.Regenerate();
        return instance;
    }

    private static FamilyInstance PlaceVerticalValve(Document document, FamilySymbolCache symbols, PipeSegmentFamilySelection families,
        string familyTypeName, XYZ anchor, double diameterMm, Level? level, List<string> warnings)
    {
        var symbol = symbols.Resolve(familyTypeName, "배관부속");
        var instance = PlaceAt(document, symbol, anchor, 0, level);
        // 4번 섹션 패밀리별 직경 파라미터(docs/135 § B). 관경 = 시작 커넥터.
        SetOptionalDiameterParameter(instance, ValveDiameterParameterNameFor(families, symbol), diameterMm, "배관부속", warnings);
        document.Regenerate();
        return instance;
    }

    /// <summary>커넥터 바깥 방향(BasisZ)이 <paramref name="target"/>을 향하게 3D 회전. 회전축은 커넥터 원점을 지남.</summary>
    private static void AlignConnector(Document document, FamilyInstance instance, int connectorId, XYZ target, string label)
    {
        var connector = ConnectorById(instance, connectorId);
        var current = connector.CoordinateSystem.BasisZ.Normalize();
        var dot = Math.Clamp(current.DotProduct(target), -1, 1);
        if (dot >= 1 - 1e-9) return;
        var axis = current.CrossProduct(target);
        // 정반대면 외적 0 — 수평 아무 축으로 180°.
        if (axis.GetLength() < 1e-9) axis = Math.Abs(current.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
        var origin = connector.Origin;
        try
        {
            ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(origin, origin + axis.Normalize()), Math.Acos(dot));
            document.Regenerate();
        }
        catch (Exception ex)
        {
            throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed,
                $"{label}: 부품을 세우지 못했습니다(3D 회전 실패). 패밀리가 작업기준면·수직 고정이면 세울 수 없습니다. {ex.Message}", ex);
        }
        var after = ConnectorById(instance, connectorId).CoordinateSystem.BasisZ;
        if (after.DotProduct(target) < VerticalConnectorMinZ)
            throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchPlacementFailed,
                $"{label}: 회전 뒤에도 커넥터가 수직이 아닙니다. 패밀리가 회전을 제한하는지 확인하세요.");
    }

    private static Connector ConnectorById(FamilyInstance instance, int connectorId)
        => GetMepConnectors(instance).First(x => x.Id == connectorId);

    /// <summary>연결 안 된 위(+Z) 커넥터. 여럿이면 가장 높은 것.</summary>
    private static Connector? FindOpenUpConnector(FamilyInstance instance)
        => GetMepConnectors(instance)
            .Where(x => !x.IsConnected && x.CoordinateSystem.BasisZ.Z >= VerticalConnectorMinZ)
            .OrderByDescending(x => x.Origin.Z)
            .FirstOrDefault();

    /// <summary>"패밀리 : 유형" 지정과 같은 패밀리인지(유형 무관 — 관경별 유형이 따로 있음).</summary>
    private static bool IsSameFamily(FamilyInstance instance, string? familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName)) return false;
        var separator = familyTypeName.IndexOf(" : ", StringComparison.Ordinal);
        var familyName = separator < 0 ? familyTypeName : familyTypeName[..separator];
        return string.Equals(instance.Symbol.Family.Name, familyName.Trim(), StringComparison.Ordinal);
    }
}
