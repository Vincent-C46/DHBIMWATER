using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>접합 개소를 어느 매개변수에 적을지 가르는 규칙(docs/166 § 6·§ 7, docs/168 § 4·§ 5-3, docs/170).
/// <para><b>배분표는 하나다(docs/170 § D-1)</b> — 계획관로와 밸브실 모두 <b>관종 + 소켓/플랜지 + 압륜</b>으로 가른다.
/// 밸브실이 달리 갖는 것은 "접합 방식을 어떻게 알아내느냐"(<see cref="PipeJointResolution"/>)뿐이다.
/// 규칙은 산출서(도수1-1) 원장에서 나왔고, 여기 숫자가 바뀌면 산출서가 조용히 틀린다.</para></summary>
public sealed class JointCountMappingTests
{
    // ── 계획관로: 관종 + 곡관 접합 → 슬롯 종류 ──────────────────────────────────

    [Theory]
    // 강관은 소켓 조인트가 없다. 플랜지가 아니면 용접이다 — 산출서의 `이형관 용접합`이 이것이다.
    [InlineData(PipeMaterial.Steel, BendConnection.Socket, "용접합")]
    [InlineData(PipeMaterial.Steel, BendConnection.Flanged, "플랜지접합")]
    // 주철관의 소켓 계열(KP 메커니컬·타이튼).
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, "KP접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Flanged, "플랜지접합")]
    public void JointKindFor_IsDecidedByMaterialAndConnection(PipeMaterial material, BendConnection connection, string expected)
        => Assert.Equal(expected, PipeAlignmentParameters.JointKindFor(material, connection));

    [Theory]
    [InlineData(PipeMaterial.Pe)]
    [InlineData(PipeMaterial.Pvc)]
    [InlineData(null)]
    public void JointKindFor_ReturnsNull_WhenSocketMaterialHasNoColumn(PipeMaterial? material)
        // 융착·접착은 세 공종 어디에도 없다. 값을 지어내지 말고 셋 다 0 + 경고다.
        => Assert.Null(PipeAlignmentParameters.JointKindFor(material, BendConnection.Socket));

    [Fact]
    public void Flanged_MapsRegardlessOfMaterial()
    {
        // 플랜지 접합은 관종을 가리지 않는다(KCS 57 30 20 → 3.3).
        Assert.Equal("플랜지접합", PipeAlignmentParameters.JointKindFor(PipeMaterial.Pe, BendConnection.Flanged));
        Assert.Equal("플랜지접합", PipeAlignmentParameters.JointKindFor(null, BendConnection.Flanged));
    }

    [Fact]
    public void JointTotalCount_ReplacesLegacyJointColumns()
    {
        // 산출서의 접합 공종 열 넷 — 용접합 / 플랜지 접합 / KP접합 / 이탈방지압륜 접합(docs/168 § 3).
        // 부설은 접합과 무관한 별도 계수라 이 묶음에 들어가지 않는다.
        Assert.Equal("DH_접합수", PipeAlignmentParameters.JointTotalCount);
        Assert.Contains(PipeAlignmentParameters.JointTotalCount, PipeAlignmentParameters.Guids.Keys);
    }

    [Theory]
    // 주철관 소켓에서만 갈린다 — 압륜은 KP 메커니컬 조인트에 덧대는 부속이다.
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, true, "이탈방지압륜접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, false, "KP접합")]
    // 강관·플랜지는 플래그를 켜도 그대로다.
    [InlineData(PipeMaterial.Steel, BendConnection.Socket, true, "용접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Flanged, true, "플랜지접합")]
    [InlineData(PipeMaterial.Steel, BendConnection.Flanged, true, "플랜지접합")]
    public void RestraintGland_SplitsOnlyDuctileIronSocket(PipeMaterial material, BendConnection connection, bool useRestraintGland, string expected)
        => Assert.Equal(expected, PipeAlignmentParameters.JointKindFor(material, connection, useRestraintGland));

    [Fact]
    public void RestraintGland_DefaultsToOff_SoExistingModelsKeepKpCounts()
        => Assert.Equal("KP접합",
            PipeAlignmentParameters.JointKindFor(PipeMaterial.DuctileIron, BendConnection.Socket));

    [Fact]
    public void JointTotalCount_HasItsOwnGuid_SharedByBothStores()
    {
        // KP접합과 별개 공종이므로 GUID도 별개여야 한다. 두 저장 위치는 같은 값을 본다.
        Assert.Equal(PipeAlignmentParameters.JointTotalCount, ValvePipingParameters.JointTotalCount);
        Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.JointTotalCount],
            ValvePipingParameters.Guids[ValvePipingParameters.JointTotalCount]);
    }

