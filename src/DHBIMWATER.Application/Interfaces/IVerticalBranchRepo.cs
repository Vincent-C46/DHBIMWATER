using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>수직분기 시작 부속 피킹(docs/301 § A). Revit API 컨텍스트(ExternalEvent)에서만 부른다.</summary>
public interface IVerticalBranchPickRepo
{
    /// <summary>Esc면 null. 열린 +Z 커넥터가 없는 부속이면 <c>DhbimwaterException</c>.</summary>
    VerticalBranchStart? PickStart();
}

/// <summary>수직분기 배치(docs/301 § B). 트랜잭션은 UseCase가 연다.</summary>
public interface IVerticalBranchCommandRepo
{
    VerticalBranchResult Place(VerticalBranchRequest request);
}
