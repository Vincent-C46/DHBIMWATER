using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Helpers;

/// <summary>
/// 단부 가시성 Yes/No를 인스턴스에 씀(docs/315). 없음·읽기전용·정수 아님 → 조용히 건너뜀.
/// 유형 매개변수는 인스턴스 조회에 안 잡혀 자연히 건너뜀 — 유형 전체가 바뀌므로 Symbol에 쓰지 않음.
/// </summary>
internal static class PipeEndFlagWriter
{
    public static void Write(FamilyInstance instance, (bool? LeftSocket, bool? RightSocket, bool LeftFlange, bool RightFlange) flags)
    {
        Set(instance, PipeEndFlagParameters.LeftSocket, flags.LeftSocket);
        Set(instance, PipeEndFlagParameters.RightSocket, flags.RightSocket);
        Set(instance, PipeEndFlagParameters.LeftFlange, flags.LeftFlange);
        Set(instance, PipeEndFlagParameters.RightFlange, flags.RightFlange);
    }

    private static void Set(FamilyInstance instance, string name, bool? value)
    {
        if (value is not { } on) return;
        if (instance.LookupParameter(name) is { IsReadOnly: false, StorageType: StorageType.Integer } parameter)
            parameter.Set(on ? 1 : 0);
    }
}
