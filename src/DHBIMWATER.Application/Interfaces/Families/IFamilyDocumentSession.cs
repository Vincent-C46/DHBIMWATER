namespace DHBIMWATER.Application.Interfaces.Families;

/// <summary>
/// 백그라운드로 연 패밀리 문서 하나(docs/309 § 6 Q1).
/// <para><see cref="ITransactionContext"/>는 활성 문서 전용이라 따로 둔다. 트랜잭션은 UseCase가 이 세션으로 연다.</para>
/// <para>Dispose = 저장 없이 닫기. 저장은 <see cref="Save"/>로만.</para>
/// </summary>
public interface IFamilyDocumentSession : IDisposable
{
    string Path { get; }
    void Begin(string name);
    void Commit();
    void Rollback();
    /// <summary>원본 덮어쓰기. Revit이 남긴 새 백업(<c>*.0001.rfa</c>)은 지운다.</summary>
    void Save();
}

public interface IFamilyDocumentSessionFactory
{
    /// <summary>화면에 띄우지 않고 연다. 패밀리 문서가 아니면 닫고 예외.</summary>
    IFamilyDocumentSession Open(string rfaPath);

    /// <summary>파일이 저장된 Revit 버전이 현재보다 낮으면 그 버전 문자열, 같거나 읽지 못하면 null.</summary>
    string? GetOlderVersion(string rfaPath);
}

/// <summary>열린 패밀리 문서의 유형 이미지 쓰기. 트랜잭션은 호출자(UseCase)가 연다.</summary>
public interface IFamilyTypeImageRepo
{
    /// <summary>이미지를 Import해 모든 유형에 지정하고, 안 쓰게 된 이전 이미지를 지운다. 반환 = 지정한 유형 수.</summary>
    int Apply(IFamilyDocumentSession session, string imagePath);
}

/// <summary>패밀리 폴더별 매핑 저장(JSON). 다음에 같은 폴더를 열면 복원.</summary>
public interface ITypeImageMappingStore
{
    Core.Families.TypeImageMapping Load(string familyFolder);
    void Save(string familyFolder, Core.Families.TypeImageMapping mapping);
}
