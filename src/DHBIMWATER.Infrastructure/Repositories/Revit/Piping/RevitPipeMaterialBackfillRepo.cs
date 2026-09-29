using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 재료표 값이 비어 있는 기존 배관 부재에 명칭·규격·번호를 다시 기록한다(docs/136).
/// <para>docs/133 이전에 배치된 배관 이음쇠는 공유 매개변수 바인딩이 없어 값이 통째로 비어 있고,
/// 바인딩을 나중에 보강해도 소급되지 않는다. 배치 당시의 UI 입력(관경·정척·격점번호)도 남아 있지 않으므로
/// 명칭·규격은 <b>커넥터 실측</b>으로 복원한다.</para>
/// <para>트랜잭션은 열지 않는다. UseCase가 연다(CLAUDE.md).</para>
/// </summary>
internal sealed class RevitPipeMaterialBackfillRepo : IPipeMaterialBackfillRepo
{
    /// <summary>결과 창에 남길 경고 줄 수 상한.</summary>
    private const int MaxWarningLines = 5;

    /// <summary>한 부재의 복원된 재료표 값.
    /// 뒤의 넷은 계획관로와 같은 형태로 적는 정보 매개변수(docs/178 § C-3)의 원천이다 — 신규 배치의 <c>MaterialPart</c>와 같은 뜻.</summary>
    private sealed record Restored(string JunctionId, FamilyInstance Instance, string Name, string Spec,
        double DiameterMm, double? BranchDiameterMm = null, double? LengthMm = null, double? DeflectionDeg = null);

    private readonly Func<Document?> _document;
    private readonly Func<UIDocument?> _uiDocument;

    public RevitPipeMaterialBackfillRepo(Func<Document?> document, Func<UIDocument?> uiDocument)
    {
        _document = document;
        _uiDocument = uiDocument;
    }

    public PipeMaterialBackfillScan ScanCandidates()
    {
        var document = Document();
        var (candidates, fromSelection) = Candidates(document);
        return new PipeMaterialBackfillScan(candidates.Count, fromSelection);
    }

