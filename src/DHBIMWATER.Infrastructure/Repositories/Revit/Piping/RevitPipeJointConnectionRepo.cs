using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary><see cref="IPipeJointConnectionRepo"/>의 Revit 구현. 기록은 <see cref="PipeJointConnectionRecorder"/>에 맡긴다(docs/266 § 1).</summary>
internal sealed class RevitPipeJointConnectionRepo : IPipeJointConnectionRepo
{
    private readonly Func<Document?> _document;
    private readonly IPipeQuantityRules _rules;

    public RevitPipeJointConnectionRepo(Func<Document?> document, IPipeQuantityRules rules)
    { _document = document; _rules = rules; }

    public IReadOnlyList<string> RecordConnectionJoints()
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 문서가 없습니다.");
        var warnings = new List<string>();
        PipeJointConnectionRecorder.Apply(document, warnings, _rules);
        return warnings;
    }
}
