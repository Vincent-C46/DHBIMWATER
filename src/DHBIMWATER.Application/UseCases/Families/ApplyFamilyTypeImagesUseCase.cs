using DHBIMWATER.Application.Interfaces.Families;
using DHBIMWATER.Core.Families;

namespace DHBIMWATER.Application.UseCases.Families;

/// <summary>
/// 패밀리 파일 일괄 유형 이미지 지정(docs/309 § 3-6).
/// 파일마다: 열기 → 트랜잭션(Import·지정·이전 이미지 삭제) → 저장 → 닫기. 한 파일 실패는 다음 파일을 막지 않음.
/// </summary>
public sealed class ApplyFamilyTypeImagesUseCase
{
    private readonly IFamilyDocumentSessionFactory _sessions;
    private readonly IFamilyTypeImageRepo _repo;

    public ApplyFamilyTypeImagesUseCase(IFamilyDocumentSessionFactory sessions, IFamilyTypeImageRepo repo)
    {
        _sessions = sessions;
        _repo = repo;
    }

    /// <summary>실행 전 확인창용 — 저장하면 현재 버전으로 올라가는 구버전 파일 수.</summary>
    public int CountOlderVersion(IEnumerable<string> familyPaths) => familyPaths.Count(p => _sessions.GetOlderVersion(p) is not null);

    /// <param name="onResult">파일 하나 끝날 때마다 호출. 창 상태 칸 갱신용.</param>
    public IReadOnlyList<FamilyTypeImageResult> Execute(IReadOnlyList<(string FamilyPath, string ImagePath)> jobs, Action<FamilyTypeImageResult>? onResult = null)
    {
        var results = new List<FamilyTypeImageResult>(jobs.Count);
        foreach (var (familyPath, imagePath) in jobs)
        {
            var result = ApplyOne(familyPath, imagePath);
            results.Add(result);
            onResult?.Invoke(result);
        }
        return results;
    }

    private FamilyTypeImageResult ApplyOne(string familyPath, string imagePath)
    {
        if (!File.Exists(imagePath)) return new(familyPath, false, 0, $"이미지 파일 없음: {imagePath}");
        try
        {
            using var session = _sessions.Open(familyPath);
            int count;
            session.Begin("유형 이미지 지정");
            try
            {
                count = _repo.Apply(session, imagePath);
                session.Commit();
            }
            catch
            {
                session.Rollback();
                throw;
            }
            session.Save();
            return new(familyPath, true, count, $"완료 · 유형 {count}개");
        }
        catch (Exception ex)
        {
            return new(familyPath, false, 0, ex.Message);
        }
    }
}
