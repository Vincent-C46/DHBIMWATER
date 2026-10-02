using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>밸브실 입력값을 독립 구조 모델로 변환한다. Transaction은 이 UseCase에서만 관리한다.</summary>
public sealed class CreateValveRoomUseCase
{
    /// <summary>생성 트랜잭션 이름. ValveRoomRefreshSubscription이 우리 트랜잭션의 DocumentChanged를 걸러내는 데 쓴다(docs/207 § D).</summary>
    public const string TransactionName = "Create Valve Room";

    private readonly ITransactionContext _transaction;
    private readonly ILevelQueryRepo _levelQuery;
    private readonly ILevelCommandRepo _levelCommand;
    private readonly ISlabCommandRepo _slabCommand;
    private readonly IProjectLocationCommandRepo _projectLocationCommand;
    private readonly IFoundationCommandRepo _foundationCommand;
    private readonly IAirValveVoidCommandRepo _airValveVoidCommand;
    private readonly IDrainPitVoidCommandRepo _drainPitVoidCommand;
    private readonly IWallCommandRepo _wallCommand;
    private readonly IBeamCommandRepo _beamCommand;
    private readonly IColumnCommandRepo _columnCommand;
    private readonly IElementTypeCommandRepo _elementTypeCommand;
    private readonly IElementTypeQueryRepo _elementTypeQuery;
    private readonly ISharedParameterRepository _sharedParameter;
    private readonly IElementParameterCommandRepo _elementParameterCommand;
    private readonly IProjectInformationCommandRepo _projectInformationCommand;
    private readonly IViewCommandRepo _viewCommand;
    private readonly IDialogService _dialog;
    private readonly IConcreteSettingsRepo _concreteSettings;

    public CreateValveRoomUseCase(ITransactionContext transaction, ILevelQueryRepo levelQuery, ILevelCommandRepo levelCommand,
        IProjectLocationCommandRepo projectLocationCommand,
        ISlabCommandRepo slabCommand, IFoundationCommandRepo foundationCommand, IAirValveVoidCommandRepo airValveVoidCommand, IDrainPitVoidCommandRepo drainPitVoidCommand, IWallCommandRepo wallCommand, IBeamCommandRepo beamCommand,
        IColumnCommandRepo columnCommand, IElementTypeCommandRepo elementTypeCommand, IElementTypeQueryRepo elementTypeQuery,
        ISharedParameterRepository sharedParameter, IElementParameterCommandRepo elementParameterCommand,
        IProjectInformationCommandRepo projectInformationCommand, IViewCommandRepo viewCommand, IDialogService dialog,
        IConcreteSettingsRepo concreteSettings)
    {
        _transaction = transaction; _levelQuery = levelQuery; _levelCommand = levelCommand; _projectLocationCommand = projectLocationCommand; _slabCommand = slabCommand; _foundationCommand = foundationCommand; _airValveVoidCommand = airValveVoidCommand; _drainPitVoidCommand = drainPitVoidCommand;
        _wallCommand = wallCommand; _beamCommand = beamCommand; _columnCommand = columnCommand; _elementTypeCommand = elementTypeCommand;
        _elementTypeQuery = elementTypeQuery;
        _sharedParameter = sharedParameter; _elementParameterCommand = elementParameterCommand;
        _projectInformationCommand = projectInformationCommand; _viewCommand = viewCommand; _dialog = dialog;
        _concreteSettings = concreteSettings;
    }

