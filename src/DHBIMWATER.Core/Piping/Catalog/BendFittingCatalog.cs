using DHBIMWATER.Core.Gis;
using System.Text.Json.Serialization;

namespace DHBIMWATER.Core.Piping;

/// <summary>적용 규격이 정의하는 이형관 형식 구분. Joint Type과는 별개다.</summary>
public enum BendForm { AType, BType }

/// <summary>이형관 접합 방식. 핸드북에서 소켓곡관과 플랜지곡관은 R·t가 다른 별도 표다(e·s는 같다).
/// 강관에서 <see cref="Socket"/>은 <b>용접</b>을 뜻한다(접합 슬롯 종류와 같은 규약).</summary>
public enum BendConnection { Socket, Flanged }

/// <summary>KS D 3578 강관 이형관의 종류(F12/F15/F20). 관두께 T와 무게만 다르고 외경·R·L은 같다.
/// 주철관 행은 <see cref="None"/>이다. 직관 등급(STWW)과 대응시키지 않는다 — 사용자가 고른다(docs/211 § 0 확정 2).</summary>
public enum SteelFittingClass { None, F12, F15, F20 }

/// <param name="DiameterMm">호칭지름 DN. 조회는 Exact Match다.</param>
/// <param name="Form">
/// A형(양쪽 소켓)/B형(한쪽 소켓). 핸드북상 e·R·t는 형식과 무관하게 같고 s(추가 다리 길이)·무게만 다르다
/// (2026-08-19 확인). 행 하나가 한 형식만 대표하므로 기본은 B형이고, A형이 필요한 자리만 사용자가 바꾼다.
/// </param>
/// <param name="WallThicknessMm">e — 곡관 자체의 규격상 벽두께(mm).</param>
/// <param name="WeightKpMechanicalKg">KP 메커니컬 조인트 기준 무게(kg). 현재 <see cref="Form"/> 값에 대응한다.</param>
/// <param name="WeightTytonKg">타이튼 조인트 기준 무게(kg). 현재 <see cref="Form"/> 값에 대응한다.</param>
/// <param name="Material">
/// 이 규격이 속한 관종. DN·각도만으로는 관종이 구분되지 않아 조회 키에 필요하다.
/// 기본값이 있으므로 관종 축 도입 이전에 저장된 JSON도 그대로 역직렬화된다.
/// </param>
/// <param name="Connection">소켓/플랜지. 기본값이 있어 이 축 도입 이전 JSON도 소켓으로 읽힌다.</param>
/// <param name="FittingClass">강관 이형관 종류(F12/F15/F20). 주철관 행은 None. 기본값이 있어 이 축 도입 이전 JSON도 그대로 읽힌다(docs/211 § C-2).</param>
/// <param name="WeightKg">접합 종류와 무관한 한 개 무게(kg) — 강관 곡관과 주철관 플랜지곡관(B형, docs/260)용.
/// 주철관 소켓 행은 0이며 KP/타이튼 무게 열을 쓴다.</param>
public sealed record BendFittingEntry(
    double DiameterMm,
    double AngleDeg,
    BendForm Form,
    double LayingLengthMm,
    double CenterlineRadiusMm,
    double WallThicknessMm = 0d,
    double WeightKpMechanicalKg = 0d,
    double WeightTytonKg = 0d,
    PipeMaterial Material = PipeMaterial.DuctileIron,
    BendConnection Connection = BendConnection.Socket,
    SteelFittingClass FittingClass = SteelFittingClass.None,
    double WeightKg = 0d)
{
    /// <summary>s — B형(한쪽 소켓)만 200mm, A형(양쪽 소켓)은 0. 접합 종류와 무관하다(2026-09-04 확정).
    /// 강관은 소켓이 없어 형식과 무관하게 0이다(docs/211 § C-2).</summary>
    [JsonIgnore]
    public double ExtraLegLengthMm => Material == PipeMaterial.Steel ? 0d : Form == BendForm.BType ? 200d : 0d;
}

public sealed class BendFittingCatalog
{
    private const double Epsilon = 1e-6;

    public BendFittingCatalog(IReadOnlyList<BendFittingEntry> entries) => Entries = entries;
    public IReadOnlyList<BendFittingEntry> Entries { get; }

    /// <summary>
    /// 저장된 곡관표가 현행 규격 구조와 맞지 않아 [기본값 복원] 안내가 필요한 상태인지.
    /// A형·B형 이중행 또는 플랜지 행이 없는 구 규격표를 감지한다.
    /// </summary>
    public bool NeedsRestore => Entries.GroupBy(x => (x.Material, x.Connection, x.FittingClass, x.DiameterMm, x.AngleDeg)).Any(x => x.Count() > 1)
        || !Entries.Any(x => x.Connection == BendConnection.Flanged);

    /// <summary>DN·각도·관종·접합종류(·강관 종류)로 조회한다. 형식(A/B)은 행 자체가 갖고 있으므로 조회 키가 아니다.</summary>
    public BendFittingEntry? Find(double diameterMm, double angleDeg, PipeMaterial material = PipeMaterial.DuctileIron,
        BendConnection connection = BendConnection.Socket, SteelFittingClass fittingClass = SteelFittingClass.None) => Entries.FirstOrDefault(x =>
        x.Material == material
        && x.Connection == connection
        && x.FittingClass == fittingClass
        && Math.Abs(x.DiameterMm - diameterMm) <= Epsilon
        && Math.Abs(x.AngleDeg - angleDeg) <= Epsilon);

