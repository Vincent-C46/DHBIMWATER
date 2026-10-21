using DHBIMWATER.Application.DTOs.Gis;

namespace DHBIMWATER.Application.Interfaces.Gis;

/// <summary>엑셀 소스는 시트·헤더행·열 매핑을 사용자가 지정해야 하므로, 기본 Read(path) 대신 매핑을 받는 오버로드를 추가로 제공한다.</summary>
public interface IExcelAlignmentSourceReader : IAlignmentSourceReader
{
    IReadOnlyList<string> GetSheetNames(string filePath);

    /// <summary>매핑 대화상자 미리보기용. 1행부터 maxRows행까지 셀 값을 문자열로 반환한다.</summary>
    IReadOnlyList<IReadOnlyList<string?>> PreviewRows(string filePath, string sheetName, int maxRows);

    ShapefileReadResult Read(string filePath, ExcelAlignmentMapping mapping);

    /// <summary>밸브실 위치 자동지정용. 측점 열이 매핑돼 있어야 한다(docs/300 § B-2).</summary>
    AlignmentProfileReadResult ReadProfile(string filePath, ExcelAlignmentMapping mapping);

    /// <summary>선형 시트에서 밸브실 유형 칸이 채워진 행을 밸브실 목록으로 읽는다(docs/304).
    /// Z = 관저고, 직경(DN)·유형·원지반 열 매핑 필수. 밸브실 행이 0개면 VR-506.</summary>
    ValveRoomListReadResult ReadValveRoomList(string filePath, ExcelAlignmentMapping mapping);
}