    public PipeMaterialBackfillResult Backfill()
    {
        var document = Document();
        // UseCase가 방금 넣은 바인딩이 '기존' 요소에 반영된 뒤에 LookupParameter를 불러야 한다.
        // 이 한 줄이 빠지면 오래된 문서에서 전부 실패한다(docs/136 § 3-6.2).
        document.Regenerate();

        var (candidates, _) = Candidates(document);
        var warnings = new List<string>();
        var restored = new List<Restored>();
        var skipped = 0;

        foreach (var instance in candidates)
        {
            var part = Describe(instance, warnings);
            if (part is null) { skipped++; continue; }
            restored.Add(part);
        }

        // 접합 방식을 채번보다 먼저 정한다 — 부재명 표기·채번·비고·접합수가 모두 이 결과를 본다(docs/170 § C-⚠2).
        // 폴백이 플랜지인 이유: 재기록은 창이 없고, 원장에서 밸브실은 실당 용접 1개소를 빼면 전량 플랜지다(docs/168 § 5-3).
        // 그래서 이 기본값이 docs/170 이전 결과와 같은 값을 만든다(회귀 없음).
        // 접합 구성(`2S1F`)도 채번보다 먼저 정한다 — 부재명 접두가 이 값에서 나온다(2026-09-15, 부재명·접합구성 둘 다 유지).
        // 관종은 이미 찍혀 있는 DH_관종에서만 읽는다(사용자 입력이 없는 경로, docs/141 § 2 판단 5). 구성 폴백과 비고가 같이 쓴다.
        var connections = new Dictionary<ElementId, BendConnection>();
        var compositions = new Dictionary<ElementId, string?>();
        var flagsById = new Dictionary<ElementId, IReadOnlyDictionary<string, bool>>();
        var materials = new Dictionary<ElementId, PipeMaterial?>();
        var marked = new List<Restored>(restored.Count);
        foreach (var part in restored)
        {
            materials[part.Instance.Id] = PipeAlignmentParameters.MaterialFromModelValue(
                part.Instance.LookupParameter(ValvePipingParameters.Material)?.AsString());
            // 밸브류는 명칭이 곧 패밀리명이고 접합 개소도 세지 않는다. 표기를 붙이지 않는다.
            if (PipeJointCountWriter.IsAccessory(part.Instance)) { marked.Add(part); continue; }
            var connection = PipeJointCountWriter.ResolveConnection(part.Instance, BendConnection.Flanged);
            var endCount = PipeMaterialSpecText.EndCountFor(part.Name);
            var flags = PipeJointCountWriter.ReadFlags(part.Instance);
            flagsById[part.Instance.Id] = flags;
            var composition = PipeJointCountWriter.ResolveComposition(part.Instance, materials[part.Instance.Id], connection,
                PipeJointCountWriter.ReadRestraintGland(part.Instance), endCount, flags);
            connections[part.Instance.Id] = connection;
            compositions[part.Instance.Id] = composition;
            marked.Add(part with { Name = PipeMaterialSpecText.WithJointMark(part.Name, composition, connection, endCount) });
        }
        restored = marked;

        var numbers = AssignNumbers(document, restored);

        var failed = new List<string>();
        var diagnostics = new JointCountDiagnostics();
        foreach (var part in restored)
        {
            SetOptionalText(part.Instance, ValvePipingParameters.Addin, ValvePipingParameters.AddinValue, failed);
            SetOptionalText(part.Instance, ValvePipingParameters.ItemName, part.Name, failed);
            SetOptionalText(part.Instance, ValvePipingParameters.ItemSpec, part.Spec, failed);
            SetOptionalInteger(part.Instance, ValvePipingParameters.ItemNumber, numbers[part.Instance.Id], failed);
            // 계획관로와 같은 형태의 네 칸(docs/178 § C-3). 신규 배치(RevitPipeSegmentCommandRepo.RecordMaterialParameters)와 같은 값이어야 한다.
            SetOptionalText(part.Instance, ValvePipingParameters.Part, PipeMaterialSpecText.StripJointMark(part.Name), failed);
            SetOptionalText(part.Instance, ValvePipingParameters.NominalDiameter, part.BranchDiameterMm is { } branchMm
                ? PipeAlignmentParameters.DiameterText(part.DiameterMm, branchMm)
                : PipeAlignmentParameters.DiameterText(part.DiameterMm), failed);
            // 수량산출 규격2(DH_분기규격). 신규 배치(RevitPipeSegmentCommandRepo.RecordMaterialParameters)와 같은 값·같은 규칙이다 —
            // 사용자가 손으로 고친 값을 재기록이 덮으면 안 되므로 비어 있을 때만 적는다.
            if (part.BranchDiameterMm is { } branchSpecMm)
                SetOptionalTextIfEmpty(part.Instance, ValvePipingParameters.BranchSpec,
                    PipeAlignmentParameters.BranchSpecText(branchSpecMm), failed);
            // 현장 절단 개소. 신규 배치와 같은 규칙 — 관·이형관만, 기본 1, 이미 값이 있으면 그대로 둔다.
            if (PipeMaterialSpecText.IsCutTarget(part.Name))
                SetOptionalIntegerIfEmpty(part.Instance, ValvePipingParameters.CutCount,
                    PipeAlignmentParameters.DefaultCutCount, failed);
            if (part.LengthMm is { } lengthMm) SetOptionalDouble(part.Instance, ValvePipingParameters.Length, UnitUtils.ConvertToInternalUnits(lengthMm, UnitTypeId.Millimeters), failed);
            if (part.DeflectionDeg is { } deflectionDeg) SetOptionalDouble(part.Instance, ValvePipingParameters.BendAngle, UnitUtils.ConvertToInternalUnits(deflectionDeg, UnitTypeId.Degrees), failed);
            // DH_격점번호는 배치 당시 입력이라 복원할 수 없다. 비운 채로 두면 '배관재료표_미지정'으로 모인다.
            // DH_관종·DH_야간도 이 경로에서는 알 수 없다 — 사용자 입력이 없는 경로이므로 값을 지어내지 않는다(docs/141 § 2 판단 5).
            // DH_비고만, 이미 찍혀 있는 DH_관종에서 유도할 수 있으면 채운다. 못 구하면 조용히 건너뛴다
            // (관종 미기록은 docs/139 이전에 배치된 부재의 정상 상태다).
            var material = materials.GetValueOrDefault(part.Instance.Id);
            var remark = PipeMaterialSpecText.Remark(part.Name, material);
            if (!string.IsNullOrEmpty(remark))
                SetOptionalTextIfEmpty(part.Instance, ValvePipingParameters.Remark, remark, failed);
            // 접합·부설 개소(docs/166 § 7, docs/170 § D). 명칭·규격을 복원한 부재만 대상이다 —
            // 종류를 판정하지 못해 건너뛴 부재는 접합방법도 배분할 수 없다.
            // 압륜 플래그는 이미 찍혀 있는 값을 이어받는다. Write가 그 값을 덮어쓰므로 인자에서 먼저 읽는다(docs/170 § D-3).
            PipeJointCountWriter.Write(part.Instance, material,
                connections.TryGetValue(part.Instance.Id, out var connection) ? connection : BendConnection.Flanged,
                PipeJointCountWriter.ReadRestraintGland(part.Instance), compositions.GetValueOrDefault(part.Instance.Id), failed, diagnostics,
                flagsById.GetValueOrDefault(part.Instance.Id));
        }

        if (diagnostics.Warning is { } jointWarning) warnings.Add(jointWarning);

        var distinct = failed.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 0)
            warnings.Add($"매개변수 {string.Join(", ", distinct)}에 값을 쓰지 못했습니다. " +
                "배관 밸브류·배관 이음쇠 카테고리에 공유 매개변수가 바인딩됐는지 확인하세요.");

