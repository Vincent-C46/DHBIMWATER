using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>격점마다 시트 1장 + 평면·3D·드래프팅(배관재료표 도면) 뷰 배치.
/// 평면·도면 UseCase 다음에 따로 트랜잭션 — 시트 실패해도 앞 뷰 안 되돌림.</summary>
public sealed class CreateJunctionDetailSheetUseCase
{
    public const string ThreeDPrefix = CreateJunctionDetailViewUseCase.NamePrefix + "_3D";
    public const string SheetNumberPrefix = "격점";

    private readonly ITransactionContext _transaction;
    private readonly IJunctionDetailRepo _junctions;
    private readonly IJunctionDetailSheetRepo _repo;

    public CreateJunctionDetailSheetUseCase(ITransactionContext transaction, IJunctionDetailRepo junctions,
        IJunctionDetailSheetRepo repo)
    {
        _transaction = transaction;
        _junctions = junctions;
        _repo = repo;
    }

    public JunctionDetailSheetResult Execute()
    {
        var groups = _junctions.GetJunctions();
        var created = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();
        var skipped = new List<string>();
        var warnings = new List<string>();
        if (groups.Count == 0) return new JunctionDetailSheetResult(created, updated, unchanged, skipped, warnings);

        using (_transaction)
        {
            try
            {
                _transaction.Begin("격점상세도 시트 생성·갱신");
                foreach (var group in groups)
                {
                    var request = BuildRequest(group);
                    // 격점 하나 실패해도 나머지 살림. Repo가 SubTransaction으로 그 격점만 되돌림
                    try
                    {
                        var change = _repo.CreateOrUpdateSheet(request);
                        warnings.AddRange(change.Warnings);
                        (change.Status switch
                        {
                            JunctionDetailSheetStatus.Created => created,
                            JunctionDetailSheetStatus.Updated => updated,
                            _ => unchanged
                        }).Add(request.SheetName);
                    }
                    catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
                    catch (Exception ex) { skipped.Add($"{request.SheetName}: {ex.Message}"); }
                }
                _transaction.Commit();
            }
            catch
            {
                _transaction.Rollback();
                throw;
            }
        }
        return new JunctionDetailSheetResult(created, updated, unchanged, skipped,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    // 이름 규약: 평면 = CreateJunctionDetailViewUseCase, 드래프팅 = CreatePipeMaterialDraftingUseCase와 같아야 찾음
    internal static JunctionDetailSheetRequest BuildRequest(JunctionDetailGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.JunctionId))
            return new JunctionDetailSheetRequest(group, $"{CreateJunctionDetailViewUseCase.NamePrefix} (미지정)",
                $"{SheetNumberPrefix}_미지정", "격점상세도_미지정", CreatePipeMaterialDraftingUseCase.UnassignedName,
                $"{ThreeDPrefix}_미지정");
        var id = group.JunctionId;
        return new JunctionDetailSheetRequest(group,
            $"{CreateJunctionDetailViewUseCase.NamePrefix} ({PipeMaterialViewName.Sanitize(id)})",
            PipeMaterialViewName.For(SheetNumberPrefix, id),
            PipeMaterialViewName.For(CreateJunctionDetailViewUseCase.NamePrefix, id),
            PipeMaterialViewName.For(CreatePipeMaterialDraftingUseCase.NamePrefix, id),
            PipeMaterialViewName.For(ThreeDPrefix, id));
    }
}
