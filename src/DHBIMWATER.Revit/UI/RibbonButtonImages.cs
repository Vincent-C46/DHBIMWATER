using DHBIMWATER.Infrastructure.Logging;
using System.Windows.Media.Imaging;

namespace DHBIMWATER.Revit.UI
{
    internal static class RibbonButtonImages
    {
        /// <summary>
        /// 리본 아이콘 = 이 어셈블리 내장 리소스(Resources/RibbonIcons, 이름 "RibbonIcons.파일명").
        /// UI 어셈블리 pack URI 안 씀 → Revit 시작 때 UI 어셈블리(약 14MB) 풀지 않음.
        /// 새 아이콘은 Resources/RibbonIcons 에 PNG 넣으면 자동 포함.
        /// </summary>
        internal static BitmapImage? GetIcon(string imageName)
        {
            try
            {
                using var stream = typeof(RibbonButtonImages).Assembly.GetManifestResourceStream($"RibbonIcons.{imageName}");
                if (stream == null)
                {
                    LogManager.Logger.Error($"아이콘 로드 실패: {imageName}. 내장 리소스 없음(Resources/RibbonIcons 확인).");
                    return null;
                }

                BitmapImage btnImage = new BitmapImage();
                btnImage.BeginInit();
                btnImage.StreamSource = stream;
                btnImage.DecodePixelWidth = 32;  // Revit 권장 크기로 리사이징
                btnImage.CacheOption = BitmapCacheOption.OnLoad; // 즉시 로드. 스트림 닫아도 됨
                btnImage.EndInit();
                btnImage.Freeze(); // 중요: 다른 스레드에서 사용 가능하도록
                return btnImage;
            }
            catch (Exception ex)
            {
                LogManager.Logger.Error($"아이콘 로드 실패: {imageName}. 예외: {ex.Message}");
            }

            return null;
        }
    }
}
