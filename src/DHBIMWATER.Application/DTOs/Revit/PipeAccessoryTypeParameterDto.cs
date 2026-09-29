namespace DHBIMWATER.Application.DTOs.Revit
{
    /// <summary>배관부속 유형(FamilySymbol)의 길이 계열 유형 매개변수 한 건. 값은 mm로 환산한 읽기 전용 확인용이다(docs/233 § B-2).
    /// 게이트밸브처럼 공칭지름이 유형 매개변수(`Nominal pipe size` 등)로 고정된 패밀리에서
    /// "고른 유형의 관경이 실제 관경과 맞는지"를 대조하는 데 쓴다 — 유형 매개변수에 값을 쓰지는 않는다.</summary>
    public sealed record PipeAccessoryTypeParameterDto(string Name, double ValueMm);
}
