using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>
/// 토출조 제원을 Revit 구조 모델로 만든다. Transaction 은 이 UseCase 에서만 연다.
///
/// <para><b>이번 범위</b> — 레벨 · 바닥슬래브 · 외벽 · 상부슬래브 · 상부 개구부.</para>
///
/// <para><b>기둥·거더는 만들지 않는다</b> — 원본 엑셀이 단면(TCB1·TCH1·TGB1·TGH1)만 주고
/// <b>열 위치와 개수를 주지 않기</b> 때문이다. 더구나 <see cref="IElementTypeCommandRepo"/> 에
/// 기둥 유형을 만드는 API 가 없어 기존 문서의 패밀리 유형을 골라야 한다(밸브실 방식).
/// 펌프장도 기둥은 만들지 않는다. docs/164_토출조_확인필요사항.md C-4 참조.</para>
///
/// <para><b>단, 배선은 열어 두었다</b> — <see cref="DischargeBasinInputDto.Frame"/> 에
/// <see cref="DischargeBasinFrameSpec"/> 를 채워 넣으면 아래 생성 루프가 그대로 돈다.
/// 지금은 항상 null 이라 빈 목록을 돌려받고 아무것도 만들지 않는다.</para>
/// </summary>
public sealed class CreateDischargeBasinUseCase : ICreateDischargeBasinUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly ILevelQueryRepo _levelQuery;
    private readonly ILevelCommandRepo _levelCommand;
    private readonly ISlabCommandRepo _slabCommand;
    private readonly IWallCommandRepo _wallCommand;
    private readonly IOpeningCommandRepo _openingCommand;
    private readonly IColumnCommandRepo _columnCommand;
    private readonly IBeamCommandRepo _beamCommand;
    private readonly IElementTypeCommandRepo _elementTypeCommand;
    private readonly ISharedParameterRepository _sharedParameter;
    private readonly IElementParameterCommandRepo _elementParameterCommand;
    private readonly IDialogService _dialog;
    private readonly IConcreteSettingsRepo _concreteSettings;

    public CreateDischargeBasinUseCase(
        ITransactionContext transaction,
        ILevelQueryRepo levelQuery,
        ILevelCommandRepo levelCommand,
        ISlabCommandRepo slabCommand,
        IWallCommandRepo wallCommand,
        IOpeningCommandRepo openingCommand,
        IColumnCommandRepo columnCommand,
        IBeamCommandRepo beamCommand,
        IElementTypeCommandRepo elementTypeCommand,
        ISharedParameterRepository sharedParameter,
        IElementParameterCommandRepo elementParameterCommand,
        IDialogService dialog,
        IConcreteSettingsRepo concreteSettings)
    {
        _transaction = transaction;
        _levelQuery = levelQuery;
        _levelCommand = levelCommand;
        _slabCommand = slabCommand;
        _wallCommand = wallCommand;
        _openingCommand = openingCommand;
        _columnCommand = columnCommand;
        _beamCommand = beamCommand;
        _elementTypeCommand = elementTypeCommand;
        _sharedParameter = sharedParameter;
        _elementParameterCommand = elementParameterCommand;
        _dialog = dialog;
        _concreteSettings = concreteSettings;
    }

    /// <summary>
    /// 생성 가능 여부를 먼저 확인한다. 막히면 사유를 돌려준다.
    /// 유형(단차)뿐 아니라 <b>제원까지</b> 본다 — 퇴화한 평면을 그대로 Revit 에 넘기면 예외로 터진다.
    /// </summary>
    public string? GetBlockingReason(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry)
        => DischargeBasinLayoutCalculator.GetUnsupportedReason(input, geometry);

    public void Execute(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry)
    {
        if (GetBlockingReason(input, geometry) is { } reason)
        {
            _dialog.Warn("토출조", reason);
            return;
        }

        using (_transaction)
        {
            try
            {
                _transaction.Begin("Create Discharge Basin");

                // 이 호출이 없으면 각 Repo 의 LookupParameter("DH_*") 가 null 을 돌려주고 ?.Set 이 조용히 무시된다.
                _sharedParameter.EnsureParameters(GetSharedParameterDefinitions());

                var basinLevels = EnsureLevels(geometry);

                var createdIds = new List<int>();

                // ── 슬래브 ──
                var slabs = DischargeBasinLayoutCalculator.CalculateSlabs(input, geometry);
                var slabLevels = _levelQuery.GetLevelIds(slabs.Select(s => s.LevelName));
                // 부재별 콘크리트는 문서 공용 설정을 쓴다(docs/249 § D). 토출조 바닥슬래브·상부슬래브는 슬래브 강도다.
                var concrete = new ConcreteTypeResolver(_elementTypeCommand, _concreteSettings.Load() ?? ConcreteSettings.Default);
                var slabList = slabs.ToList();
                var slabTypes = concrete.ResolveSlabTypes(slabList);
                for (var i = 0; i < slabList.Count; i++)
                    createdIds.Add(_slabCommand.CreateSlab(slabList[i], slabLevels[slabList[i].LevelName], slabTypes[i].TypeId));

                // ── 외벽 ──
                var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, geometry);
                var wallLevels = _levelQuery.GetLevelIds(walls.Select(w => w.LevelName));
                var wallTypes = concrete.ResolveWallTypes(walls.Select(w => w.Thickness)).ToDictionary(kv => kv.Key, kv => kv.Value.TypeId);
                foreach (var wall in walls)
                    createdIds.Add(_wallCommand.CreateLinearWall(wall, wallLevels[wall.LevelName], wallTypes[wall.Thickness]));

                // ── 상부 개구부 ──
                // 오프닝은 "바닥 개구부_사각형" 패밀리를 문서에서 찾아 쓴다. 없으면 Repo 가 안내만 하고 건너뛴다.
                // 실패(0)해도 나머지는 계속 만들되, 몇 개가 빠졌는지는 끝에 알린다(조용히 넘어가면 모르고 지나간다).
                var openingDefs = DischargeBasinLayoutCalculator.CalculateSlabOpenings(input, geometry);
                var openingFailures = 0;
                foreach (var opening in openingDefs)
                {
                    var openingId = _openingCommand.CreateSlabOpening(opening);
                    if (openingId == 0) openingFailures++;
                    else createdIds.Add(openingId);
                }

                // ── 펌프측 벽 토출관 관통구 ──
                // 펌프장 W2 의 같은 개구부와 같은 표고·같은 Y 에 뚫려 관이 일직선으로 이어진다.
                // DH_Zone 은 Repo 가 직접 쓰므로(개구부는 ID 를 돌려받지 못한다) 아래 일괄 기록 대상이 아니다.
                foreach (var wallOpening in DischargeBasinLayoutCalculator.CalculateWallOpenings(input, geometry))
                    _openingCommand.CreateWallOpening(wallOpening);

                // ── 기둥 · 거더 (확장 지점) ──
                // input.Frame 이 null 인 동안은 두 목록 모두 비어 있어 아무 일도 일어나지 않는다.
                createdIds.AddRange(CreateColumns(input, geometry, concrete));
                createdIds.AddRange(CreateGirders(input, geometry, concrete));

                // 생성한 부재에 DH_Zone 을 한 번에 찍는다. 생성 실패(0)는 제외한다.
                _elementParameterCommand.SetTextParameter(
                    createdIds.Where(id => id != 0).ToList(), "DH_Zone", DischargeBasinLayoutCalculator.ZoneName);

                // 레벨 후처리 — 평면뷰가 없으면 만들고, 3D 범위를 모델에 맞춘다(펌프장과 같은 마무리).
                FinishLevels(basinLevels);

                _transaction.Commit();

                var frameNote = input.Frame is null
                    ? "\r\n\r\n※ 기둥·거더는 원본 자료에 배치 정보가 없어 생성하지 않았습니다."
                    : string.Empty;
                var openingNote = openingFailures > 0
                    ? $"\r\n\r\n※ 상부 개구부 {openingDefs.Count}개소 중 {openingFailures}개소를 만들지 못했습니다."
                    : string.Empty;
                var placement = DischargeBasinPlacement.For(input, geometry);
                _dialog.Info("모델 생성",
                    $"토출조 모델 생성을 완료했습니다.\r\n\r\n" +
                    $"내공 {geometry.TB1:N0} ~ {geometry.TB2:N0} × {geometry.TLT:N0} mm\r\n" +
                    $"벽체 {geometry.TT1:N0} · 바닥슬래브 {geometry.TT2:N0} · 상부슬래브 {geometry.TT3:N0} mm\r\n" +
                    $"상단고 EL {geometry.TB:N3} · 바닥고 EL {geometry.TF:N3}\r\n" +
                    $"배치 — 펌프장 우측 {input.Separation:N1} m 이격 " +
                    $"(펌프측 벽 외면 X = {placement.OriginX - geometry.TT1:N0} mm)\r\n" +
                    $"토출관 관통구 {input.N}개소 — Ø{input.D:N0}, BOP EL {geometry.TE:N3}" +
                    frameNote + openingNote);
            }
            catch (Exception ex)
            {
                // 무엇 때문에 멈췄는지 알려 주지 않으면 사용자는 창이 닫힌 것만 본다.
                // 롤백으로 문서는 원래대로 돌아가므로 예외를 다시 던지지 않고 창을 살려 둔다(입력 보존).
                _transaction.Rollback();
                _dialog.Warn("토출조 생성 실패",
                    "모델을 만들지 못해 변경 사항을 모두 되돌렸습니다.\r\n\r\n" +
                    $"원인: {ex.GetType().Name} — {ex.Message}\r\n\r\n" +
                    "제원을 확인한 뒤 다시 시도해 주세요.");
            }
        }
    }

    /// <summary>
    /// 기둥. Frame 이 없으면 빈 목록이라 그대로 빠져나간다.
    /// 기둥 유형은 만들 수 없고 <b>기존 문서에서 찾아 쓰는 것</b>뿐이라, 못 찾으면(0) 만들지 않고 경고한다.
    /// </summary>
    private IReadOnlyList<int> CreateColumns(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry, ConcreteTypeResolver concrete)
    {
        var columns = DischargeBasinLayoutCalculator.CalculateColumns(input, geometry);
        if (columns.Count == 0) return Array.Empty<int>();

        var levels = _levelQuery.GetLevelIds(columns.SelectMany(c => new[] { c.BaseLevelName, c.TopLevelName }));
        var symbols = columns.Select(c => c.TypeName).Distinct()
            .ToDictionary(name => name, _elementTypeCommand.FindColumnSymbolByFamilyType);
        foreach (var symbolId in symbols.Values.Distinct())
            concrete.ApplyTo(symbolId, ConcreteMember.Column);

        var created = new List<int>(columns.Count);
        foreach (var column in columns)
        {
            var symbolId = symbols[column.TypeName];
            if (symbolId == 0)
            {
                _dialog.Warn("토출조", $"기둥 유형 '{column.TypeName}' 을 찾지 못해 기둥을 만들지 않았습니다.");
                return created;
            }
            created.Add(_columnCommand.CreateColumn(column, levels[column.BaseLevelName], levels[column.TopLevelName], symbolId));
        }
        return created;
    }

    /// <summary>거더. 보 유형은 단면으로 찾거나 만들 수 있다(펌프장과 같은 방식).</summary>
    private IReadOnlyList<int> CreateGirders(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry, ConcreteTypeResolver concrete)
    {
        var girders = DischargeBasinLayoutCalculator.CalculateGirders(input, geometry);
        if (girders.Count == 0) return Array.Empty<int>();

        var levels = _levelQuery.GetLevelIds(girders.Select(b => b.LevelName));
        var types = girders.Select(b => (b.Width, b.Height)).Distinct().ToDictionary(
            section => section,
            section => _elementTypeCommand.FindOrCreateBeamType(
                new BeamTypeSpec(section.Width, section.Height, $"{section.Width} x {section.Height}")));
        foreach (var typeId in types.Values.Distinct())
            concrete.ApplyTo(typeId, ConcreteMember.Beam);

        return girders
            .Select(g => _beamCommand.CreateBeam(g, levels[g.LevelName], types[(g.Width, g.Height)]))
            .ToList();
    }

    /// <summary>산출한 레벨을 만들되, 이미 있으면 표고만 갱신한다. 만들거나 찾은 레벨 ID를 돌려준다.</summary>
    private IReadOnlyList<(string Name, long Id)> EnsureLevels(DischargeBasinGeometryDto geometry)
    {
        var existing = _levelQuery.GetExistingLevelNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var levels = new List<(string, long)>(2);
        foreach (var level in DischargeBasinLayoutCalculator.CalculateLevels(geometry))
            levels.Add((level.Name, existing.Contains(level.Name)
                ? _levelCommand.UpdateLevel(level.Name, level.Elevation)
                : _levelCommand.CreateLevel(level.Name, level.Elevation)));
        return levels;
    }

    /// <summary>
    /// 레벨 마무리 — 평면뷰 생성 + 3D 범위 최대화(펌프장 UseCase 와 같은 처리).
    /// 이게 없으면 새 레벨에 평면뷰가 없고 레벨선의 3D 범위가 기본값으로 남아 단면·평면에서 안 보인다.
    /// 평면뷰 이름은 레벨명을 따르므로 <b>이미 같은 이름의 평면이 있으면 만들지 않는다</b>(중복 생성 방지).
    /// </summary>
    private void FinishLevels(IReadOnlyList<(string Name, long Id)> levels)
    {
        var existingPlans = _levelQuery.GetExistingPlanNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, id) in levels)
        {
            if (id == 0) continue;
            if (!existingPlans.Contains(name)) _levelCommand.CreatePlan(id);
            _levelCommand.Maximize3dExtents(id);
        }
    }

    /// <summary>DH_* 값을 쓰려면 먼저 바인딩이 있어야 한다. 상부 개구부가 GenericModel 이라 함께 포함한다.</summary>
    private static List<SharedParameterDefinition> GetSharedParameterDefinitions()
    {
        var categories = new List<ParameterCategory>
        {
            ParameterCategory.Floors,
            ParameterCategory.Walls,
            ParameterCategory.GenericModel,
            ParameterCategory.StructuralFraming,
            ParameterCategory.StructuralColumns,
        };

        return new List<SharedParameterDefinition>
        {
            new() { Name = "DH_Addin",       SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = categories, UserModifiable = false },
            new() { Name = "DH_ElementCode", SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = categories },
            new() { Name = "DH_Category",    SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = categories },
            new() { Name = "DH_Zone",        SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = categories },
            new() { Name = "DH_Part",        SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = categories },
            new() { Name = "DH_IsExterior",  SpecType = ParameterSpecType.YesNo, GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = new List<ParameterCategory> { ParameterCategory.Walls } },
        };
    }
}
