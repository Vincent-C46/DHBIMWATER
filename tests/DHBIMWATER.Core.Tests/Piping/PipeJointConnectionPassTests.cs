using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>docs/266 § 1 연결 패스(<see cref="PipeJointConnectionPass"/>)만 본다. 부재별 개수 규칙은 <c>PipeCountRulesTests</c>.</summary>
public sealed class PipeJointConnectionPassTests
{
    /// <summary>부재·연결을 손으로 적는 작은 도우미. 위치는 서로 겹치지 않게 부재마다 멀리 떨어뜨려 위치 일치가 끼어들지 않게 한다.</summary>
    private sealed class Net
    {
        private readonly List<PipeJointMemberInput> _members = [];
        private readonly Dictionary<(long, int), PipeJointEndInput> _ends = [];
        private long _next = 1;

        /// <param name="part">관이면 `단관`·`직관`. 비우면 이형관이다.</param>
        public long Add(string composition, double diameterMm, string? part = null, bool isValve = false, double[]? endDiameters = null)
        {
            var id = _next++;
            var ends = endDiameters ?? [diameterMm, diameterMm];
            _members.Add(new PipeJointMemberInput(id, isValve, part, composition, diameterMm));
            for (var i = 0; i < ends.Length; i++)
                _ends[(id, i)] = new PipeJointEndInput(id, i, id * 100_000d + i * 10_000d, 0, 0, ends[i]);
            return id;
        }

        public void Connect(long a, int ai, long b, int bi)
        {
            _ends[(a, ai)] = _ends[(a, ai)] with { PartnerMemberId = b, PartnerIndex = bi };
            _ends[(b, bi)] = _ends[(b, bi)] with { PartnerMemberId = a, PartnerIndex = ai };
        }

        public PipeJointPassResult Run(bool isValveRoom = true)
            => PipeJointConnectionPass.Run(_members, _ends.Values.ToList(), isValveRoom);

        public double DiameterOf(long id) => _members.Single(x => x.Id == id).DiameterMm;
    }

    /// <summary>플랜지 개소를 접합 관경별로 합친다. 규격이 비면 귀속 부재 관경이다.</summary>
    private static Dictionary<double, int> FlangeByDiameter(Net net, PipeJointPassResult result)
        => result.FlangeSlots
            .SelectMany(x => x.Value.Select(slot => (Diameter: slot.Spec.Length == 0 ? net.DiameterOf(x.Key) : double.Parse(slot.Spec[1..]), slot.Count)))
            .Where(x => x.Count > 0)
            .GroupBy(x => x.Diameter)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

    // ── 배관상세도 _2 (제수밸브실 No.3+19.10) — 기대값은 docs/266 § 4-1 ────────────

