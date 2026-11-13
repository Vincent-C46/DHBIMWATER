using DHBIMWATER.Application.DTOs.Revit.Reservoir;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DHBIMWATER.Application.UseCases.AutoGenerator
{
    public class CreateReservoirUseCase
    {
        #region Fields
        private readonly ITransactionContext _tx;
        private readonly IDialogService _dialogService;
        private readonly ILevelQueryRepo _levelQueryRepo;
        private readonly ILevelCommandRepo _levelCmdRepo;
        private readonly ISlabCommandRepo _slabCmdRepo;
        private readonly IWallCommandRepo _wallCmdRepo;
        private readonly IBeamCommandRepo _beamCmdRepo;
        private readonly IColumnCommandRepo _columnCmdRepo;
        private readonly IGenericModelCommandRepo _genericModelCmdRepo;
        private readonly IElementTypeCommandRepo _elementTypeCmdRepo;
        private readonly ISharedParameterRepository _sharedParameterRepo;
        private readonly ClassifyExteriorWallsUseCase _classifyWallsUseCase;
        private readonly IConcreteSettingsRepo _concreteSettingsRepo;
        private readonly IViewCommandRepo _viewCmdRepo;
        private readonly IStructureModelRepo _structureModelRepo;
        #endregion

        #region Constructor
        public CreateReservoirUseCase(
            ITransactionContext tx,
            IDialogService dialogService,
            ILevelQueryRepo levelQueryRepo,
            ILevelCommandRepo levelCmdRepo,
            ISlabCommandRepo slabCmdRepo,
            IWallCommandRepo wallCmdRepo,
            IBeamCommandRepo beamCmdRepo,
            IColumnCommandRepo columnCmdRepo,
            IGenericModelCommandRepo genericModelCmdRepo, IElementTypeCommandRepo elementTypeCmdRepo,
            ISharedParameterRepository sharedParameterRepo,
            ClassifyExteriorWallsUseCase classifyWallsUseCase,
            IConcreteSettingsRepo concreteSettingsRepo,
            IViewCommandRepo viewCmdRepo,
            IStructureModelRepo structureModelRepo)
        {
            _tx = tx;
            _dialogService = dialogService;
            _levelQueryRepo = levelQueryRepo;
            _levelCmdRepo = levelCmdRepo;
            _slabCmdRepo = slabCmdRepo;
            _wallCmdRepo = wallCmdRepo;
            _beamCmdRepo = beamCmdRepo;
            _columnCmdRepo = columnCmdRepo;
            _genericModelCmdRepo = genericModelCmdRepo;
            _elementTypeCmdRepo = elementTypeCmdRepo;
            _sharedParameterRepo = sharedParameterRepo;
            _classifyWallsUseCase = classifyWallsUseCase;
            _concreteSettingsRepo = concreteSettingsRepo;
            _viewCmdRepo = viewCmdRepo;
            _structureModelRepo = structureModelRepo;
        }
        #endregion

        #region Methods
        public void Execute(ReservoirCreationRequestDto dto)
        {
            // 기존 배수지 있으면 개수 확인. 취소면 문서 변경 없음(docs/320 § C)
            if (StructureRegeneration.Prepare(_structureModelRepo, _dialogService, StructureRegeneration.Reservoir) is not { } regeneration)
                return;

            using (_tx)
            {
                try
                {
                    _tx.Begin("Create Reservoir");

                    #region 0. 공유 매개변수 생성
                    _sharedParameterRepo.EnsureParameters(GetReservoirSharedParameterDefinitions());
                    regeneration.DeleteExisting();
                    #endregion

                    #region 1. 레벨 생성
                    var existingLevels = _levelQueryRepo.GetExistingLevelNames();
                    var existingPlanNames = _levelQueryRepo.GetExistingPlanNames();

                    foreach (var lvl in ReservoirGeometryCalculator.CalculateLevels(dto))
                    {
                        // 이름 완전일치. 부분일치면 "…_old" 레벨을 잡아 정확한 이름 레벨이 안 생김(docs/289 § E-4)
                        var existing = existingLevels.FirstOrDefault(s => s == lvl.Name);
                        long levelId;
                        if (existing != null)
                            levelId = _levelCmdRepo.UpdateLevel(existing, lvl.Elevation);
                        else
                            levelId = _levelCmdRepo.CreateLevel(lvl.Name, lvl.Elevation);

                        bool isWaterElevationLevel =
                            lvl.Name == ReservoirGeometryCalculator.LWLLevelName ||
                            lvl.Name == ReservoirGeometryCalculator.HWLLevelName;
                        if (!isWaterElevationLevel && !existingPlanNames.Contains(lvl.Name))
                            _levelCmdRepo.CreatePlan(levelId);
                    }
                    #endregion

                    #region 2. 슬래브 생성
                    var slabDefs = ReservoirGeometryCalculator.CalculateSlabs(dto).ToList();
                    var slabLevels = _levelQueryRepo.GetLevelIds(slabDefs.Select(s => s.LevelName));
                    // 부재별 콘크리트는 문서 공용 설정을 쓴다(docs/249 § D). 기초·버림·슬래브(덧침 포함)는 Part로 나뉜다.
                    var concrete = new ConcreteTypeResolver(_elementTypeCmdRepo, _concreteSettingsRepo.Load() ?? Core.Structures.ConcreteSettings.Default);
                    var slabTypes = concrete.ResolveSlabTypes(slabDefs);
                    // 수조부 상부슬래브(S1)는 마지막에 보·헌치·벽과 결합(docs/289 § D)
                    var upperSlabIds = new List<int>();
                    for (var i = 0; i < slabDefs.Count; i++)
                    {
                        var slabId = _slabCmdRepo.CreateSlab(slabDefs[i], slabLevels[slabDefs[i].LevelName], slabTypes[i].TypeId);
                        if (slabDefs[i].ElementCode == "S1") upperSlabIds.Add(slabId);
                    }
                    #endregion

                    #region 3. 벽체 생성
                    var wallDefs = ReservoirGeometryCalculator.CalculateLinearWalls(dto).ToList();
                    var levelIds = _levelQueryRepo.GetLevelIds(wallDefs.Select(w => w.LevelName));
                    var wallTypeIds = concrete.ResolveWallTypes(wallDefs.Select(w => w.Thickness)).ToDictionary(kv => kv.Key, kv => kv.Value.TypeId);
                    foreach (var wallDef in wallDefs)
                        _wallCmdRepo.CreateLinearWall(wallDef, levelIds[wallDef.LevelName], wallTypeIds[wallDef.Thickness]);
                    _classifyWallsUseCase.Execute();
                    #endregion

                    #region 4. 기둥 생성
                    var columnDefs = ReservoirGeometryCalculator.CalculateColumns(dto).ToList();
                    var columnLevels = _levelQueryRepo.GetLevelIds(columnDefs.SelectMany(c => new[] { c.BaseLevelName, c.TopLevelName }));
                    // 기둥 B·H 입력으로 유형 찾고 없으면 콘크리트 사각형 기둥 복제(docs/325 § C). 0이면 기둥이 조용히 빠지므로 중단
                    var th = dto.ThicknessDto;
                    var columnTypes = columnDefs.Select(c => c.TypeName).Distinct().ToDictionary(name => name,
                        name => Require(_elementTypeCmdRepo.FindOrCreateColumnType(new Core.Structures.ColumnTypeSpec(th.ColumnB, th.ColumnH, name)),
                            $"기둥 유형 '{name}'을 만들 수 없습니다. b·h 매개변수가 있는 콘크리트 사각형 기둥 패밀리를 로드하세요."));
                    foreach (var columnTypeId in columnTypes.Values.Distinct())
                        concrete.ApplyTo(columnTypeId, Core.Structures.ConcreteMember.Column);
                    foreach (var colDef in columnDefs)
                        _columnCmdRepo.CreateColumn(colDef, columnLevels[colDef.BaseLevelName], columnLevels[colDef.TopLevelName], columnTypes[colDef.TypeName]);
                    #endregion

                    #region 5. 보 생성
                    // 보는 기둥 면에서, 헌치는 보 폭만큼 끊음 — 단면 크기 필요(docs/289 § C-1)
                    // 단면 크기는 입력값 그대로(유형 실측 안 함, docs/325 § D)
                    var memberSizes = new ReservoirMemberSizes(th.ColumnB, th.ColumnH, th.BeamW);
                    var beamDefs = ReservoirGeometryCalculator.CalculateBeams(dto, memberSizes).ToList();
                    var beamLevels = _levelQueryRepo.GetLevelIds(beamDefs.Select(b => b.LevelName));
                    // 유형 키 = TypeName("W x H" / "헌치 b x h"). 보는 헌치 패밀리 뺀 콘크리트 사각형 보, 헌치는 헌치 패밀리에서 복제(docs/325 § C)
                    var beamTypes = beamDefs.GroupBy(b => b.TypeName).ToDictionary(g => g.Key, g =>
                    {
                        var first = g.First();
                        var spec = new Core.Structures.BeamTypeSpec(first.Width, first.Height, g.Key);
                        return first.Part == "HAUNCH"
                            ? Require(_elementTypeCmdRepo.FindOrCreateHaunchType(spec),
                                $"헌치 유형 '{g.Key}'을 만들 수 없습니다. 패밀리 이름에 '헌치' 또는 'haunch'가 든 구조 프레임 패밀리(크기 매개변수 b·h)를 로드하거나 헌치 체크를 끄세요.")
                            : Require(_elementTypeCmdRepo.FindOrCreateBeamType(spec),
                                $"보 유형 '{g.Key}'을 만들 수 없습니다. 헌치가 아닌 콘크리트 사각형 보 패밀리(b·h)를 로드하세요.");
                    });
                    foreach (var beamTypeId in beamTypes.Values.Distinct())
                        concrete.ApplyTo(beamTypeId, Core.Structures.ConcreteMember.Beam);
                    var haunchIdsByLevel = new Dictionary<string, List<int>>();
                    foreach (var beamDef in beamDefs)
                    {
                        var beamId = _beamCmdRepo.CreateBeam(beamDef, beamLevels[beamDef.LevelName], beamTypes[beamDef.TypeName]);
                        if (beamDef.Part != "HAUNCH") continue;
                        if (!haunchIdsByLevel.TryGetValue(beamDef.LevelName, out var ids))
                            haunchIdsByLevel[beamDef.LevelName] = ids = new List<int>();
                        ids.Add(beamId);
                    }
                    // 헌치 코너·T접합 겹침 정리(레거시 RH:2978-3014, 밸브실 UseCase와 같은 방식)
                    foreach (var levelHaunches in haunchIdsByLevel.Values) _beamCmdRepo.JoinHaunches(levelHaunches);
                    #endregion

                    #region 6. 일반 패밀리 배치 (단차버림 Con'c, PIT)
                    var genericDefs = ReservoirGeometryCalculator.CalculateGenericModels(dto).ToList();
                    var genericLevels = _levelQueryRepo.GetLevelIds(genericDefs.Select(g => g.LevelName));
                    var genericSymbols = genericDefs.Select(g => g.SymbolName).Distinct().ToDictionary(name => name, _elementTypeCmdRepo.FindGenericModelSymbol);
                    // 로드 안 된 패밀리는 완료창에 1개 1줄로 알리고 배치 건너뜀. 인스턴스마다 경고 뜨던 것 방지.
                    var placementWarnings = genericSymbols.Where(kv => kv.Value == 0).Select(kv => $"패밀리 없음(배치 안 함): {kv.Key}").ToList();
                    foreach (var def in genericDefs.Where(g => genericSymbols[g.SymbolName] != 0))
                        _genericModelCmdRepo.PlaceInstance(def, genericLevels[def.LevelName], genericSymbols[def.SymbolName]);
                    placementWarnings.AddRange(_genericModelCmdRepo.TakeWarnings());
                    #endregion

                    #region 7. 상부 슬래브 결합 (레거시 RH:2940-2975)
                    var joinWarnings = new List<string>();
                    foreach (var slabId in upperSlabIds)
                        joinWarnings.AddRange(_slabCmdRepo.JoinWithIntersectingFramingAndWalls(slabId));
                    #endregion

                    #region 8. 단면뷰 A~D (레거시 RH:2731-2845)
                    foreach (var cut in ReservoirGeometryCalculator.CalculateSectionViews(dto))
                        _viewCmdRepo.CreateSectionView(cut);
                    #endregion

                    // TODO: 오프닝 배치 (파이프 관통 슬리브 등) — ReservoirGeometryCalculator.CalculateOpenings() 추가 후 구현

                    regeneration.MarkCreated();
                    _tx.Commit();

                    var message = "배수지 작성 완료";
                    if (placementWarnings.Count > 0)
                        message += $"\n\n배치 경고 {placementWarnings.Count}건:\n" + string.Join("\n", placementWarnings);
                    if (joinWarnings.Count > 0)
                        message += $"\n\n결합 경고 {joinWarnings.Count}건:\n" + string.Join("\n", joinWarnings.Take(10));
                    _dialogService.Info("Success", message);
                }
                catch (Exception ex)
                {
                    _dialogService.Warn("Rollback", ex.Message);
                    _tx.Rollback();
                    throw;
                }
            }
        }
        #endregion

        #region Private Helpers
        /// <summary>유형 ID 0이면 예외 → catch에서 롤백·알림. 0이 Create에 들어가 부재가 조용히 빠지던 것 방지(밸브실 docs/207 § B와 같은 원칙).</summary>
        private static int Require(int typeId, string message)
            => typeId != 0 ? typeId : throw new InvalidOperationException(message);

        private static List<SharedParameterDefinition> GetReservoirSharedParameterDefinitions()
        {
            var structuralCategories = new List<ParameterCategory>
            {
                ParameterCategory.StructuralFraming,
                ParameterCategory.StructuralColumns,
                ParameterCategory.GenericModel,
                ParameterCategory.Floors,
                ParameterCategory.Walls,
                ParameterCategory.Stairs,
            };

            return new List<SharedParameterDefinition>
            {
                new SharedParameterDefinition { Name = "DH_ElementCode", SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_Addin",       SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories, UserModifiable = false },
                new SharedParameterDefinition { Name = StructureRegeneration.ParameterName, SpecType = ParameterSpecType.Text, GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = structuralCategories, UserModifiable = false },
                new SharedParameterDefinition { Name = "DH_Class",       SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_Category",    SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_Zone",        SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_Part",        SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_HostId",      SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = new List<ParameterCategory> { ParameterCategory.GenericModel } },
                new SharedParameterDefinition { Name = "DH_Formula",     SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = structuralCategories },
                new SharedParameterDefinition { Name = "DH_IsExterior",  SpecType = ParameterSpecType.YesNo, GroupType = ParameterGroupType.Data,         BindingType = ParameterBindingType.Instance, Categories = new List<ParameterCategory> { ParameterCategory.Walls } },
                new SharedParameterDefinition { Name = "DH_뷰 카테고리", SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.IdentityData, BindingType = ParameterBindingType.Instance, Categories = new List<ParameterCategory> { ParameterCategory.Views } },
                new SharedParameterDefinition { Name = "DH_뷰 타입",     SpecType = ParameterSpecType.Text,  GroupType = ParameterGroupType.IdentityData, BindingType = ParameterBindingType.Instance, Categories = new List<ParameterCategory> { ParameterCategory.Views } },
            };
        }
        #endregion
    }
}

