using Autodesk.Revit.UI;
using DHBIMWATER.Infrastructure.Logging;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

/// <summary>예외를 "[코드] 메시지" 규약으로 사용자에게 보여주고 로그에 남긴다(docs/205 § A-4).
/// <para><see cref="DhbimwaterException"/>은 자기 코드로, 그 외(Revit·.NET)는 <see cref="ErrorCodes.Unclassified"/>로 표시한다.
/// 스택은 대화상자가 아니라 로그 파일(%LOCALAPPDATA%\DHBIMWATER\Logs\DHBIMWATER.log)에만 쓴다 — 사용자는 코드와 첫 줄만 보면 된다.</para></summary>
internal static class ErrorPresenter
{
    /// <summary>표시용 문자열. 상태줄·콜백처럼 대화상자를 띄우지 않는 곳도 이걸 쓴다.</summary>
    public static string Format(Exception ex) => ErrorText.Format(ex);

    /// <summary>로그만 남긴다. 어느 기능(<paramref name="feature"/>)에서 났는지 한 줄로 앞세운다.</summary>
    public static void Log(string feature, Exception ex)
        => LogManager.Logger.Error($"{feature} {Format(ex)}", ex);

    /// <summary>TaskDialog + 로그. 첫 줄은 MainInstruction, 나머지 줄은 MainContent에 둔다.</summary>
    public static void Show(string title, string feature, Exception ex)
    {
        Log(feature, ex);
        var lines = Format(ex).Split([Environment.NewLine, "\n"], StringSplitOptions.None);
        var dialog = new TaskDialog(title)
        {
            MainInstruction = lines[0],
            MainContent = string.Join(Environment.NewLine, lines.Skip(1)),
            // 코드는 본문이 길어도 항상 보이도록 바닥글에 한 번 더 둔다.
            FooterText = ex is DhbimwaterException dh ? $"오류 코드 {dh.Code}" : $"오류 코드 {ErrorCodes.Unclassified} (Revit/.NET 원문)"
        };
        dialog.Show();
    }
}
