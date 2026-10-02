using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary><see cref="IPipeJointConnectionRepo"/>의 Revit 구현. 기록은 <see cref="PipeJointConnectionRecorder"/>에 맡긴다(docs/266 § 1).</summary>
internal sealed class RevitPipeJointConnectionRepo : IPipeJointConnectionRepo
{
    private readonly Func<Document?> _document;

    public RevitPipeJointConnectionRepo(Func<Document?> document) => _document = document;

    public IReadOnlyList<string> RecordConnectionJoints()
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 문서가 없습니다.");
        var warnings = new List<string>();
        PipeJointConnectionRecorder.Apply(document, warnings);
        return warnings;
    }
}