    [Fact]
    public void GateValveRoomNo3_MatchesDetailDrawing()
    {
        var net = new Net();
        // 바이패스: 주관 부속의 D300 분기 → 밸브부관 B → 제수밸브 D300 → 밸브부관 B → 맞은편 D300 분기
        void Bypass(long from, int fromEnd, long to, int toEnd)
        {
            var b1 = net.Add("2F", 300);
            var valve = net.Add("", 300, isValve: true);
            var b2 = net.Add("2F", 300);
            net.Connect(from, fromEnd, b1, 0);
            net.Connect(b1, 1, valve, 0);
            net.Connect(valve, 1, b2, 0);
            net.Connect(b2, 1, to, toEnd);
        }
        // 공기밸브 분기: 4F T형관 D600 → 공기변용플랜지 D600xD200 → 제수밸브 D200 → 2F 단관 D200 → 공기밸브
        void AirValve(long tee, int teeEnd)
        {
            var reducer = net.Add("2F", 600, endDiameters: [600, 200]);
            var valve = net.Add("", 200, isValve: true);
            var pipe = net.Add("2F", 200, part: "단관");
            var air = net.Add("", 200, isValve: true, endDiameters: [200]);
            net.Connect(tee, teeEnd, reducer, 0);
            net.Connect(reducer, 1, valve, 0);
            net.Connect(valve, 1, pipe, 0);
            net.Connect(pipe, 1, air, 0);
        }
        long Tee4F() => net.Add("4F", 1650, endDiameters: [1650, 1650, 600, 300]);
        long ValveTeeA() => net.Add("3F", 1650, endDiameters: [1650, 1650, 300]);
        long Bfv() => net.Add("", 1650, isValve: true);

        // 하부 본관: 유입 1S1F 단관 → 4F T → BFV → 밸브부관 A → 3F T(D1650 상향) → 밸브부관 A → BFV → 4F T → 밸브접합관 → 1F 단관(유출)
        var inletLow = net.Add("1S1F", 1650, part: "단관");
        var teeLowL = Tee4F();
        var bfvL = Bfv();
        var aL = ValveTeeA();
        var tee3F = net.Add("3F", 1650, endDiameters: [1650, 1650, 1650]);
        var aR = ValveTeeA();
        var bfvR = Bfv();
        var teeLowR = Tee4F();
        var jointLow = net.Add("2F", 1650);
        var outlet = net.Add("1F", 1650, part: "단관");
        net.Connect(inletLow, 1, teeLowL, 0);
        net.Connect(teeLowL, 1, bfvL, 0);
        net.Connect(bfvL, 1, aL, 0);
        net.Connect(aL, 1, tee3F, 0);
        net.Connect(tee3F, 1, aR, 0);
        net.Connect(aR, 1, bfvR, 0);
        net.Connect(bfvR, 1, teeLowR, 0);
        net.Connect(teeLowR, 1, jointLow, 0);
        net.Connect(jointLow, 1, outlet, 0);
        Bypass(teeLowL, 3, aL, 2);
        Bypass(aR, 2, teeLowR, 3);
        AirValve(teeLowL, 2);
        AirValve(teeLowR, 2);

        // 상향관: 3F T 분기 → 밸브접합관 → 4F T → BFV → 밸브부관 A → 2F 곡관 → 2F 단관 4.00m → 유입 1S1F 단관
        var jointUp = net.Add("2F", 1650);
        var teeUp = Tee4F();
        var bfvUp = Bfv();
        var aUp = ValveTeeA();
        var bend = net.Add("2F", 1650);
        var pipe4m = net.Add("2F", 1650, part: "단관");
        var inletUp = net.Add("1S1F", 1650, part: "단관");
        net.Connect(tee3F, 2, jointUp, 0);
        net.Connect(jointUp, 1, teeUp, 0);
        net.Connect(teeUp, 1, bfvUp, 0);
        net.Connect(bfvUp, 1, aUp, 0);
        net.Connect(aUp, 1, bend, 0);
        net.Connect(bend, 1, pipe4m, 0);
        net.Connect(pipe4m, 1, inletUp, 0);
        Bypass(aUp, 2, teeUp, 3);
        AirValve(teeUp, 2);

        var result = net.Run();
        var flange = FlangeByDiameter(net, result);

        Assert.Equal(10, flange.GetValueOrDefault(1650));
        Assert.Equal(3, flange.GetValueOrDefault(600));
        Assert.Equal(6, flange.GetValueOrDefault(300));
        Assert.Equal(0, flange.GetValueOrDefault(200));
        Assert.Equal(2, result.CutCounts.Values.Sum());          // 유입부 소켓 2곳
        Assert.Equal(1, result.CutCounts[inletLow]);
        Assert.Equal(1, result.CutCounts[inletUp]);
        Assert.Equal(0, result.CutCounts[outlet]);               // 유출부 평단은 자르지 않는다
        Assert.Equal(0, result.UnpairedFlangeCount);
        Assert.Equal(0, result.UnresolvedEndCount);
    }

    // ── 규칙별 ────────────────────────────────────────────────

    [Fact]
    public void FlangeToFlange_CountsOnce_OnPipeBeforeFitting()
    {
        var net = new Net();
        var bend = net.Add("2F", 600);
        var pipe = net.Add("2F", 600, part: "단관");
        net.Connect(bend, 1, pipe, 0);

        var result = net.Run(isValveRoom: false);

        Assert.Equal([new PipeJointSlotValue("플랜지접합", string.Empty, 1)], result.FlangeSlots[pipe]);
        Assert.Empty(result.FlangeSlots[bend]);
    }

    [Fact]
    public void FlangeOwner_LargerDiameterThenSmallerId_AndBranchSpec()
    {
        var net = new Net();
        var small = net.Add("2F", 300);
        var tee = net.Add("3F", 600, endDiameters: [600, 600, 300]);
        net.Connect(small, 0, tee, 2);

        var result = net.Run(isValveRoom: false);

        Assert.Equal([new PipeJointSlotValue("플랜지접합", "D300", 1)], result.FlangeSlots[tee]);
        Assert.Empty(result.FlangeSlots[small]);
    }

    [Fact]
    public void FlangeTouchingValve_IsZero()
    {
        var net = new Net();
        var pipe = net.Add("2F", 300, part: "단관");
        var valve = net.Add("", 300, isValve: true);
        net.Connect(pipe, 1, valve, 0);

        var result = net.Run();

        // 밸브 쪽 0 + 반대편 짝 없는 플랜지 1(밸브실) — 밸브와 닿은 단부는 세지 않는다.
        Assert.Equal(1, result.FlangeSlots[pipe].Sum(x => x.Count));
        Assert.Equal(1, result.UnpairedFlangeCount);
    }

    [Fact]
    public void UnpairedFlange_ValveRoomCountsOne_PlannedCountsZero_BothWarn()
    {
        Net Build() { var n = new Net(); n.Add("1F", 450, part: "단관"); return n; }

        var room = Build().Run(isValveRoom: true);
        var planned = Build().Run(isValveRoom: false);

        Assert.Equal(1, room.FlangeSlots[1].Sum(x => x.Count));
        Assert.Equal(0, planned.FlangeSlots[1].Sum(x => x.Count));
        Assert.Single(room.Warnings);
        Assert.Single(planned.Warnings);
        Assert.Empty(planned.CutCounts);   // 계획관로 문서는 절단을 패스가 정하지 않는다
    }

