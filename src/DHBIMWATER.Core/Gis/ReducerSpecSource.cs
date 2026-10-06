using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Core.Gis;

public enum ReducerAlignment { Concentric, Eccentric }

/// <summary>주철 소켓 편락관 형식(핸드북 10항 그림). A형 = 테이퍼 L만, B형 = 소경 쪽 직관부 200, C형 = 대경 쪽 직관부 200(docs/260 § G).</summary>
public enum ReducerForm { AType, BType, CType }

/// <param name="BodyLengthMm">이경관 전체 길이(대경 원통 + 테이퍼 + 소경 원통). 트림 계산은 이 값만 본다.</param>
/// <param name="LargeStraightMm">대경 쪽 원통 길이 — 주철 C형 200, 강관 C. 없으면 0.</param>
/// <param name="SmallStraightMm">소경 쪽 원통 길이 — 주철 B형 200, 강관 E. 없으면 0.</param>
/// <param name="IsFallback">카탈로그에 조합이 없어 사용자 입력 대체 길이를 쓴 경우(<see cref="CatalogReducerSpecSource"/>만 켠다).
/// 고정 길이 소스(<see cref="FixedReducerSpecSource"/>)는 대체가 아니므로 false다.</param>
public sealed record ReducerSpec(double BodyLengthMm, ReducerAlignment Alignment,
    double LargeStraightMm = 0d, double SmallStraightMm = 0d,
    bool IsFallback = false)
{
    /// <summary>원뿔(테이퍼) 구간 길이.</summary>
    public double TaperLengthMm => BodyLengthMm - LargeStraightMm - SmallStraightMm;
}

public interface IReducerSpecSource
{
    ReducerSpec? Find(string pipeKind, double largeDiameterMm, double smallDiameterMm);
}

public sealed class FixedReducerSpecSource : IReducerSpecSource
{
    private readonly double _bodyLengthMm;
    private readonly ReducerAlignment _alignment;

    public FixedReducerSpecSource(double bodyLengthMm, ReducerAlignment alignment)
    { _bodyLengthMm = bodyLengthMm; _alignment = alignment; }

    public ReducerSpec? Find(string pipeKind, double largeDiameterMm, double smallDiameterMm)
        => _bodyLengthMm > 0d ? new ReducerSpec(_bodyLengthMm, _alignment) : null;
}

/// <summary>핸드북 편락관 카탈로그로 이경관 길이를 정한다(docs/260 § G, 2026-10-02 사용자 결정).
/// <list type="bullet">
/// <item>주철 소켓: 소켓 편락관 표의 L. B·C형은 L + 200(직관부).</item>
/// <item>주철 플랜지: 플랜지 편락관 표의 L(형식 무시).</item>
/// <item>강관: 편락관 표(docs/261)의 C + W + E = L 형상. 형식 선택은 무관하다.</item>
/// <item>표에 없는 대·소경 조합: 사용자 입력 대체 길이, <see cref="ReducerSpec.IsFallback"/> true.</item>
/// </list>
/// 편심 이경관도 동심 표를 쓴다 — 핸드북에 편심 표가 없다(사용자 결정).</summary>
public sealed class CatalogReducerSpecSource : IReducerSpecSource
{
    private readonly BendConnection _connection;
    private readonly string? _jointType;
    private readonly ReducerForm _form;
    private readonly double _fallbackLengthMm;
    private readonly ReducerAlignment _alignment;
    private readonly SteelFittingClass _steelClass;

    public CatalogReducerSpecSource(BendConnection connection, string? jointType, ReducerForm form,
        double fallbackLengthMm, ReducerAlignment alignment, SteelFittingClass steelClass = SteelFittingClass.F12)
    {
        _connection = connection; _jointType = jointType; _form = form;
        _fallbackLengthMm = fallbackLengthMm; _alignment = alignment; _steelClass = steelClass;
    }

    public ReducerSpec? Find(string pipeKind, double largeDiameterMm, double smallDiameterMm)
    {
        var material = PipeKindCatalog.MaterialOf(pipeKind);
        if (material == PipeMaterial.Steel && SteelFittingSpecTable.FindReducer(largeDiameterMm, smallDiameterMm) is { } steel)
            return new ReducerSpec(steel.L, _alignment, steel.C, steel.SmallStraightMm);
        if (material == PipeMaterial.DuctileIron)
        {
            if (_connection == BendConnection.Flanged)
            {
                if (DuctileFittingSpecTable.FindFlangedTaper(largeDiameterMm, smallDiameterMm) is { } flanged)
                    return new ReducerSpec(flanged.L, _alignment);
            }
            else if (DuctileFittingSpecTable.FindSocketTaper(largeDiameterMm, smallDiameterMm) is { } socket)
            {
                var straight = DuctileFittingSpecTable.TaperStraightLengthMm;
                var large = _form == ReducerForm.CType ? straight : 0d;
                var small = _form == ReducerForm.BType ? straight : 0d;
                return new ReducerSpec(socket.L + large + small, _alignment, large, small);
            }
        }
        return _fallbackLengthMm > 0d ? new ReducerSpec(_fallbackLengthMm, _alignment, IsFallback: true) : null;
    }

}
