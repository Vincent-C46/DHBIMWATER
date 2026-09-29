namespace DHBIMWATER.Core.Piping;

public enum NodeKind
{
    EndPoint,
    Inline,
    Elbow,
    Tee,
    Cross,
    /// <summary>Y형(경사 분기). 차수 3인데 분기가 직교가 아니라 30~60°로 붙은 절점이다(docs/187 § 1).
    /// <para>맨 뒤에 둔다 — 이 값은 <see cref="PipeNodeDefinition"/>으로 배치 경계까지 그대로 넘어가고,
    /// 중간에 끼우면 기존 값의 순서가 밀린다.</para></summary>
    Wye
}
