using System.IO;

namespace DHBIMWATER.Infrastructure.Logging
{
    public static class LogManager
    {
        private static ILogger _logger;

        /// <summary>로그 파일 경로. 안내 텍스트에서 "상세는 로그를 보라"고 가리킬 때 쓴다(docs/241).</summary>
        public static string LogFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DHBIMWATER",
            "Logs",
            "DHBIMWATER.log");

        public static ILogger Logger
        {
            get
            {
                if(_logger == null)
                {
                    _logger = new FileLogger(LogFilePath);
                }

                return _logger;
            }

        }
    }
}