    public void Execute(ValveRoomRequestDto request)
    {
        // 보 폭·기둥 단면은 읽기 전용 조회라 Transaction 밖에서 실측한다.
        // 보 폭은 상부 헌치를 보에 맞춰 끊는 데(docs/162 § D-8), 기둥 단면은 상부 보를 기둥 자리에서 끊는 데(docs/202 § E) 쓴다.
        var dto = ToGeometryDto(request, ResolveSluiceBeamWidth(request), ResolveSluiceColumnSection(request));
        ValveRoomGeometryCalculator.Validate(dto);
        using (_transaction)
        {
            try
            {
                _transaction.Begin(TransactionName);
                // 지난 실행(실패로 끝난 것 포함)의 남은 경고를 비운다 — 아래 완료 대화상자에 이번 실행분만 싣는다(docs/238 § C).
                _beamCommand.TakeWarnings();
                _airValveVoidCommand.TakeWarnings();

                // 이 호출이 없으면 각 Repo의 LookupParameter("DH_*")가 null을 돌려주고 ?.Set이 조용히 무시된다.
                // 배수지를 먼저 만든 문서에서만 우연히 기록되던 결함이었다(docs/142 § 4-1).
                _sharedParameter.EnsureParameters(GetValveRoomSharedParameterDefinitions());

                // 프로젝트 정보 3종(밸브실유형·관로구간·밸브실측점). 이미 있으면 EnsureParameters가 바인딩을 그대로 두므로
                // 값만 덮어써진다. 부재 매개변수와 카테고리가 달라 정의 목록을 따로 넘긴다.
                _sharedParameter.EnsureParameters(ValveRoomProjectInfoParameters.Definitions);
                _projectInformationCommand.SetTextParameters(new Dictionary<string, string>
                {
                    [ValveRoomProjectInfoParameters.RoomType] = request.RoomType,
                    [ValveRoomProjectInfoParameters.PipelineSection] = request.PipelineSection,
                    [ValveRoomProjectInfoParameters.StationPoint] = request.StationPoint,
                });

                // 이번 실행으로 만든 구조부재. 커밋 직전에 DH_Zone을 한 번에 찍는다.
                var createdIds = new List<int>();

                EnsureLevels(dto);
                // 내부원점·프로젝트 기준점은 X, Y만 가진다. 표고는 항상 0이며 구조물 높이는 레벨이 결정한다.
                _projectLocationCommand.SetInternalOriginSharedPosition(
                    request.SharedCoordinateX,
                    request.SharedCoordinateY,
                    request.TrueNorthToProjectNorthClockwiseDegrees);
                var slabs = ValveRoomGeometryCalculator.CalculateSlabs(dto).ToList();
                var slabLevels = _levelQuery.GetLevelIds(slabs.Select(s => s.LevelName));
                // 부재별 콘크리트는 문서 공용 설정을 쓴다(docs/249 § D). 기초·버림·슬래브는 Part로 나뉘고,
                // 같은 두께에 강도가 섞이면 유형 이름에 "_{강도}MPa"가 붙는다.
                var concrete = new ConcreteTypeResolver(_elementTypeCommand, _concreteSettings.Load() ?? DHBIMWATER.Core.Structures.ConcreteSettings.Default);
                var slabTypes = concrete.ResolveSlabTypes(slabs);
                var slabIds = new Dictionary<string, int>();
                for (var i = 0; i < slabs.Count; i++)
                {
                    var slab = slabs[i];
                    Require(slabTypes[i].MaterialId, ErrorCodes.VR_ConcreteMaterialNotFound, ConcreteMaterialMessage);
                    var slabTypeId = Require(slabTypes[i].TypeId,
                        ErrorCodes.VR_BaseTypeNotFound, $"바닥 유형 '{slabTypes[i].TypeName}'을 만들 수 없습니다. 복제할 바닥 유형이 프로젝트에 있어야 합니다.");
                    slabIds[slab.Part] = _slabCommand.CreateSlab(slab, slabLevels[slab.LevelName], slabTypeId);
                    createdIds.Add(slabIds[slab.Part]);
                }
                var pitWarnings = new List<string>();
                if (request.DrainPits.Count > 0 && slabIds.TryGetValue("기초", out var foundationSlabId))
                {
                    if (request.DrainPitVoidMapping is not { } mapping)
                        throw new DhbimwaterException(ErrorCodes.VR_DrainPitMappingRequired, "드레인 피트 Void 패밀리 매핑이 설정되지 않았습니다.");

                    foreach (var pit in request.DrainPits)
                    {
                        var hosts = new List<int> { foundationSlabId };
                        if (pit.Depth > dto.ProfileSpecDto.FoundationThickness && slabIds.TryGetValue("버림콘크리트", out var leanId))
                            hosts.Add(leanId);
                        var placement = _drainPitVoidCommand.CreateDrainPitVoid(
                            hosts, slabLevels[ValveRoomGeometryCalculator.BaseLevelName], pit, mapping,
                            dto.ProfileSpecDto.FoundationThickness, dto.ProfileSpecDto.PlainConcreteThickness);
                        if (placement.Warning != null) pitWarnings.Add(placement.Warning);
                        // 실물 피트 패밀리는 구조부재이므로 DH_Zone 기록 대상에 넣는다. 절단용 Void는 제외한다(docs/153 § E-4).
                        if (!placement.IsVoidCutting && placement.InstanceId != 0) createdIds.Add(placement.InstanceId);
                    }
                }
                foreach (var foundation in ValveRoomGeometryCalculator.CalculateFoundations(dto))
                {
                    var foundationId = Require(_foundationCommand.CreateFoundationFromFirstInstance(foundation),
                        ErrorCodes.VR_FoundationTypeNotFound, "공기밸브실 독립기초를 만들 수 없습니다. 독립기초 패밀리 유형을 하나 이상 로드하세요.");
                    createdIds.Add(foundationId);
                    concrete.ApplyTo(foundationId, DHBIMWATER.Core.Structures.ConcreteMember.Foundation);
                    if (request.AirSpec is { } air)
                        _airValveVoidCommand.CreateAirValveFoundationVoid(foundationId, ValveRoomGeometryCalculator.CalculateAirValveVoid(dto, air));
                }
                var walls = ValveRoomGeometryCalculator.CalculateWalls(dto).ToList();
                var wallLevelIds = _levelQuery.GetLevelIds(walls.Select(w => w.LevelName));
                Require(concrete.MaterialId(DHBIMWATER.Core.Structures.ConcreteMember.Wall), ErrorCodes.VR_ConcreteMaterialNotFound, ConcreteMaterialMessage);
                var wallTypeIds = concrete.ResolveWallTypes(walls.Select(w => w.Thickness)).ToDictionary(kv => kv.Key,
                    kv => Require(kv.Value.TypeId,
                        ErrorCodes.VR_BaseTypeNotFound, $"벽 유형 '{kv.Value.TypeName}'을 만들 수 없습니다. 복제할 벽 유형이 프로젝트에 있어야 합니다."));
                foreach (var wall in walls)
                    createdIds.Add(Require(_wallCommand.CreateLinearWall(wall, wallLevelIds[wall.LevelName], wallTypeIds[wall.Thickness]),
                        ErrorCodes.VR_WallCreateFailed, $"벽체 '{wall.ElementCode}'를 만들지 못했습니다. 벽 높이·두께·위치를 확인하세요."));
                // 제수밸브실 보와 헌치를 한 목록으로 합친다. 아래 유형 해석·생성 루프가 둘을 함께 처리한다(docs/153 § C-1).
                var beams = ValveRoomGeometryCalculator.CalculateBeams(dto)
                    .Concat(ValveRoomGeometryCalculator.CalculateHaunches(dto)).ToList();
                var beamLevels = _levelQuery.GetLevelIds(beams.Select(b => b.LevelName));
                // 보 유형은 콤보가 "패밀리명 : 타입명"을 넘긴다 — 타입명 완전일치인 FindBeamSymbol로는 못 찾는다(docs/159 § C-3).
                // 유형을 못 찾으면 0이 CreateBeam에 들어가 보·헌치가 조용히 빠지고 "완료"가 떴다(docs/207 § B).
                var beamTypes = beams.GroupBy(b => b.Part == "HAUNCH" ? "HAUNCH" : b.TypeName ?? $"{b.Width} x {b.Height}").ToDictionary(g => g.Key, g => Require(
                    g.Key == "HAUNCH" ? ResolveHaunchSymbol(g.First().TypeName) : !string.IsNullOrEmpty(g.First().TypeName) ? _elementTypeCommand.FindStructuralFramingSymbolByFamilyType(g.First().TypeName) : _elementTypeCommand.FindOrCreateBeamType(new DHBIMWATER.Core.Structures.BeamTypeSpec(g.First().Width, g.First().Height, g.Key)),
                    ErrorCodes.VR_BeamTypeNotFound, BeamTypeNotFoundMessage(g.Key, g.First().TypeName)));
                // 보·헌치 유형의 구조 재료를 보 강도로 바꾼다 — 유형 매개변수라 문서 전체에 반영된다(docs/249 § 0).
                foreach (var beamTypeId in beamTypes.Values.Distinct())
                    concrete.ApplyTo(beamTypeId, DHBIMWATER.Core.Structures.ConcreteMember.Beam);
                // 헌치는 레벨(ElementCode = VR-H1/H2/H3)별로 모아 두었다가 형상 결합한다.
                // "같은 레벨끼리만" 결합하는 그룹 판정은 도메인 지식이라 Repo가 아니라 여기서 한다(docs/162 § C-4).
                var haunchIdsByLevel = new Dictionary<string, List<int>>();
                foreach (var beam in beams)
                {
                    // VR-B 보는 결합할 상부슬래브를 넘겨 Repo가 문서 전체 Floor를 훑지 않게 한다(docs/207 § C).
                    var joinTargetSlabId = beam.ElementCode == "VR-B" && slabIds.TryGetValue("상부슬래브", out var upperSlabId) ? upperSlabId : 0;
                    var beamId = Require(_beamCommand.CreateBeam(beam, beamLevels[beam.LevelName], beamTypes[beam.Part == "HAUNCH" ? "HAUNCH" : beam.TypeName ?? $"{beam.Width} x {beam.Height}"], joinTargetSlabId),
                        ErrorCodes.VR_BeamCreateFailed, $"보 '{beam.ElementCode}'를 만들지 못했습니다(유형 또는 레벨을 찾을 수 없음).");
                    createdIds.Add(beamId);
                    if (beam.Part != "HAUNCH") continue;
                    if (!haunchIdsByLevel.TryGetValue(beam.ElementCode, out var levelHaunches))
                        haunchIdsByLevel[beam.ElementCode] = levelHaunches = new List<int>();
                    levelHaunches.Add(beamId);
                }
                foreach (var levelHaunches in haunchIdsByLevel.Values) _beamCommand.JoinHaunches(levelHaunches);
                var columns = ValveRoomGeometryCalculator.CalculateColumns(dto).ToList();
                var columnLevels = _levelQuery.GetLevelIds(columns.SelectMany(c => new[] { c.BaseLevelName, c.TopLevelName }));
                // 기둥도 콤보가 "패밀리명 : 타입명"을 넘긴다(docs/159 § C-3).
                var columnTypes = columns.Select(c => c.TypeName).Distinct().ToDictionary(name => name, name => Require(_elementTypeCommand.FindColumnSymbolByFamilyType(name),
                    ErrorCodes.VR_ColumnTypeNotFound, $"기둥 유형 '{name}'을 찾을 수 없습니다. 유형이 삭제·개명됐으면 창을 다시 열어 선택하세요."));
                foreach (var columnTypeId in columnTypes.Values.Distinct())
                    concrete.ApplyTo(columnTypeId, DHBIMWATER.Core.Structures.ConcreteMember.Column);
                foreach (var column in columns)
                    createdIds.Add(Require(_columnCommand.CreateColumn(column, columnLevels[column.BaseLevelName], columnLevels[column.TopLevelName], columnTypes[column.TypeName]),
                        ErrorCodes.VR_ColumnCreateFailed, $"기둥 '{column.ElementCode}'를 만들지 못했습니다."));
                // 평면도(기초 상부) · 단면도 A-A/B-B 작성(docs/246). 실패해도 모델은 커밋하고 확인 필요 목록에 싣는다.
                var viewWarnings = CreateViews(dto);
                // DH_Zone에는 라디오에서 고른 실제 유형명이 들어간다(타이밸브실이면 "타이밸브실").
                // 정의 기반 기록(ValveRoomGeometryCalculator의 Zone)만으로는 드레인 피트 실물이 빠지므로
                // 여기서 한 번에 덮어쓴다 — RevitDrainPitVoidCommandRepo는 Zone을 쓰지 않는다(docs/155 § C).
                // 절단용 Void 패밀리(드레인 피트·공기밸브 관통)는 대상에서 제외한다(docs/142 § 4).
                // 실물 드레인 피트 패밀리는 위 배치 시점에 createdIds에 넣는다(docs/153 § E-4).
                _elementParameterCommand.SetTextParameter(createdIds, "DH_Zone", request.RoomType);
                _transaction.Commit();
                // 트랜잭션 도중 모달을 띄우던 경고(보 맞춤·결합, Void 절단)를 드레인 피트 경고와 함께 한 번에 보여준다(docs/238 § C).
                var warnings = pitWarnings.Concat(viewWarnings).Concat(_beamCommand.TakeWarnings()).Concat(_airValveVoidCommand.TakeWarnings()).ToList();
                var warningMessage = warnings.Count == 0 ? string.Empty : $"\n\n확인 필요 {warnings.Count}건:\n- {string.Join("\n- ", warnings)}";
                _dialog.Info("모델 생성", $"{request.RoomType} 모델 생성을 완료했습니다.{warningMessage}");
            }
            catch { _transaction.Rollback(); throw; }
        }
    }

