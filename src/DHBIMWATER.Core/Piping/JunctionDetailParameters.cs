namespace DHBIMWATER.Core.Piping;

public static class JunctionDetailParameters
{
    public const string SymbolOffsetX = "심볼오프셋X";
    public const string SymbolOffsetY = "심볼오프셋Y";
    public const string SymbolOffsetZ = "심볼오프셋Z";
    public const double StepMm = 400;
    public const int ViewScale = 50;

    // 패밀리 매개변수 이름 공백 차이 무시. 대소문자는 구분 (docs/286)
    public static bool NameMatches(string? actual, string expected)
        => actual is not null && string.Equals(Compact(actual), Compact(expected), StringComparison.Ordinal);

    private static string Compact(string value) => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
}
