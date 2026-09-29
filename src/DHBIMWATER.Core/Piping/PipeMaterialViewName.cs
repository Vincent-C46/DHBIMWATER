namespace DHBIMWATER.Core.Piping;

/// <summary>격점번호로 Revit 뷰 이름을 만든다(docs/208 § B). 일람표·도면 UseCase가 같은 규칙을 써야
/// 같은 격점의 두 뷰 이름이 어긋나지 않는다.
/// <para>격점번호는 자유 입력(PipeLayoutViewModel.JunctionId·속성창)이라 Revit 뷰 이름 금지 문자가 들어올 수 있다.
/// 금지 문자는 '_'로 바꾸고 앞뒤 공백은 잘라 쓴다. <b>필터 값과 표 비교 키는 원문을 그대로 쓴다</b> — 이름만 정제한다.</para></summary>
public static class PipeMaterialViewName
{
    // Revit이 뷰 이름에 허용하지 않는 문자(RevitAPI: "Names cannot contain \ : { } [ ] | ; < > ? ` ~").
    private static readonly char[] Forbidden = ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'];

    public static string For(string prefix, string junctionId)
    {
        var trimmed = junctionId.Trim();
        var chars = trimmed.Select(c => Array.IndexOf(Forbidden, c) >= 0 ? '_' : c).ToArray();
        return $"{prefix}_{new string(chars)}";
    }
}
