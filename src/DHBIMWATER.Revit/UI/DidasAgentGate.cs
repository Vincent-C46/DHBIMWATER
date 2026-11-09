using DidasUsage;

namespace DHBIMWATER.Revit.UI
{
    /// <summary>
    /// Didas 에이전트 사용자 확인 스위치. 리본 버튼 활성화·외부 도구 명령 실행 전 확인 한곳 모음.
    /// </summary>
    internal static class DidasAgentGate
    {
        /// <summary>
        /// true = 에이전트 사용자 확인 실패 시 버튼 끔·명령 막음(사내 배포).
        /// false = 확인 안 함, 에이전트 없어도 실행(외부 배포).
        /// 사용 로그 전송(DidasUsageService)은 이 값과 무관 — 실패해도 실행 안 막음.
        /// </summary>
        // const 대신 static readonly — const false면 아래 확인 코드에 CS0162(도달 불가) 경고
        internal static readonly bool RequireDidasAgent = false;

        /// <summary>실행 허용 여부. 스위치 꺼져 있으면 에이전트 확인 안 하고 바로 true.</summary>
        internal static bool IsUserAllowed()
        {
            if (!RequireDidasAgent)
                return true;

            return DidasUsageTracker.TryGetUserInfo(out _, out _);
        }
    }
}
