namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>모델 생성 전 입력 문제 1건. 클릭하면 탭 이동·강조한다.</summary>
public sealed record PreflightIssue(PreflightSeverity Severity, string Message, PipeAlignmentTab TargetTab, string? HighlightKey);

public enum PreflightSeverity { Blocking, Warning }

public enum PipeAlignmentTab { Input, OutputMode, SpecMapping, Direction, Diagnosis }
