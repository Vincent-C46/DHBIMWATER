using System.Security.Cryptography;
using System.Text;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 재료표 도면 내용 서명(docs/291). 기존 드래프팅뷰에 저장한 값과 같으면 다시 그리지 않는다.
/// <para>그려지는 값만 넣음: 제목·이미지 사용 여부·행(번호·명칭·규격·수량·비고·이미지 경로). SymbolTypeId는 안 그려지므로 제외.</para>
/// <para>한계: 이미지 파일 내용이 바뀌어도 경로가 같으면 감지 못 함.</para>
/// </summary>
public static class PipeMaterialTableSignature
{
    // 표 그리기 규칙(열 폭·행 높이·글자) 바꾸면 올린다 → 기존 도면 전부 한 번 다시 그림
    // v2: A1 크기 표(칸 2배) + 문자 2.5mm 유형 고정. v3: 문자 = 5 ÷ 뷰 축척 mm(A1 5, A3 2.5)
    private const string Version = "v3";
    private const char Separator = '\u001F';

    public static string Of(string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage)
    {
        var text = new StringBuilder()
            .Append(Version).Append(Separator)
            .Append(title).Append(Separator)
            .Append(includeTypeImage ? '1' : '0');
        foreach (var row in rows)
            text.Append('\n')
                .Append(row.ItemNumber).Append(Separator)
                .Append(row.Name).Append(Separator)
                .Append(row.Spec).Append(Separator)
                .Append(row.Count).Append(Separator)
                .Append(row.Remark).Append(Separator)
                .Append(includeTypeImage ? row.TypeImagePath ?? "" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
