namespace DHBIMWATER.Core.Structures
{
    /// <summary>기둥 유형 찾기·만들기 사양(mm). B = X 폭, H = Y 깊이, Name = 유형 이름(docs/325 § C).</summary>
    public record ColumnTypeSpec(double B, double H, string Name);
}
