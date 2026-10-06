using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Converters;
using DHBIMWATER.Application.Interfaces.Piping;
using FittingWeightInput = DHBIMWATER.Application.DTOs.Piping.FittingWeightInput;
using FittingWeightPart = DHBIMWATER.Application.DTOs.Piping.FittingWeightPart;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>밸브실 배관 부재에 `DH_무게`·`DH_단위중량`을 기록한다(docs/260 § B·§ E).
/// 신규 배치(<see cref="RevitPipeSegmentCommandRepo"/>)와 재기록(<see cref="RevitPipeMaterialBackfillRepo"/>)이 같은 규칙을 쓴다.
/// <para>값을 못 구하면 <b>쓰지 않는다</b> — 기존 값을 0으로 지우지도, 지어내지도 않는다.</para>
/// <para>질량·단위 길이당 질량 형식으로 기록한다(docs/264). 형식이 다르면 실패로 모은다.</para></summary>
internal static class PipeWeightParameterWriter
{
    /// <summary>밸브·이음관 패밀리에서 무게를 읽을 매개변수 이름(우선순위 순). 사용자 결정 2026-10-02.</summary>
    private static readonly string[] FamilyWeightParameterNames = { "무게", "중량" };

    /// <summary>카탈로그 부재는 수량 규칙으로, 밸브류는 패밀리 매개변수에서 무게를 구해 적는다.</summary>
    public static void Write(FamilyInstance instance, FittingWeightInput input, IPipeCatalog catalog,
        IPipeQuantityRules rules, ICollection<string> failed)
    {
        if (input.Part == FittingWeightPart.Other)
        {
            if (ReadFamilyWeightKg(instance) is { } familyKg) SetMass(instance, ValvePipingParameters.Weight, familyKg, failed);
            return;
        }

        var weight = rules.ResolveFittingWeight(catalog, input);
        if (weight.WeightKg is { } kg) SetMass(instance, ValvePipingParameters.Weight, kg, failed);
        if (weight.UnitWeightKgPerM is { } unit) SetMassPerLength(instance, ValvePipingParameters.UnitWeight, unit, failed);
    }

    /// <summary>인스턴스 → 유형 순으로 `무게`, 없으면 `중량`. 숫자(질량·실수·정수)나 숫자 문자열만 받고 0 이하는 무시한다.
    /// 질량 매개변수는 kg로 환산한다.</summary>
    public static double? ReadFamilyWeightKg(FamilyInstance instance)
    {
        foreach (var name in FamilyWeightParameterNames)
        {
            if (Read(instance.LookupParameter(name)) is { } instanceKg) return instanceKg;
            if (Read(instance.Symbol?.LookupParameter(name)) is { } typeKg) return typeKg;
        }
        return null;

        static double? Read(Parameter? parameter)
        {
            if (parameter is null || !parameter.HasValue) return null;
            double? value = parameter.StorageType switch
            {
                StorageType.Double => parameter.Definition.GetDataType() == SpecTypeId.Mass
                    ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Kilograms)
                    : parameter.AsDouble(),
                StorageType.Integer => parameter.AsInteger(),
                StorageType.String => double.TryParse(parameter.AsString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                _ => null
            };
            return value is > 0d ? value : null;
        }
    }

    private static void SetMass(FamilyInstance instance, string parameterName, double kilograms, ICollection<string> failed)
        => SetDouble(instance, parameterName, SpecTypeId.Mass, RevitUnitConverter.KgToInternal(kilograms), failed);

    /// <summary>kg/m → 내부 kg/ft. 변환 없이 넣으면 약 3.28배 틀어진다(docs/264 § 0-2).</summary>
    private static void SetMassPerLength(FamilyInstance instance, string parameterName, double kilogramsPerMeter, ICollection<string> failed)
        => SetDouble(instance, parameterName, SpecTypeId.MassPerUnitLength, RevitUnitConverter.KgPerMToInternal(kilogramsPerMeter), failed);

    private static void SetDouble(FamilyInstance instance, string parameterName, ForgeTypeId expectedDataType, double internalValue, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double
            || !parameter.Definition.GetDataType().Equals(expectedDataType) || !parameter.Set(internalValue))
            failed.Add(parameterName);
    }
}