    // ── 밸브실: 접합 방식 해석 (docs/170 § A) ─────────────────────────────────

    [Theory]
    // 순위 1 — 패밀리 파라미터가 있으면 그것이 이긴다. 이름 표기가 반대여도 뒤집지 못한다.
    [InlineData("플랜지", "1S 곡관", BendConnection.Socket, BendConnection.Flanged)]
    [InlineData("소켓", "2F 단관", BendConnection.Flanged, BendConnection.Socket)]
    // 순위 2 — 파라미터가 없으면 부재명 앞머리 표기를 본다. 폴백이 반대여도 표기가 이긴다.
    [InlineData(null, "1S1F 단관", BendConnection.Flanged, BendConnection.Socket)]
    [InlineData("", "1S 곡관", BendConnection.Flanged, BendConnection.Socket)]
    // 순위 3 — 둘 다 없으면 폴백이다.
    [InlineData(null, "단관", BendConnection.Flanged, BendConnection.Flanged)]
    [InlineData(null, "단관", BendConnection.Socket, BendConnection.Socket)]
    [InlineData(null, null, BendConnection.Flanged, BendConnection.Flanged)]
    // 해석 못 하는 파라미터 값은 없는 것과 같다 — 아래 순위로 내려간다.
    [InlineData("STS316", "1S 곡관", BendConnection.Flanged, BendConnection.Socket)]
    public void Resolve_FollowsPriority_ParameterThenNameThenFallback(
        string? parameterValue, string? partName, BendConnection fallback, BendConnection expected)
        => Assert.Equal(expected, PipeJointResolution.Resolve(parameterValue, partName, fallback));

    [Theory]
    [InlineData("소켓")]
    [InlineData("KP 메커니컬 조인트")]
    [InlineData("타이튼 조인트")]
    [InlineData("맞대기 용접")]
    [InlineData("Socket Weld")]
    [InlineData("S")]
    [InlineData("so")]
    public void ParseConnection_ReadsSocketVocabulary(string value)
        => Assert.Equal(BendConnection.Socket, PipeJointResolution.ParseConnection(value));

