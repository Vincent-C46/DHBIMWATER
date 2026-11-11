using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>타이밸브실 최소 폭용 패밀리 실측(docs/323). 바이패스 배치와 같은 프로브로 T·밸브부관 A/B·게이트·버터플라이 밸브를 잰다.
/// <para>프로브 인스턴스를 놓고 지우므로 트랜잭션 안에서 불러야 한다 — 열고 되돌리는 것은 UseCase 몫(CLAUDE.md).</para></summary>
public interface ITieValveRoomProbeRepo
{
    /// <summary>패밀리는 PipeLayout 패밀리 설정(문서 저장본)을 쓴다. 바이패스 밸브 = 이름에 "게이트", 본관 밸브 = "버터플라이" 든 배관 밸브류 패밀리.
    /// 설정·패밀리·핸드북 규격이 없으면 사유와 함께 예외.</summary>
    TieValveRoomProbe Measure(double mainDiameterMm, PipeMaterial material, SteelFittingClass steelClass);
}