        return new PipeMaterialBackfillResult(restored.Count, skipped, Compress(warnings));
    }

    /// <summary>건너뛴 부재가 많으면 결과 창이 폭주한다. 같은 사유는 한 줄로 접고 나머지는 건수만 남긴다.</summary>
    private static IReadOnlyList<string> Compress(IEnumerable<string> warnings)
    {
        var distinct = warnings.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count <= MaxWarningLines) return distinct;
        var head = distinct.Take(MaxWarningLines).ToList();
        head.Add($"그 외 {distinct.Count - MaxWarningLines:N0}건.");
        return head;
    }

    /// <summary>재기록 대상. 선택 요소 안에 후보가 있으면 그것만 본다 —
    /// DHBIMWATER가 만들지 않은 부속까지 건드리는 것을 사용자가 막을 수 있는 유일한 수단이다(docs/136 § 2).</summary>
    private (IReadOnlyList<FamilyInstance> Candidates, bool FromSelection) Candidates(Document document)
    {
        // Revit이 돌려주는 ICollection<ElementId>는 IReadOnlyCollection으로 바로 안 넘어간다. 한 번 굳혀서 쓴다.
        var selection = _uiDocument()?.Selection.GetElementIds().ToList() ?? [];
        if (selection.Count > 0)
        {
            var selected = Ordered(PipeMaterialPartCollector.UntaggedInstances(document, selection));
            if (selected.Count > 0) return (selected, true);
        }
        return (Ordered(PipeMaterialPartCollector.UntaggedInstances(document)), false);
    }

    /// <summary>ElementId 오름차순 — 배치 순서에 가장 가까운 대용이다. 번호가 도면 읽는 순서에 가깝게 나온다.</summary>
    private static List<FamilyInstance> Ordered(IEnumerable<FamilyInstance> instances)
        => instances.OrderBy(x => x.Id.Value).ToList();

    /// <summary>커넥터를 실측해 명칭·규격을 복원한다. 판정할 수 없으면 null(건너뛴다).</summary>
    private static Restored? Describe(FamilyInstance instance, ICollection<string> warnings)
    {
        var junctionId = instance.LookupParameter(ValvePipingParameters.JunctionId)?.AsString() ?? string.Empty;
        var connectors = PipeMaterialConnectorReader.EndConnectors(instance);
        // 밸브부관(배관 부속류)도 밸브류와 같이 명칭 = 패밀리명이다. 커넥터 수로 가르면 B는 곡관, A는 십자로 잘못 기록된다.
        // 판정은 접합 표기와 같은 PipeJointCountWriter.IsAccessory 한 곳을 본다.
        var isAccessory = PipeJointCountWriter.IsAccessory(instance);

        if (connectors.Count == 0)
        {
            warnings.Add(Skip(instance, "배관 커넥터가 없습니다"));
            return null;
        }

        // 인라인 밸브·1포트 부속은 명칭이 곧 패밀리명이다. 커넥터 수로 종류를 나눌 필요가 없다.
        if (isAccessory)
        {
            // 밸브부관 A(4포트)는 주관 x 분기 규격이다 — 신규 배치(RevitPipeSegmentCommandRepo)와 같은 `D1650xD300`(docs/227 § E).
            // 수직분기 커넥터도 분기 구경이므로 최대 = 주관, 최소 = 분기로 읽어도 같다.
            // 밸브부관 B는 자기 커넥터가 모두 바이패스 관경이라 본관 구경을 알 수 없다 — `D300` 표기를 유지한다(한계, docs/227 § E).
            if (connectors.Count == PipeValveBend.ValveTeeConnectorCount && PipeValveBend.IsValveBendFamily(instance.Symbol.Family.Name))
            {
                var diameters = connectors.Select(PipeMaterialConnectorReader.DiameterMm).ToList();
                if (diameters.Any(x => x is null))
                {
                    warnings.Add(Skip(instance, "커넥터가 원형이 아니라 규격을 만들 수 없습니다"));
                    return null;
                }
                var mainMm = diameters.Max()!.Value;
                var branchMm = diameters.Min()!.Value;
                return new Restored(junctionId, instance, FittingName(instance),
                    PipeMaterialSpecText.ValveBranch(mainMm, branchMm), mainMm, BranchDiameterMm: branchMm);
            }
            var accessoryDiameter = PipeMaterialConnectorReader.DiameterMm(connectors[0]);
            if (accessoryDiameter is null)
            {
                warnings.Add(Skip(instance, "커넥터가 원형이 아니라 규격을 만들 수 없습니다"));
                return null;
            }
            return new Restored(junctionId, instance, FittingName(instance),
                ValvePipingParameters.DiameterText(accessoryDiameter.Value), accessoryDiameter.Value);
        }

        switch (connectors.Count)
        {
            case 2:
            {
                var diameter = PipeMaterialConnectorReader.DiameterMm(connectors[0]);
                if (diameter is null)
                {
                    warnings.Add(Skip(instance, "커넥터가 원형이 아니라 규격을 만들 수 없습니다"));
                    return null;
                }
                var deflection = PipeMaterialSpecText.DeflectionDeg(
                    PipeMaterialConnectorReader.OutwardDot(connectors[0], connectors[1]));
                if (deflection > PipeMaterialSpecText.StraightDeflectionToleranceDeg)
                    return new Restored(junctionId, instance, PipeMaterialSpecText.BendName,
                        PipeMaterialSpecText.Bend(diameter.Value, deflection), diameter.Value, DeflectionDeg: deflection);

                var lengthMm = PipeMaterialConnectorReader.SpanMm(connectors[0], connectors[1]);
                return new Restored(junctionId, instance, PipeMaterialSpecText.SegmentName(lengthMm),
                    PipeMaterialSpecText.Segment(diameter.Value, lengthMm), diameter.Value, LengthMm: lengthMm);
            }
            case 3:
            {
                var branch = BranchConnector(connectors);
                var main = connectors.First(x => !ReferenceEquals(x, branch));
                var mainDiameter = PipeMaterialConnectorReader.DiameterMm(main);
                var branchDiameter = PipeMaterialConnectorReader.DiameterMm(branch);
                if (mainDiameter is null || branchDiameter is null)
                {
                    warnings.Add(Skip(instance, "커넥터가 원형이 아니라 규격을 만들 수 없습니다"));
                    return null;
                }
                // 3구는 분기각으로 T형·Y형을 가른다. 역가져오기(Classify)와 같은 규칙이고,
                // 신규 배치 경로와 글자 하나까지 같은 규격 문자열을 만들어야 한다(docs/187 § 5-2).
                var others = connectors.Where(x => !ReferenceEquals(x, branch)).ToList();
                var branchAngleDeg = others.Count == 2
                    ? PipeWyeResolver.BranchAngleDegFromDots(
                        PipeMaterialConnectorReader.OutwardDot(branch, others[0]),
                        PipeMaterialConnectorReader.OutwardDot(branch, others[1]))
                    : (double?)null;
                return branchAngleDeg is not null && PipeWyeResolver.IsWyeBranchAngle(branchAngleDeg.Value)
                    ? new Restored(junctionId, instance, PipeMaterialSpecText.WyeName,
                        PipeMaterialSpecText.Wye(mainDiameter.Value, branchDiameter.Value, branchAngleDeg.Value),
                        mainDiameter.Value, BranchDiameterMm: branchDiameter.Value)
                    : new Restored(junctionId, instance, PipeMaterialSpecText.TeeName,
                        PipeMaterialSpecText.Tee(mainDiameter.Value, branchDiameter.Value), mainDiameter.Value, BranchDiameterMm: branchDiameter.Value);
            }
            case 4:
            {
                // 십자(docs/175 § D-3). 커넥터 0과 그 반대(편각 0°)가 주관, 나머지가 분기다. 역가져오기(Classify)와 같은 규칙.
                var main = connectors[0];
                var opposite = connectors.Skip(1).OrderBy(x => PipeMaterialSpecText.DeflectionDeg(
                    PipeMaterialConnectorReader.OutwardDot(main, x))).First();
                var branch = connectors.First(x => !ReferenceEquals(x, main) && !ReferenceEquals(x, opposite));
                var mainDiameter = PipeMaterialConnectorReader.DiameterMm(main);
                var branchDiameter = PipeMaterialConnectorReader.DiameterMm(branch);
                if (mainDiameter is null || branchDiameter is null)
                {
                    warnings.Add(Skip(instance, "커넥터가 원형이 아니라 규격을 만들 수 없습니다"));
                    return null;
                }
                return new Restored(junctionId, instance, PipeMaterialSpecText.CrossName,
                    PipeMaterialSpecText.Cross(mainDiameter.Value, branchDiameter.Value), mainDiameter.Value, BranchDiameterMm: branchDiameter.Value);
            }
            default:
                warnings.Add(Skip(instance, $"배관 커넥터가 {connectors.Count}개라 부재 종류를 판정할 수 없습니다"));
                return null;
        }
    }

    /// <summary>T형의 분기 커넥터. 주관 2개는 서로 반대 방향(편각 0°)이므로, 그 짝이 아닌 하나가 분기다.</summary>
    private static Connector BranchConnector(IReadOnlyList<Connector> connectors)
    {
        var bestDeflection = double.MaxValue;
        var branch = connectors[0];
        for (var i = 0; i < connectors.Count; i++)
        for (var j = i + 1; j < connectors.Count; j++)
        {
            var deflection = PipeMaterialSpecText.DeflectionDeg(
                PipeMaterialConnectorReader.OutwardDot(connectors[i], connectors[j]));
            if (deflection >= bestDeflection) continue;
            bestDeflection = deflection;
            branch = connectors.First(x => !ReferenceEquals(x, connectors[i]) && !ReferenceEquals(x, connectors[j]));
        }
        return branch;
    }

    /// <summary>부재번호를 격점별로 매긴다. 같은 `명칭+규격`은 같은 번호를 받아야
    /// 항목화를 끈 일람표에서 행 1개에 번호 1개가 대응한다(docs/131 § B-4).
    /// <para>그 격점에 이미 태그된 부재가 있으면 그 번호 체계를 이어받는다 — 기존 번호는 바꾸지 않는다.
    /// 규칙은 <see cref="PipeMaterialNumbering.Assign"/>에 있다(docs/180) — 신규 배치 경로와 같은 함수를 쓴다.</para></summary>
    private static Dictionary<ElementId, int> AssignNumbers(Document document, IReadOnlyList<Restored> restored)
    {
        var assigned = new Dictionary<ElementId, int>();
        if (restored.Count == 0) return assigned;

        // 재기록 대상은 DH_Addin이 비어 있는 인스턴스라 tagged에 섞이지 않지만(UntaggedInstances),
        // 신규 배치 경로와 같은 모양을 유지하려고 제외 목록을 넘긴다(docs/180 § D).
        var tagged = PipeMaterialPartCollector.Read(document, restored.Select(x => x.Instance.Id).ToHashSet());

        foreach (var group in restored.GroupBy(x => x.JunctionId, StringComparer.Ordinal))
        {
            var existing = tagged.Where(x => PipeMaterialNumbering.SameJunction(x.JunctionId, group.Key)).ToList();
            var numbers = PipeMaterialNumbering.Assign(
                existing, group.Select(x => new PipeMaterialNumbering.PartKey(x.Name, x.Spec)));
            foreach (var part in group)
                assigned[part.Instance.Id] = numbers[new PipeMaterialNumbering.PartKey(part.Name, part.Spec)];
        }
        return assigned;
    }

    /// <summary>배관부속 명칭. 사용자가 `DH_제수밸브`처럼 접두어를 붙여 쓰므로 그것만 뗀다
    /// (<see cref="RevitPipeSegmentCommandRepo"/>의 신규 배치 경로와 같은 규칙).</summary>
    private static string FittingName(FamilyInstance instance)
    {
        var name = instance.Symbol.Family.Name ?? string.Empty;
        return name.StartsWith("DH_", StringComparison.Ordinal) ? name["DH_".Length..] : name;
    }

    private static string Skip(FamilyInstance instance, string reason)
        => $"'{instance.Symbol.Family.Name} : {instance.Symbol.Name}'을(를) 건너뛰었습니다 — {reason}.";

    private static void SetOptionalText(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String || !parameter.Set(value))
            failed.Add(parameterName);
    }

    /// <summary>이미 값이 있으면 덮어쓰지 않는다. 재기록은 <b>빈 칸을 채우는</b> 작업이므로
    /// 사용자가 손으로 적은 비고를 지우면 안 된다(docs/141 § 1-5). 건너뛴 경우는 실패가 아니다.</summary>
    private static void SetOptionalTextIfEmpty(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            failed.Add(parameterName);
            return;
        }
        if (!string.IsNullOrWhiteSpace(parameter.AsString())) return;
        if (!parameter.Set(value)) failed.Add(parameterName);
    }

    /// <inheritdoc cref="SetOptionalTextIfEmpty"/>
    private static void SetOptionalIntegerIfEmpty(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
        {
            failed.Add(parameterName);
            return;
        }
        // Integer는 0도 뜻이 있는 값이라(자르지 않는다) 미태깅만 채운다.
        if (parameter.HasValue) return;
        if (!parameter.Set(value)) failed.Add(parameterName);
    }

    private static void SetOptionalInteger(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value))
            failed.Add(parameterName);
    }

    /// <summary>길이·각도(Double 저장) 정보 매개변수. 값은 호출부가 Revit 내부 단위로 바꿔 넘긴다(docs/178 § C-3).</summary>
    private static void SetOptionalDouble(FamilyInstance instance, string parameterName, double internalValue, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double || !parameter.Set(internalValue))
            failed.Add(parameterName);
    }

    private Document Document()
        => _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
}
