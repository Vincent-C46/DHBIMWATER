using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Helpers;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>실행 한 번에서 모인 접합 개소 기록 경고(docs/170 § F).
/// <para>인스턴스마다 한 줄씩 쌓으면 결과 창이 폭주하므로 <b>건수로 접어 한 줄</b>만 만든다 —
/// <c>RevitPipeMaterialBackfillRepo.Compress</c>와 같은 태도다.</para></summary>
internal sealed class JointCountDiagnostics
{
    /// <summary><c>DH_관종</c>이 비어 강관으로 간주한 부재 수.</summary>
    public int MaterialAssumedSteel { get; private set; }

    public void CountMaterialAssumedSteel() => MaterialAssumedSteel++;

    /// <summary>남길 경고 한 줄. 없으면 null이다.</summary>
    public string? Warning => MaterialAssumedSteel == 0
        ? null
        : $"부재 {MaterialAssumedSteel:N0}개는 DH_관종이 비어 있어 강관(용접)으로 간주해 접합 슬롯을 기록했습니다. " +
          "주철관 밸브실이면 DH_관종을 채운 뒤 [배관재료표 재기록]을 다시 실행하세요.";
}

/// <summary>
/// 밸브실 부재에 접합·부설 개소를 기록한다(docs/166 § 7, docs/168 § 5-3, docs/170 § D).
/// <para>신규 배치 경로(<see cref="RevitPipeSegmentCommandRepo"/>)와 재기록 경로
/// (<see cref="RevitPipeMaterialBackfillRepo"/>)가 <b>같은 값</b>을 찍어야 하므로 기록을 여기 한곳에 둔다 —
/// <see cref="PipeMaterialSpecText"/>를 공용화한 것과 같은 이유다.</para>
/// <para><b>커넥터 실측을 버린 이유(2026-09-14 변경)</b> — 처음에는 연결된 커넥터를 세고 연결 1건을
/// ElementId가 작은 쪽에 귀속시켰다(`PipeMaterialConnectorReader.OwnedJointCount`). 그 방식은 구조상
/// 접합 합이 <b>항상 부재 수보다 작게</b> 나오는데, 산출서 원장은 <b>접합 합 = 부재 수</b>다
/// (제수밸브실 D600·D300은 부설수와 플랜지접합수가 같은 값이고, 세 실의 D1650도 부재 수와 맞는다).
/// 관행이 "부재를 하나 놓고 붙이는 품"을 1개소로 세기 때문이라, 모델의 실제 연결 관계를 재는 것이
/// 오히려 산출서를 못 맞춘다. 그래서 실측을 버리고 부재당 1개소로 돌렸다.</para>
/// </summary>
internal static class PipeJointCountWriter
{
    /// <summary>밸브류처럼 다룰 부재인지 — 부재명 접합 표기·접합 구성을 붙이지 않고 밸브 부설 1개소로 센다.
    /// <para>밸브부관 A·B는 배관 부속류(OST_PipeFitting) 패밀리지만 품목명을 패밀리명 그대로 쓰기로 해서
    /// (2026-09-28 사용자 결정, docs/227 § 0-3) 패밀리명 키워드로 밸브류와 같이 묶는다. 신규 배치·재기록이 모두 이 한 곳을 본다.</para></summary>
    public static bool IsAccessory(FamilyInstance instance)
        => instance.Category?.Id.Value == (long)BuiltInCategory.OST_PipeAccessory
           || PipeValveBend.IsValveBendFamily(instance.Symbol?.Family?.Name);

    /// <summary>이 부재의 접합 방식을 정한다(docs/170 § A).
    /// <para><b>부재명은 인스턴스에서 직접 읽는다</b> — 호출부가 방금 만든 이름이 아니라 <b>이미 찍혀 있던</b>
    /// <c>DH_부재명칭</c>이어야 한다. 재기록 경로는 이름을 커넥터 실측으로 새로 만들기 때문에, 호출부의 이름을
    /// 받으면 사용자가 손으로 적어 둔 `1S1F 단관` 표기를 영영 못 본다(docs/170 § C-⚠2).</para></summary>
    public static BendConnection ResolveConnection(FamilyInstance instance, BendConnection fallback)
        => PipeJointResolution.Resolve(
            ReadJointParameter(instance),
            instance.LookupParameter(ValvePipingParameters.ItemName)?.AsString(),
            fallback);

