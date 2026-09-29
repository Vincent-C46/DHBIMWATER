using System.Windows;

namespace DHBIMWATER.UI.Views.Modeling;

/// <summary>임시 도구(2026-09-18) — 캔버스를 프리셋 제출 양식 텍스트로 보여 주는 읽기 전용 창.
/// <para>ViewModel을 붙이지 않는다 — 만들어진 문자열 한 장만 받아 띄우는 창이라 바인딩할 상태가 없다.
/// 유형별 기본값이 카탈로그에 들어가면 이 창과 호출부(PipeLayoutView.xaml.cs, ExportTemplateTextCommand)를 함께 지운다.</para></summary>
public partial class PipeLayoutTemplateTextView : Window
{
    public PipeLayoutTemplateTextView(string text)
    {
        InitializeComponent();
        TextArea.Text = text;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        // 다른 프로세스가 클립보드를 잡고 있으면 SetText가 던진다. 본문은 화면에 그대로 있으므로 창을 닫지 않는다.
        try
        {
            Clipboard.SetText(TextArea.Text);
            CopyHint.Text = "클립보드에 복사했습니다.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopyHint.Text = "클립보드를 쓸 수 없습니다. 본문을 직접 선택해 복사하세요.";
        }
        CopyHint.Visibility = Visibility.Visible;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