    [Theory]
    [InlineData("플랜지")]
    [InlineData("Flange")]
    [InlineData("F")]
    [InlineData("fl")]
    public void ParseConnection_ReadsFlangeVocabulary(string value)
        => Assert.Equal(BendConnection.Flanged, PipeJointResolution.ParseConnection(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DN1650")]
    // 한 글자 표기를 포함 판정으로 돌리면 이런 값이 잡힌다. 정확히 일치할 때만 본다.
    [InlineData("Stainless")]
    [InlineData("FLEX")]
    // 두 접합이 함께 잡히면 한쪽으로 몰지 않는다 — 어느 쪽이든 절반은 틀린다.
    [InlineData("소켓/플랜지")]
    public void ParseConnection_ReturnsNull_WhenItCannotTell(string? value)
        => Assert.Null(PipeJointResolution.ParseConnection(value));

    [Fact]
    public void ParameterNames_IsEmptyUntilTheFamilySurveyLandsIt()
        // docs/170 § B 조사 전에는 비어 있어야 한다. 값이 들어차면 순위 1이 동작을 바꾸므로
        // "조사 전에는 docs/170 이전과 같은 결과"라는 회귀 기준(§ 10-1)이 깨진다.
        => Assert.Empty(PipeJointResolution.ParameterNames);

    [Theory]
    [InlineData("STS 단관", false)]      // 본문의 S를 접합 표기로 착각하면 안 된다
    [InlineData("단관", false)]
    [InlineData("1S", true)]
    [InlineData("2F1S 단관", true)]
    [InlineData("2F 단관", false)]
    public void HasSocketMark_ReadsOnlyTheLeadingJointTag(string partName, bool expected)
        => Assert.Equal(expected, PipeJointResolution.HasSocketMark(partName));

    // ── 밸브실: 관종 + 접합 방식 → 매개변수 (docs/170 § D-1) ──────────────────

    [Theory]
    [InlineData(PipeMaterial.Steel, BendConnection.Socket, false, "용접합")]
    [InlineData(PipeMaterial.Steel, BendConnection.Flanged, false, "플랜지접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, false, "KP접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Socket, true, "이탈방지압륜접합")]
    [InlineData(PipeMaterial.DuctileIron, BendConnection.Flanged, true, "플랜지접합")]
    public void ValveRoom_UsesMaterialAndConnection(PipeMaterial material, BendConnection connection, bool gland, string expected)
        => Assert.Equal(expected, PipeJointMethodTable.JointKindFor(material, connection, gland, isAccessory: false));

    [Fact]
    public void ValveRoom_AndAlignment_AgreeOnEveryInput()
    {
        // docs/169 § 6-5의 성공 기준을 구조로 고정한다 — 두 경로가 같은 입력에 다른 값을 찍으면 DHBoost 집계가 갈린다.
        PipeMaterial?[] materials = [PipeMaterial.Steel, PipeMaterial.DuctileIron, PipeMaterial.Pe, PipeMaterial.Pvc, null];
        foreach (var material in materials)
            foreach (var connection in new[] { BendConnection.Socket, BendConnection.Flanged })
                foreach (var gland in new[] { true, false })
                    Assert.Equal(
                        PipeAlignmentParameters.JointKindFor(material, connection, gland),
                        PipeJointMethodTable.JointKindFor(material, connection, gland, isAccessory: false));
    }

    [Fact]
    public void ValveRoom_Accessory_IsNotCounted_BecauseValveWorkIncludesTheJoint()
    {
        // 밸브는 `제수밸브 접합 및 부설`(CGI…)로 1대 = 1nr 계상된다. 여기서 또 찍으면 이중 계상(docs/168 § 5-4).
        Assert.Null(PipeJointMethodTable.JointKindFor(PipeMaterial.Steel, BendConnection.Socket, false, isAccessory: true));
        Assert.Null(PipeJointMethodTable.JointKindFor(PipeMaterial.DuctileIron, BendConnection.Flanged, true, isAccessory: true));
    }

    // ── 밸브실: 부재명 접합 표기 (docs/170 § C) ───────────────────────────────

    [Theory]
    [InlineData(PipeMaterialSpecText.StraightName, BendConnection.Flanged, "2F 직관")]
    [InlineData(PipeMaterialSpecText.ShortName, BendConnection.Socket, "2S 단관")]
    [InlineData(PipeMaterialSpecText.BendName, BendConnection.Socket, "2S 곡관")]
    [InlineData(PipeMaterialSpecText.TeeName, BendConnection.Flanged, "3F T형")]   // T형만 3구다
    public void WithJointMark_PrefixesEndCountAndConnection(string partName, BendConnection connection, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.WithJointMark(partName, connection));

    [Theory]
    [InlineData(PipeMaterialSpecText.StraightName)]
    [InlineData(PipeMaterialSpecText.ShortName)]
    [InlineData(PipeMaterialSpecText.BendName)]
    [InlineData(PipeMaterialSpecText.TeeName)]
    public void StripJointMark_IsTheInverseOfWithJointMark(string partName)
    {
        foreach (var connection in new[] { BendConnection.Socket, BendConnection.Flanged })
            Assert.Equal(partName, PipeMaterialSpecText.StripJointMark(PipeMaterialSpecText.WithJointMark(partName, connection)));
    }

    [Fact]
    public void WithJointMark_DoesNotStack_WhenRunAgainOnAnAlreadyMarkedName()
        // 재기록은 여러 번 돌 수 있다. `2F 2F 단관`이 되면 일람표 행이 실행마다 갈린다.
        => Assert.Equal("2S 단관", PipeMaterialSpecText.WithJointMark("2F 단관", BendConnection.Socket));

    /// <summary>접두는 접합 구성에서 나온다(2026-09-15) — `2S1F` T형이 `3F T형`으로 적히지 않는다. 구형 값도 정규화된다.</summary>
    [Theory]
    [InlineData("T형", "2S1F", BendConnection.Flanged, 3, "2S1F T형")]
    [InlineData("단관", "1S1F", BendConnection.Socket, 2, "1S1F 단관")]
    [InlineData("T형", "KP:2+플랜지:1", BendConnection.Flanged, 3, "2S1F T형")]
    [InlineData("3F T형", "1F2S", BendConnection.Flanged, 3, "2S1F T형")]
    public void WithJointMark_UsesComposition_WhenPresent(string partName, string composition, BendConnection connection, int endCount, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.WithJointMark(partName, composition, connection, endCount));

    /// <summary>접합 구성을 모르면(밸브·PE·플래그 없음) 종전 단일 표기로 폴백한다.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2X")]
    public void WithJointMark_FallsBackToConnection_WhenCompositionUnknown(string? composition)
        => Assert.Equal("3F T형", PipeMaterialSpecText.WithJointMark("T형", composition, BendConnection.Flanged, 3));

    /// <summary>혼합 접두가 붙은 이름도 기본명·단부 수·소켓 판정이 그대로 읽힌다.</summary>
    [Fact]
    public void MixedMark_RoundTrips_ThroughStripEndCountAndSocketMark()
    {
        Assert.Equal("T형", PipeMaterialSpecText.StripJointMark("2S1F T형"));
        Assert.Equal(3, PipeMaterialSpecText.EndCountFor("2S1F T형"));
        Assert.True(PipeJointResolution.HasSocketMark("2S1F T형"));
        Assert.False(PipeJointResolution.HasSocketMark("3F T형"));
    }

    [Theory]
    [InlineData("STS 단관")]        // 표기가 아니면 원본 그대로
    [InlineData("1650 강관")]       // 숫자로 시작해도 뒤가 S/F가 아니면 자르지 않는다
    [InlineData("2F단관")]          // 표기 뒤에 공백이 없으면 사용자가 지은 이름으로 본다
    [InlineData("단관")]
    public void StripJointMark_LeavesNonMarkedNamesAlone(string partName)
        => Assert.Equal(partName, PipeMaterialSpecText.StripJointMark(partName));

    [Theory]
    [InlineData("2F 직관", "DCIP")]
    [InlineData("2S 단관", "DCIP")]
    [InlineData(PipeMaterialSpecText.StraightName, "DCIP")]
    [InlineData("3F T형", "")]      // 곡관·T형은 비고 자동값이 없다
    [InlineData("2S 곡관", "")]
    public void Remark_StillFillsSegments_AfterTheJointMarkWasAdded(string partName, string expected)
        // 표기를 떼고 비교하지 않으면 직관·단관의 DH_비고 자동값이 통째로 사라진다(docs/170 § C-⚠1).
        => Assert.Equal(expected, PipeMaterialSpecText.Remark(partName, PipeMaterial.DuctileIron));

    // ── 두 저장 위치가 같은 어휘를 쓰는지 ─────────────────────────────────────

    [Fact]
    public void ValveRoom_And_Alignment_ShareTheSameParameterNamesAndGuids()
    {
        Assert.Equal(PipeAlignmentParameters.JointTotalCount, ValvePipingParameters.JointTotalCount);
        Assert.Equal(PipeAlignmentParameters.LayingCount, ValvePipingParameters.LayingCount);

        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
            Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.JointSlotKind(slot)], ValvePipingParameters.Guids[ValvePipingParameters.JointSlotKind(slot)]);
        Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.LayingCount],
            ValvePipingParameters.Guids[ValvePipingParameters.LayingCount]);
    }
}
