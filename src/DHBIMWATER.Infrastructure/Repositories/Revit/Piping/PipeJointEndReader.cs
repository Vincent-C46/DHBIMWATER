using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>연결 패스(<see cref="PipeJointConnectionPass"/>) 입력을 모델에서 모은다(docs/266 § 1-1). 규칙은 두지 않고 값만 읽는다.</summary>
internal static class PipeJointEndReader
{
    /// <summary>패스 입력과, 결과를 쓸 요소 표.</summary>
    public sealed record Snapshot(IReadOnlyList<PipeJointMemberInput> Members, IReadOnlyList<PipeJointEndInput> Ends,
        IReadOnlyDictionary<long, Element> Elements, bool IsValveRoom);

    private static readonly Regex DiameterNumber = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

    public static Snapshot Read(Document document)
    {
        var members = new List<PipeJointMemberInput>();
        var ends = new List<PipeJointEndInput>();
        var elements = new Dictionary<long, Element>();

        ReadPipingFittings(document, members, ends, elements);
        ReadAdaptivePipes(document, members, ends, elements);

        return new Snapshot(members, ends, elements, IsValveRoom(document));
    }

    /// <summary>밸브실 문서인지 — 프로젝트 정보 `밸브실유형`이 채워져 있으면 그렇다(docs/266 § 1-3). 짝 없는 플랜지 · 유입부 절단 판정이 갈린다.</summary>
    private static bool IsValveRoom(Document document)
        => !string.IsNullOrWhiteSpace(document.ProjectInformation?.LookupParameter(ValveRoomProjectInfoParameters.RoomType)?.AsString());

    /// <summary>DHBIMWATER가 태깅한 배관 이음쇠·밸브류. 단부 = 물리 배관 End 커넥터, 짝 = <c>AllRefs</c>의 다른 부재 End 커넥터.
    /// 연결이 없는 단부는 위치만 넘겨 패스가 위치 일치로 짝을 찾는다(연결 실패로 열린 커넥터가 있다, docs/262 § 1-1).</summary>
    private static void ReadPipingFittings(Document document, List<PipeJointMemberInput> members, List<PipeJointEndInput> ends,
        Dictionary<long, Element> elements)
    {
        var instances = PipeMaterialPartCollector.AddinInstances(document).ToList();
        var connectorIndex = new Dictionary<(long Element, int Connector), int>();
        var connectorsById = new Dictionary<long, List<Connector>>();
        foreach (var instance in instances)
        {
            var connectors = PipeMaterialConnectorReader.EndConnectors(instance);
            connectorsById[instance.Id.Value] = connectors;
            for (var i = 0; i < connectors.Count; i++) connectorIndex[(instance.Id.Value, connectors[i].Id)] = i;
        }

        foreach (var instance in instances)
        {
            var id = instance.Id.Value;
            var connectors = connectorsById[id];
            if (connectors.Count == 0) continue;
            var diameters = connectors.Select(PipeMaterialConnectorReader.DiameterMm).ToList();
            members.Add(new PipeJointMemberInput(id,
                PipeJointCountWriter.IsValveForCount(instance),
                instance.LookupParameter(ValvePipingParameters.Part)?.AsString() ?? instance.LookupParameter(ValvePipingParameters.ItemName)?.AsString(),
                instance.LookupParameter(ValvePipingParameters.JointComposition)?.AsString(),
                diameters.Max() ?? 0d,
                // 밸브부관은 접합구성을 적지 않는 플랜지 이형관이다(R-7, docs/227).
                PipeValveBend.IsValveBendFamily(instance.Symbol?.Family?.Name)));
            elements[id] = instance;

            for (var i = 0; i < connectors.Count; i++)
            {
                var connector = connectors[i];
                var (partnerId, partnerIndex) = Partner(connector, id, connectorIndex);
                var origin = connector.Origin;
                ends.Add(new PipeJointEndInput(id, i, UC.FtToMm(origin.X), UC.FtToMm(origin.Y), UC.FtToMm(origin.Z),
                    diameters[i] ?? 0d, partnerId, partnerIndex));
            }
        }
    }

    private static (long? Id, int? Index) Partner(Connector connector, long ownerId, Dictionary<(long, int), int> connectorIndex)
    {
        if (!connector.IsConnected) return (null, null);
        foreach (Connector other in connector.AllRefs)
        {
            if (other.Owner is not FamilyInstance owner || owner.Id.Value == ownerId) continue;
            if (connectorIndex.TryGetValue((owner.Id.Value, other.Id), out var index)) return (owner.Id.Value, index);
        }
        return (null, null);
    }

    /// <summary>계획관로 가변 패밀리 — 직관 배치점 [0]·[1], 곡관 [0]·[4](P1·P5)가 단부다(docs/266 § 1-1). 커넥터가 없으니 위치 일치로만 짝을 찾는다.
    /// <para>플랜지 구성인 부재만 넣는다 — 계획관로 문서는 유입부 절단이 없어 소켓 부재는 결과가 언제나 비고,
    /// 선형 전체(수천 개)를 넣으면 한 번의 배치마다 모든 점을 다시 읽게 된다.</para></summary>
    private static void ReadAdaptivePipes(Document document, List<PipeJointMemberInput> members, List<PipeJointEndInput> ends,
        Dictionary<long, Element> elements)
    {
        var instances = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance);
        foreach (var instance in instances)
        {
            if (!string.Equals(instance.LookupParameter(PipeAlignmentParameters.Addin)?.AsString(), PipeAlignmentParameters.AddinValue, StringComparison.Ordinal)) continue;
            var composition = instance.LookupParameter(PipeAlignmentParameters.JointComposition)?.AsString();
            if (PipeJointComposition.Decode(composition).Flange <= 0) continue;
            var pointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);
            int[] endPoints = pointIds.Count switch { 2 => [0, 1], 5 => [0, 4], _ => [] };
            if (endPoints.Length == 0) continue;

            var id = instance.Id.Value;
            var diameter = DiameterFromText(instance.LookupParameter(PipeAlignmentParameters.NominalDiameter)?.AsString());
            members.Add(new PipeJointMemberInput(id, false, instance.LookupParameter(PipeAlignmentParameters.Part)?.AsString(),
                composition, diameter));
            elements[id] = instance;
            for (var i = 0; i < endPoints.Length; i++)
            {
                if (document.GetElement(pointIds[endPoints[i]]) is not ReferencePoint point) continue;
                var p = point.Position;
                ends.Add(new PipeJointEndInput(id, i, UC.FtToMm(p.X), UC.FtToMm(p.Y), UC.FtToMm(p.Z), diameter));
            }
        }
    }

    /// <summary>`DH_호칭지름`(`DN300`)의 첫 숫자. 이경관(`DN600×300`)은 큰 쪽이 앞이다. 못 읽으면 0.</summary>
    private static double DiameterFromText(string? text)
        => text is not null && DiameterNumber.Match(text) is { Success: true } match
           && double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : 0d;
}