    [Fact]
    public void MixedMember_EndKindsComeFromNeighbor()
    {
        var net = new Net();
        var mixed = net.Add("1S1F", 600, part: "단관");
        var flanged = net.Add("2F", 600);
        var plain = net.Add("1S", 600, part: "직관");
        net.Connect(mixed, 0, plain, 1);    // 소켓 ↔ 평단
        net.Connect(mixed, 1, flanged, 0);  // 플랜지 ↔ 플랜지

        var result = net.Run();

        Assert.Equal(1, result.FlangeSlots[mixed].Sum(x => x.Count));
        Assert.Equal(0, result.UnresolvedEndCount);
    }

    [Fact]
    public void TwoMixedMembers_CannotDecide_WarnsAndCountsZero()
    {
        var net = new Net();
        var a = net.Add("1S1F", 600, part: "단관");
        var b = net.Add("1S1F", 600, part: "단관");
        net.Connect(a, 1, b, 0);

        var result = net.Run(isValveRoom: false);

        Assert.Equal(0, result.FlangeSlots.Values.Sum(x => x.Sum(y => y.Count)));
        Assert.True(result.UnresolvedEndCount > 0);
    }

    [Fact]
    public void InletSocket_AddsCut_OutletPlainDoesNot()
    {
        var net = new Net();
        var inlet = net.Add("1S1F", 1650, part: "단관");
        var pipe = net.Add("2F", 1650, part: "단관");
        var outlet = net.Add("1F", 1650, part: "단관");
        net.Connect(inlet, 1, pipe, 0);
        net.Connect(pipe, 1, outlet, 0);

        var result = net.Run();

        Assert.Equal(1, result.CutCounts[inlet]);
        Assert.Equal(0, result.CutCounts[pipe]);
        Assert.Equal(0, result.CutCounts[outlet]);
    }

    [Fact]
    public void PlainShortPipe_BaseCutOne()
    {
        var net = new Net();
        var plainShort = net.Add("", 450, part: "단관");

        var result = net.Run();

        Assert.Equal(1, result.CutCounts[plainShort]);
    }

    [Fact]
    public void PositionMatch_PairsCoincidentEnds_AndSkipsTripleOverlap()
    {
        var members = new[]
        {
            new PipeJointMemberInput(1, false, "단관", "2F", 300),
            new PipeJointMemberInput(2, false, null, "2F", 300),
            new PipeJointMemberInput(3, false, null, "2F", 300),
        };
        // 1·2는 (0,0,0)에서 0.5mm 차이로 맞닿고, 2·3·1은 (5000,0,0)에 셋이 겹친다.
        var ends = new[]
        {
            new PipeJointEndInput(1, 0, 0, 0, 0, 300), new PipeJointEndInput(2, 0, 0.5, 0, 0, 300),
            new PipeJointEndInput(1, 1, 5000, 0, 0, 300), new PipeJointEndInput(2, 1, 5000, 0, 0, 300),
            new PipeJointEndInput(3, 0, 5000, 0.3, 0, 300), new PipeJointEndInput(3, 1, 9000, 0, 0, 300),
        };

        var result = PipeJointConnectionPass.Run(members, ends, isValveRoom: false);

        Assert.Equal(1, result.FlangeSlots[1].Sum(x => x.Count));   // (0,0,0) 짝 1건 → 관에 귀속
        Assert.Equal(3, result.AmbiguousEndCount);
    }

    [Fact]
    public void ValveBendWithoutComposition_IsTreatedAsFlanged()
    {
        var members = new[]
        {
            new PipeJointMemberInput(1, false, null, null, 300, FlangedWhenNoComposition: true),
            new PipeJointMemberInput(2, false, "단관", "2F", 300),
        };
        var ends = new[]
        {
            new PipeJointEndInput(1, 0, 0, 0, 0, 300, PartnerMemberId: 2, PartnerIndex: 0),
            new PipeJointEndInput(2, 0, 0, 0, 0, 300, PartnerMemberId: 1, PartnerIndex: 0),
            new PipeJointEndInput(1, 1, 9000, 0, 0, 300), new PipeJointEndInput(2, 1, 5000, 0, 0, 300),
        };

        var result = PipeJointConnectionPass.Run(members, ends, isValveRoom: false);

        Assert.Equal(1, result.FlangeSlots[2].Sum(x => x.Count));
    }

    [Fact]
    public void ResultIsDeterministic()
    {
        var net = new Net();
        var a = net.Add("2F", 600);
        var b = net.Add("2F", 600);
        net.Connect(a, 1, b, 0);

        Assert.Equal(net.Run().FlangeSlots[a], net.Run().FlangeSlots[a]);
        Assert.Equal([new PipeJointSlotValue("플랜지접합", string.Empty, 1)], net.Run(isValveRoom: false).FlangeSlots[a]);   // 같은 등급·관경이면 ElementId 작은 쪽
    }
}
