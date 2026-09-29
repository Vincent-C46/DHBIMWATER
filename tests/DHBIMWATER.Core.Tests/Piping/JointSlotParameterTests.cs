using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>접합 슬롯 매개변수 규약(docs/186 § 4·§ 7).
/// <para>슬롯 `종류` 문자열은 수량산출(DHBoost)의 <c>PipeWorkCatalog</c> 키와 <b>글자가 같아야</b> 행이 선다.
/// 여기 문자열이 바뀌면 산출서의 접합및부설이 조용히 빈다.</para>
/// <para>개소 매개변수 넷(<see cref="JointCountMappingTests"/>)은 밸브실 경로가 계속 쓰므로 그 테스트와 함께 남는다.</para></summary>
public sealed class JointSlotParameterTests
{
    // ── § 7-1. 관종·접합·압륜 → 슬롯 `종류` 문자열 ────────────────────────────

    [Theory]
    // 강관은 소켓 조인트가 없다. 플랜지가 아니면 용접이다.
    [InlineData(PipeMaterial.Steel, BendConnection.Socket, false, "용접합")]
    [InlineData(PipeMaterial.Steel, BendConnection.Flanged, false, "플랜지접합")]
    // 주철관 소켓 계열. 압륜을 쓰면 별개 공종으로 간다(docs/168 § 3-2).
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, false, "KP접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, true, "이탈방지압륜접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Flanged, false, "플랜지접합")]
    // 압륜은 KP 메커니컬 조인트에 덧대는 부속이라 강관·플랜지에는 영향이 없다.
    [InlineData(PipeMaterial.Steel, BendConnection.Socket, true, "용접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Flanged, true, "플랜지접합")]
    public void JointKindFor_MapsMaterialAndConnectionToSlotKind(PipeMaterial material, BendConnection connection, bool gland, string expected)
        => Assert.Equal(expected, PipeAlignmentParameters.JointKindFor(material, connection, gland));

    [Theory]
    [InlineData(PipeMaterial.Pe)]
    [InlineData(PipeMaterial.Pvc)]
    [InlineData(null)]
    public void JointKindFor_ReturnsNull_WhenSocketMaterialHasNoWorkType(PipeMaterial? material)
        // 융착·접착은 접합 공종 어디에도 없다. 값을 지어내지 말고 슬롯을 비우고 경고다(docs/186 § 4-2).
        => Assert.Null(PipeAlignmentParameters.JointKindFor(material, BendConnection.Socket));

    [Fact]
    public void JointKindFor_MapsEverySupportedCombination()
    {
        // 배분표는 한 벌이다 — 슬롯 종류는 개소 매개변수 이름을 옮겨 적기만 한다(docs/186 § 6-1 ④).
        // 둘이 갈리면 관로(슬롯)와 밸브실(개소)이 같은 부재를 다른 공종으로 센다.
        foreach (var material in new PipeMaterial?[] { PipeMaterial.Steel, PipeMaterial.DuctileIron, PipeMaterial.Pe, PipeMaterial.Pvc, null })
            foreach (var connection in new[] { BendConnection.Socket, BendConnection.Flanged })
                foreach (var gland in new[] { false, true })
                {
                    var kind = PipeAlignmentParameters.JointKindFor(material, connection, gland);
                    Assert.Equal(connection == BendConnection.Socket && material is PipeMaterial.Pe or PipeMaterial.Pvc or null, kind is null);
                }
    }

    // ── § 7-2. 이름 생성기 ────────────────────────────────────────────────────

    [Fact]
    public void SlotNameBuilders_FollowTheDhboostScanPattern()
    {
        // DHBoost가 `DH_접합{n}_종류`·`_규격`·`_수` 이름 규칙으로 n을 훑는다. 이 형식이 바뀌면 그쪽이 못 읽는다.
        Assert.Equal("DH_접합2_종류", PipeAlignmentParameters.JointSlotKind(2));
        Assert.Equal("DH_접합2_규격", PipeAlignmentParameters.JointSlotSpec(2));
        Assert.Equal("DH_접합2_수", PipeAlignmentParameters.JointSlotCount(2));
    }

    // ── § 7-3. GUID ───────────────────────────────────────────────────────────

    [Fact]
    public void Guids_ContainEverySlotName()
    {
        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
        {
            Assert.True(PipeAlignmentParameters.Guids.ContainsKey(PipeAlignmentParameters.JointSlotKind(slot)));
            Assert.True(PipeAlignmentParameters.Guids.ContainsKey(PipeAlignmentParameters.JointSlotSpec(slot)));
            Assert.True(PipeAlignmentParameters.Guids.ContainsKey(PipeAlignmentParameters.JointSlotCount(slot)));
        }
    }

    [Fact]
    public void Guids_HaveNoDuplicates()
    {
        // 같은 GUID를 두 이름이 쓰면 Revit이 한 매개변수로 묶어 값이 서로를 덮어쓴다.
        var duplicates = PipeAlignmentParameters.Guids.GroupBy(pair => pair.Value)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join("·", group.Select(pair => pair.Key)))
            .ToList();
        Assert.Empty(duplicates);
    }

    // ── § 7-4. 정의 ───────────────────────────────────────────────────────────

    [Fact]
    public void Definitions_ContainSlots_WithCountAsIntegerAndRestAsText()
    {
        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
        {
            Assert.Equal(ParameterSpecType.Text, SpecOf(PipeAlignmentParameters.JointSlotKind(slot)));
            Assert.Equal(ParameterSpecType.Text, SpecOf(PipeAlignmentParameters.JointSlotSpec(slot)));
            Assert.Equal(ParameterSpecType.Integer, SpecOf(PipeAlignmentParameters.JointSlotCount(slot)));
        }
    }

    // ── § 7-5. 밸브실 회귀 방지 ───────────────────────────────────────────────

    [Fact]
    public void JointTotalCount_IsDefined_ForValveRoomAndAlignment()
    {
        // 관로 경로는 슬롯으로 옮겼지만 밸브실 배관(PipeJointCountWriter)은 개소 매개변수 넷을 그대로 쓴다.
        // 특히 `DH_이탈방지압륜접합수`는 재기록이 압륜 여부를 이어받는 사용자 입력 채널이다(docs/186 § 6-4).
        Assert.Equal(ParameterSpecType.Integer, SpecOf(PipeAlignmentParameters.JointTotalCount));
        Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.JointTotalCount],
            ValvePipingParameters.Guids[ValvePipingParameters.JointTotalCount]);
    }

    private static ParameterSpecType SpecOf(string name)
        => PipeAlignmentParameters.Definitions.Single(definition => definition.Name == name).SpecType;
}