    /// <summary>강관 행이 하나도 없으면 기본 강관 행을 뒤에 붙인 사본을, 있으면 자신을 돌려준다(docs/211 § E-1).</summary>
    public BendFittingCatalog WithSteelDefaults()
        => Entries.Any(x => x.Material == PipeMaterial.Steel) ? this : new BendFittingCatalog(Entries.Concat(SteelDefaultEntries).ToList());

    public static IReadOnlyList<double> AnglesFor(PipeMaterial material, BendConnection connection)
        => PipeCatalog.Current.StandardAngles.TryGetValue((material, connection), out var angles) ? angles : [];

    public static double? SteelWallThicknessOf(double diameterMm, SteelFittingClass fittingClass)
        => PipeCatalog.Current.BendHandbookEntries.FirstOrDefault(x => x.Material == PipeMaterial.Steel
            && x.FittingClass == fittingClass && Math.Abs(x.DiameterMm - diameterMm) <= Epsilon)?.WallThicknessMm;

    public static IReadOnlyList<BendFittingEntry> SteelDefaultEntries => PipeCatalog.Current.SteelBendDefaults;
    public static BendFittingCatalog Default => PipeCatalog.Current.Fittings;

}

/// <summary>관로 규격과 판정 설정의 저장 단위.</summary>
public sealed record BendSettings(
    StraightPipeSpecTable StraightPipes,
    JointDeflectionTable JointDeflections,
    BendFittingCatalog Fittings,
    string ActiveJointType,
    /// <summary>배치에 쓸 곡관 접합 종류. 모델링 창에서 고르며 기본은 소켓이다.</summary>
    BendConnection ActiveBendConnection = BendConnection.Socket,
    /// <summary>배치에 쓸 곡관 형식. A형(양쪽 소켓)은 s=0·양쪽 Joint, B형(한쪽 소켓)은 s=200·한쪽 Joint를 결정하며 접합 종류와 무관하다(2026-09-04 확정).</summary>
    BendForm ActiveBendForm = BendForm.BType,
    /// <summary>주철관 소켓 접합에 <b>이탈방지압륜</b>을 쓰는지(docs/166 § 11-4).
    /// <para>산출서에서 `이탈방지압륜 접합 및 부설`은 `KP접합 및 부설`과 별개 공종이라 개소를 다른 열에 적어야 한다.
    /// 켜면 주철관 소켓 부재의 접합 슬롯 `종류`가 <c>이탈방지압륜접합</c>, 끄면 <c>KP접합</c>이 된다.</para>
    /// <para>형상·허용굴곡에는 <b>영향이 없다</b> — 압륜은 같은 KP 메커니컬 조인트에 덧대는 부속이다.
    /// 강관·플랜지 접합에도 영향이 없다.</para></summary>
    bool UseRestraintGland = false,
    /// <summary>강관 곡관·이형관에 쓸 종류(F12/F15/F20). 직관 등급(STWW)과 무관하게 모델링 창에서 고른다(docs/211 § 0 확정 2).
    /// 주철관 절점에는 영향이 없다.</summary>
    SteelFittingClass ActiveSteelFittingClass = SteelFittingClass.F12,
    /// <summary>강관 곡관의 접합 적용(편측 1개소 · 양측 2개소). 슬롯 수와 접합구성(1S/2S)만 정한다 — 강관 허용굴곡은 늘 한쪽 Joint로 판정한다.
    /// <para>강관에는 A/B형이 없어 <see cref="ApplicationMode"/>(곡관 형식 유도)와 분리한다 — 공유하면 주철관 형상(s=0/200)과 간섭한다(docs/243 § B).</para></summary>
    JointApplicationMode SteelApplicationMode = JointApplicationMode.SingleJoint)
{
    /// <summary>허용굴곡 적용 방식은 곡관 형식이 결정한다. A형은 양쪽 Joint(θ₁+θ₂), B형은 한쪽 Joint.</summary>
    public JointApplicationMode ApplicationMode =>
        ActiveBendForm == BendForm.AType ? JointApplicationMode.BothJoints : JointApplicationMode.SingleJoint;

    public static BendSettings Default { get; } = new(
        StraightPipeSpecTable.Default,
        JointDeflectionTable.Default,
        BendFittingCatalog.Default,
        JointTypeCatalog.KpMechanical);

    /// <summary>직관표·곡관표에 강관 행이 전무하면 기본 강관 행을 붙인 사본을 돌려준다(docs/211 § E-1).
    /// 기존 프로젝트 저장본에는 강관 행이 없는데 [기본값 복원]을 강요하면 사용자가 손본 주철관 값이 날아가므로 병합한다.
    /// 강관 행이 하나라도 남아 있으면 사용자 편집으로 보고 건드리지 않는다.</summary>
    public BendSettings WithSteelDefaults()
    {
        var straight = StraightPipes.WithSteelDefaults();
        var fittings = Fittings.WithSteelDefaults();
        return ReferenceEquals(straight, StraightPipes) && ReferenceEquals(fittings, Fittings)
            ? this
            : this with { StraightPipes = straight, Fittings = fittings };
    }
}