    /// <summary>헌치 유형 해석. UI가 넘기는 값은 "패밀리명 : 타입명"이라 타입명 완전일치인 FindBeamSymbol로는 잡히지 않는다(docs/156 § E-3).
    /// <para>비어 있으면 이름 기반 자동 탐색이고, 고른 유형을 못 찾아도(그 사이 삭제·개명) 0을 그대로 넘기지 않고
    /// 자동 탐색으로 한 번 더 폴백한다 — 0이 CreateBeam에 들어가면 헌치가 통째로 빠진다.</para></summary>
    private int ResolveHaunchSymbol(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return _elementTypeCommand.FindHaunchBeamSymbol();
        var id = _elementTypeCommand.FindStructuralFramingSymbolByFamilyType(typeName!);
        return id != 0 ? id : _elementTypeCommand.FindHaunchBeamSymbol();
    }

    /// <summary>Repo는 "0 = 실패" 규약으로 배수지·펌프장·토출조와 공유되므로 Repo를 고치지 않고 여기서 중단한다(docs/207 § B).
    /// 0이 createdIds에 들어가면 반쪽 모델이 커밋되고 "완료"가 뜬다.</summary>
    private static int Require(int elementId, string code, string message)
        => elementId != 0 ? elementId : throw new DhbimwaterException(code, message);