    /// <summary><see cref="PipeJointResolution.ParameterNames"/>를 순회해 처음 값이 잡히는 것을 쓴다.
    /// 인스턴스를 먼저 보고 없으면 유형에서 찾는다 — 접합 형식은 유형 파라미터인 패밀리가 많다(docs/170 § B-2).
    /// <para>목록이 비어 있으면(조사 전) 항상 null이고, 그러면 해석이 부재명 표기·폴백으로 내려간다.</para></summary>
    private static string? ReadJointParameter(FamilyInstance instance)
    {
        foreach (var name in PipeJointResolution.ParameterNames)
        {
            var value = instance.LookupParameter(name)?.AsString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = instance.Symbol?.LookupParameter(name)?.AsString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>재기록 경로의 이탈방지압륜 플래그(docs/170 § D-3).
    /// <para>창이 없는 경로이므로 <b>이미 찍혀 있는 값</b>을 그대로 이어받는다. 이 한 줄이 없으면
    /// 압륜으로 기록해 둔 모델을 재기록할 때 KP접합으로 바뀐다.</para></summary>
    public static bool ReadRestraintGland(FamilyInstance instance)
        => Enumerable.Range(1, PipeAlignmentParameters.JointSlotDefinitionCount)
            .Any(slot => string.Equals(instance.LookupParameter(ValvePipingParameters.JointSlotKind(slot))?.AsString(),
                "이탈방지압륜접합", StringComparison.Ordinal));

    /// <summary>한 부재의 개소 수를 기록한다.
    /// <para>관부재는 부설 1 + 접합 1이고, 접합을 어느 매개변수에 넣을지는 <b>관종 + 접합 방식 + 압륜</b>이
    /// 정한다(<see cref="PipeJointMethodTable"/> → 계획관로 표에 위임).</para>
    /// <para>밸브류는 <b>전부 0</b>이다 — 접합·부설이 `제수밸브 접합 및 부설`(CGI…) 품에 이미 들어 있어
    /// 여기서 또 찍으면 이중 계상이 된다. 빈칸이 아니라 0을 남기는 것은 "세어보니 없다"와 "미태깅"을
    /// 구분하기 위해서다(docs/166 확정 3).</para></summary>
    /// <param name="composition"><see cref="ResolveComposition"/>가 정한 접합 구성(`2S1F`). 호출부가 부재명 접두를 만들 때
    /// 이미 구했으므로 여기서 다시 구하지 않는다 — 두 번 구하면 부재명과 접합구성이 어긋날 수 있다.</param>
    public static void Write(FamilyInstance instance, PipeMaterial? material, BendConnection connection,
        bool useRestraintGland, string? composition, ICollection<string> failed, JointCountDiagnostics diagnostics,
        IReadOnlyDictionary<string, bool>? flags = null)
    {
        if (IsAccessory(instance))
        {
            SetInteger(instance, ValvePipingParameters.ValveJointLayingCount, 1, failed);
            return;
        }

        var part = instance.LookupParameter(ValvePipingParameters.Part)?.AsString()
            ?? instance.LookupParameter(ValvePipingParameters.ItemName)?.AsString()
            ?? string.Empty;
        SetInteger(instance, ValvePipingParameters.LayingCount,
            IsStraightOrShort(part) ? 0 : 1, failed);

        var writer = new PipeParameterWriter();
        writer.JointSlots(instance, BuildSlots(instance, material, connection, useRestraintGland, composition, diagnostics));
        foreach (var name in writer.Missing) failed.Add(name);

        WriteComposition(instance, composition, failed);
    }

    private static IReadOnlyList<(string Kind, string Spec, int Count)> BuildSlots(FamilyInstance instance,
        PipeMaterial? material, BendConnection connection, bool useRestraintGland, string? composition,
        JointCountDiagnostics diagnostics)
    {
        var ends = PipeJointComposition.Decode(composition);
        if (ends.IsEmpty)
            ends = connection == BendConnection.Socket ? new PipeJointEnds(1, 0) : new PipeJointEnds(0, 1);

        var slots = new List<(string Kind, string Spec, int Count)>();
        if (ends.Socket > 0)
        {
            var effective = material;
            if (effective is null)
            {
                effective = PipeMaterial.Steel;
                diagnostics.CountMaterialAssumedSteel();
            }
            if (PipeAlignmentParameters.JointKindFor(effective, BendConnection.Socket, useRestraintGland) is { } kind)
                slots.Add((kind, string.Empty, ends.Socket));
        }
        if (ends.Flange > 0)
        {
            var part = PipeMaterialSpecText.StripJointMark(instance.LookupParameter(ValvePipingParameters.Part)?.AsString()
                ?? instance.LookupParameter(ValvePipingParameters.ItemName)?.AsString());
            var spec = part is PipeMaterialSpecText.TeeName or PipeMaterialSpecText.CrossName
                ? instance.LookupParameter(ValvePipingParameters.BranchSpec)?.AsString() ?? string.Empty
                : string.Empty;
            slots.Add(("플랜지접합", spec, ends.Flange));
        }
        return slots;
    }

    private static bool IsStraightOrShort(string part)
        => PipeMaterialSpecText.StripJointMark(part) is PipeMaterialSpecText.StraightName or PipeMaterialSpecText.ShortName;

    /// <summary>부재의 접합 구성(`2S1F`)을 정한다. 부재명 접두가 이 값에서 나오므로(2026-09-15) 채번·이름 기록보다 먼저 부른다.
    /// <para>우선순위: <b>이미 찍힌 값</b>(사용자가 손으로 고친 구성이 우선, <c>DH_비고</c>와 같은 태도) → 패밀리 플래그
    /// (`좌측_소켓`·`우측_플랜지`가 단부마다 달려 있어 `1S1F`까지 그대로 읽힘) → 개소 매개변수에서 유도(부재 전체를 한 형상으로).
    /// 밸브류는 null — 접합 개소를 세지 않는다.</para>
    /// <para>개수는 <b>모델에 달린 단부 수</b>다 — 한쪽에만 소켓을 둔 직관은 1, 양쪽이면 2다. 개소 매개변수 넷은 부재당 1로
    /// 고정이라 두 값은 일부러 다르다. 넷은 지금까지의 산출을 그대로 두기 위해 남겨 둔 것이고, 구성이 부재의 실제 모습이다.</para></summary>
    /// <param name="endCount">부재의 단부 수(<see cref="PipeMaterialSpecText.EndCountFor"/>). 접두를 떼기 전의 기본명으로 구한다.</param>
    /// <param name="flags">호출부가 <see cref="ReadFlags"/>로 미리 읽은 플래그. 없으면 여기서 읽는다(docs/206 § F — 부재당 2회 열거를 1회로).</param>
    public static string? ResolveComposition(FamilyInstance instance, PipeMaterial? material, BendConnection connection,
        bool useRestraintGland, int endCount, IReadOnlyDictionary<string, bool>? flags = null)
    {
        if (IsAccessory(instance)) return null;
        var existing = instance.LookupParameter(ValvePipingParameters.JointComposition)?.AsString();
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        return PipeJointFlagReader.Compose(flags ?? ReadFlags(instance), instance.Symbol?.Family?.Name, endCount)
               ?? (connection == BendConnection.Flanged
                   ? PipeJointComposition.Encode(0, endCount)
                   : PipeJointComposition.Encode(endCount, 0));
    }

    /// <summary>접합 구성(<c>DH_접합구성</c>, `2S1F` 표기)을 채운다. 곡관 형식(<c>DH_곡관형식</c>) 기록은 docs/214에서 폐지했다.
    /// <para><b>이미 값이 있으면 건드리지 않는다.</b> 사용자가 손으로 고친 구성을 재기록이 지우면 안 된다
    /// (<c>DH_비고</c>와 같은 태도). 손으로 적는 포맷이 부재명 접두와 같은 `1S1F`라 어렵지 않다(2026-09-15).</para></summary>
    private static void WriteComposition(FamilyInstance instance, string? composition, ICollection<string> failed)
    {
        if (composition is not null) SetTextIfEmpty(instance, ValvePipingParameters.JointComposition, composition, failed);
    }

    /// <summary>인스턴스·유형의 Yes/No 매개변수 전체. 인스턴스 값이 유형 값을 덮는다 —
    /// 가시성 플래그는 유형에 기본값을 두고 인스턴스에서 뒤집는 패밀리가 있다.</summary>
    public static IReadOnlyDictionary<string, bool> ReadFlags(FamilyInstance instance)
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        Collect(instance.Symbol?.Parameters);
        Collect(instance.Parameters);
        return flags;

        void Collect(ParameterSet? parameters)
        {
            if (parameters is null) return;
            foreach (Parameter parameter in parameters)
            {
                if (parameter.StorageType != StorageType.Integer) continue;
                if (parameter.Definition?.GetDataType() != SpecTypeId.Boolean.YesNo) continue;
                var name = parameter.Definition.Name;
                if (!string.IsNullOrWhiteSpace(name)) flags[name] = parameter.AsInteger() != 0;
            }
        }
    }

    /// <summary>이미 값이 있으면 그대로 둔다 — 사용자가 손으로 고친 구성이 우선이다.</summary>
    private static void SetTextIfEmpty(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null) { failed.Add(parameterName); return; }
        if (parameter.IsReadOnly || !string.IsNullOrWhiteSpace(parameter.AsString())) return;
        if (!parameter.Set(value)) failed.Add(parameterName);
    }

    private static void SetInteger(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value))
            failed.Add(parameterName);
    }
}
