namespace DHBIMWATER.Application.Interfaces;

/// <summary>구조물 단위 부재 조회·삭제·표시(docs/320 § C). Transaction은 호출부(UseCase)가 연다.</summary>
public interface IStructureModelRepo
{
    /// <summary>DH_Addin=DHBIMWATER 인스턴스 전체. 생성 전후 차이로 새 부재를 찾는 기준.</summary>
    IReadOnlyList<long> GetAddinElementIds();

    /// <summary>DH_Addin=DHBIMWATER 이고 DH_Structure=<paramref name="structure"/> 인 인스턴스.</summary>
    IReadOnlyList<long> GetStructureElementIds(string structure);

    void DeleteElements(IReadOnlyCollection<long> elementIds);

    /// <summary>DH_Structure 값 기록. 바인딩 없는 부재는 건너뜀.</summary>
    void MarkStructure(IEnumerable<long> elementIds, string structure);
}