    /// <summary>VR-201 문구(docs/238 § A). 헌치 자동 탐색이면 TypeName이 빈 문자열이라 종전 문구가 `헌치 유형 ''을…`로 나왔다.</summary>
    private static string BeamTypeNotFoundMessage(string key, string? typeName)
    {
        if (key != "HAUNCH")
            return $"보 유형 '{typeName ?? key}'을 찾을 수 없습니다. 유형이 삭제·개명됐으면 창을 다시 열어 선택하세요.";
        return string.IsNullOrWhiteSpace(typeName)
            ? "헌치 유형(자동 탐색)을 찾을 수 없습니다. 이름에 '헌치' 또는 'haunch'가 든 구조 프레임 패밀리를 로드하거나 헌치 체크를 끄세요."
            : $"헌치 유형 '{typeName}'을 찾을 수 없습니다. 유형이 삭제·개명됐으면 창을 다시 열어 선택하거나 헌치 체크를 끄세요.";
    }

    private const string ConcreteMaterialMessage = "콘크리트 재료를 찾을 수 없습니다. 프로젝트에 이름에 '콘크리트' 또는 'Concrete'가 들어간 재료를 하나 이상 두세요.";

    /// <summary>기초 상부 EL로 산출한 레벨을 생성하되, 이미 있으면 표고만 갱신한다.
    /// TODO(docs/207 § 0-6): 같은 문서 2번째 밸브실이 첫 밸브실 레벨 표고를 덮어쓴다 — 식별자 부여는 별건 기획.</summary>
    private void EnsureLevels(ValveRoomGeometryRequestDto dto)
    {
        var levels = _levelQuery.GetExistingLevelNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var level in ValveRoomGeometryCalculator.CalculateLevels(dto))
            if (levels.Contains(level.Name)) _levelCommand.UpdateLevel(level.Name, level.Elevation);
            else _levelCommand.CreateLevel(level.Name, level.Elevation);
    }

    /// <summary>기초 상부 구조 평면도와 단면도 A-A/B-B를 만든다(docs/246). 뷰마다 실패를 모아 돌려주고 모델 생성은 계속한다.
    /// <para>평면도는 이미 있으면 건너뛰고(펌프장과 같음), 단면도는 Repo가 같은 이름을 지우고 다시 만든다.
    /// 공기밸브실은 기초 슬래브가 없어 slabLevels에 기초 상부 레벨이 없을 수 있으므로 레벨 ID를 따로 조회한다.</para></summary>
    private List<string> CreateViews(ValveRoomGeometryRequestDto dto)
    {
        var warnings = new List<string>();
        var planName = ValveRoomGeometryCalculator.BaseLevelName;
        try
        {
            var existingPlans = _levelQuery.GetExistingPlanNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!existingPlans.Contains(planName))
                _levelCommand.CreatePlan(_levelQuery.GetLevelIds(new[] { planName })[planName]);
        }
        catch (Exception ex)
        {
            warnings.Add($"{planName} 평면도 작성 실패: {ex.Message}");
        }

        foreach (var cut in ValveRoomGeometryCalculator.CalculateSections(dto))
        {
            try
            {
                _viewCommand.CreateSectionView(cut);
            }
            catch (Exception ex)
            {
                warnings.Add($"{cut.Name} 단면도 작성 실패: {ex.Message}");
            }
        }
        return warnings;
    }

    /// <summary>제수밸브실 보의 단면 폭(mm). 보가 없거나 유형에 단면폭 파라미터가 없으면 0이고,
    /// 그러면 계산기가 상부 헌치를 끊지 않아 종전과 같은 통짜 헌치가 된다(docs/162 § C-3).</summary>
    private double ResolveSluiceBeamWidth(ValveRoomRequestDto request) =>
        string.IsNullOrWhiteSpace(request.SluiceSpec?.BeamTypeName)
            ? 0
            : _elementTypeQuery.GetStructuralFramingSectionWidthMm(request.SluiceSpec!.BeamTypeName);

    /// <summary>제수밸브실 기둥의 단면 치수(mm). 기둥 유형이 없거나 유형에 단면 파라미터가 없으면 0이고,
    /// 그러면 계산기가 상부 보를 끊지 않아 종전과 같은 통짜 보가 된다(docs/202 § D-2).</summary>
    private (double WidthX, double DepthY) ResolveSluiceColumnSection(ValveRoomRequestDto request) =>
        string.IsNullOrWhiteSpace(request.SluiceSpec?.ColumnTypeName)
            ? (0, 0)
            : _elementTypeQuery.GetStructuralColumnSectionSizeMm(request.SluiceSpec!.ColumnTypeName);

    private static ValveRoomGeometryRequestDto ToGeometryDto(ValveRoomRequestDto r, double sluiceBeamWidth,
        (double WidthX, double DepthY) sluiceColumnSection) => new(
        new ValveRoomDesignConditionDto(r.RoomType, r.FoundationTopEl) { GeometryType = NormalizeGeometryType(r.RoomType) },
        new ValveRoomPlanSpecDto(r.InnerWidth, r.InnerLength),
        new ValveRoomProfileSpecDto(r.InnerHeight, r.PlainConcreteThickness, r.FoundationThickness, r.FoundationToe, r.OuterWallThickness, r.UpperSlabThickness),
        r.MudSpec,
        r.SluiceSpec)
    {
        DrainPits = r.DrainPits,
        HasUpperHaunch = r.HasUpperHaunch,
        HasLowerHaunch = r.HasLowerHaunch,
        HaunchTypeName = r.HaunchTypeName,
        SluiceBeamWidth = sluiceBeamWidth,
        SluiceColumnWidthX = sluiceColumnSection.WidthX,
        SluiceColumnDepthY = sluiceColumnSection.DepthY
    };

    /// <summary>타이밸브실은 이토밸브실 형상을 그대로 쓴다(docs/142 § 1).
    /// RoomType은 DH_Zone에 실제 이름으로 남아야 하므로 여기서만 정규화한다.</summary>
    private static string NormalizeGeometryType(string roomType) => roomType == "타이밸브실" ? "이토밸브실" : roomType;

    /// <summary>밸브실 부재에 DH_* 값을 쓰려면 먼저 바인딩이 있어야 한다.
    /// 공기밸브실은 독립기초를 쓰므로 배수지 목록에 없는 StructuralFoundation을 반드시 포함한다.</summary>
    private static List<SharedParameterDefinition> GetValveRoomSharedParameterDefinitions()
    {
        var categories = new List<ParameterCategory>
        {
            ParameterCategory.StructuralFraming,
            ParameterCategory.StructuralColumns,
            ParameterCategory.StructuralFoundation,
            ParameterCategory.GenericModel,
            ParameterCategory.Floors,
            ParameterCategory.Walls,
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
