using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>타이밸브실 최소 폭용 패밀리 실측(docs/323). 바이패스 배치(<see cref="MeasureBypassLayout"/>)와 같은 프로브를 써서
/// 창에서 계산한 최소 폭과 실제 생성 결과가 어긋나지 않게 한다. 프로브는 놓고 바로 지운다 — 트랜잭션은 UseCase가 열고 되돌린다.</summary>
internal sealed partial class RevitPipeSegmentCommandRepo
{
    private const string GateKeyword = "게이트";
    private const string ButterflyKeyword = "버터플라이";

    /// <summary>타이밸브실 프리셋과 같은 구성으로 잰다 — 바이패스 = 게이트 밸브 2개 + 단관 300(docs/322), 본관 밸브 = 버터플라이, 절점 = T형관.</summary>
    internal static TieValveRoomProbe MeasureTieValveRoom(Document document, PipeSegmentFamilySelection families,
        double mainMm, PipeMaterial material, SteelFittingClass steelClass)
    {
        var symbols = new FamilySymbolCache(document);
        var branchMm = ValveBypassSpecTable.BranchDiameterMm(material, steelClass, mainMm)
            ?? throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound, FormattableString.Invariant(
                $"핸드북 밸브부관 규격에 없는 본관입니다(DN{mainMm:0.#}, 규격 범위 {ValveBypassSpecTable.RangeText(material, steelClass)})."));
        var gate = AccessoryFamilyContaining(document, GateKeyword);
        var butterfly = AccessoryFamilyContaining(document, ButterflyKeyword);

        // 1) 바이패스 세트 — 배치와 같은 키(밸브 2개, 단관 300)
        var layout = MeasureBypassLayout(document, symbols, families,
            new BypassLayoutKey(gate, mainMm, branchMm, gate, BypassSet.DefaultSpoolLengthMm));

        // 2) 절점 T형관 — 주·분기 같은 구경으로 구동(배치와 같은 규칙). MeasureTee로 분기 커넥터 거리
        var teeSymbol = symbols.Resolve(families.TeeFamilyTypeName, "T형관");
        var teeProbe = PlaceAt(document, teeSymbol, XYZ.Zero, 0, null);
        TeeGeometry tee;
        try
        {
            var warnings = new List<string>();
            SetOptionalDiameterParameter(teeProbe, families.TeeDiameterParameterName, mainMm, "T형관", warnings);
            SetOptionalDiameterParameter(teeProbe, families.TeeBranchDiameterParameterName, mainMm, "T형 분기", warnings);
            document.Regenerate();
            tee = MeasureTee(teeProbe);
        }
        finally { document.Delete(teeProbe.Id); document.Regenerate(); }

        // 3) 본관 버터플라이 — 본관 DN 유형
        var butterflySymbol = symbols.FindByNominalDiameter(butterfly, mainMm)
            ?? throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound, FormattableString.Invariant(
                $"버터플라이 밸브 '{butterfly}'에 {mainMm:0}A 유형이 없습니다. 그 규격 유형을 프로젝트에 로드하세요."));
        var butterflyWidth = InlineConnectorSpanMm(document, butterflySymbol, ValveDiameterParameterNameFor(families, butterflySymbol),
                mainMm, "버터플라이 밸브").WidthMm
            ?? throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, "버터플라이 밸브는 2포트 인라인 패밀리여야 합니다.");

        // 바이패스 빠짐 = A 분기 커넥터 + B 짧은 레그(코너 = 바이패스 관 중심선)
        return new TieValveRoomProbe(mainMm, tee.BranchMm, layout.TeeCenterOffsetMm, layout.TeeMainHalfMm,
            layout.TeeBranchMm + layout.BendTeeLegMm, layout.BypassDiameterMm, butterflyWidth);
    }

    /// <summary>이름에 <paramref name="keyword"/>가 든 배관 밸브류 패밀리명(이름 순 첫 값). 추천 배치의 밸브 선택 규칙과 같다. 없으면 예외.</summary>
    private static string AccessoryFamilyContaining(Document document, string keyword)
        => new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_PipeAccessory)
               .Cast<FamilySymbol>().Select(x => x.FamilyName)
               .Where(x => x.Contains(keyword, StringComparison.Ordinal))
               .Distinct().OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault()
           ?? throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound,
               $"이름에 '{keyword}'가 든 배관 밸브류 패밀리가 프로젝트에 없습니다. 로드한 뒤 다시 시도하세요.");
}
