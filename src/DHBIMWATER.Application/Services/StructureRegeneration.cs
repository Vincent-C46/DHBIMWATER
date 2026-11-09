using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Application.Services;

/// <summary>구조물 재생성 절차(docs/320 § C): 확인 → 기존 부재 삭제 → 생성 전후 차이에 구조물 표시.
/// <para>DH_Zone은 구분 불가(펌프장도 "밸브실"·빈 값 사용), 개구부는 ID 안 돌려줌, 계단은 Transaction 밖 생성 —
/// 정의마다 표시 대신 전후 차이로 표시.</para></summary>
public sealed class StructureRegeneration
{
    public const string ParameterName = "DH_Structure";
    public const string PumpingStation = "펌프장";
    public const string DischargeBasin = "토출조";
    public const string Reservoir = "배수지";

    private readonly IStructureModelRepo _repo;
    private readonly string _structure;
    private readonly IReadOnlyList<long> _existing;
    private HashSet<long> _baseline = new();

    private StructureRegeneration(IStructureModelRepo repo, string structure, IReadOnlyList<long> existing)
    {
        _repo = repo;
        _structure = structure;
        _existing = existing;
    }

    /// <summary>Transaction 전 호출. 기존 부재 있으면 개수 확인. 취소면 null.</summary>
    public static StructureRegeneration? Prepare(IStructureModelRepo repo, IDialogService dialog, string structure)
    {
        var existing = repo.GetStructureElementIds(structure);
        if (existing.Count > 0 && !dialog.Confirm("재생성 확인",
                $"기존 {structure} 부재 {existing.Count}개를 삭제하고 다시 생성합니다.\r\n" +
                "수동으로 변경한 내용과 이 부재에 달린 치수·태그도 사라집니다."))
            return null;
        return new StructureRegeneration(repo, structure, existing);
    }

    /// <summary>Transaction 안, 공유 매개변수 준비 뒤 호출. 기존 부재 삭제 후 기준 스냅샷.</summary>
    public void DeleteExisting()
    {
        if (_existing.Count > 0) _repo.DeleteElements(_existing);
        _baseline = _repo.GetAddinElementIds().ToHashSet();
    }

    /// <summary>Transaction 안, 생성 뒤 호출. 스냅샷에 없는 부재만 표시.</summary>
    public void MarkCreated()
        => _repo.MarkStructure(_repo.GetAddinElementIds().Where(id => !_baseline.Contains(id)).ToList(), _structure);
}
