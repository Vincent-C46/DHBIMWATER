namespace DHBIMWATER.Core.Piping;

public static class JunctionDetailParameters
{
    public const string SymbolOffsetX = "심볼오프셋X";
    public const string SymbolOffsetY = "심볼오프셋Y";
    public const string SymbolOffsetZ = "심볼오프셋Z";
    public const double StepMm = 400;
    // 창 미리보기 캔버스 축척. 시트 축척과 무관하게 고정
    public const int ViewScale = 50;
    // 시트에 놓는 평면·3D 뷰 기본 축척(A1 도곽 기준). 창에서 바꿀 수 있음
    public const int SheetScale = 25;
    // 새 시트 기본 도곽 = 이름에 이 단어 든 제목 블록
    public const string DefaultTitleBlockKeyword = "A1";

    // 패밀리 매개변수 이름 공백 차이 무시. 대소문자는 구분 (docs/286)
    public static bool NameMatches(string? actual, string expected)
        => actual is not null && string.Equals(Compact(actual), Compact(expected), StringComparison.Ordinal);

    private static string Compact(string value) => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
}
