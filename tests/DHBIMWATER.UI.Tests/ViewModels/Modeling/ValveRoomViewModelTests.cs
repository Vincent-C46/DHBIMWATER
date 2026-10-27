using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>창이 떠 있는 동안 패밀리가 로드·편집됐을 때의 유형 목록 재조회(docs/154) 검증.</summary>
public class ValveRoomViewModelTests
{
    private const string NotSelected = "(선택 안 함)";
    private const string NotUsed = "(사용 안 함)";
    private const string HaunchTypeAuto = "(자동 탐색)";
    private const string VoidFamily = "보이드_사각형 : 기본";
    // 자동 추정 키워드("피트"·"보이드"…)에 걸리지 않는 이름이어야 "사용자가 직접 고른 값" 검증이 성립한다.
    private const string SolidFamily = "실물모델 : 기본";

    [Fact]
    public void ReloadTypeNames_새로_로드된_패밀리가_목록에_나타난다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        Assert.DoesNotContain("보이드_사각형 : 대형", vm.DrainPitVoidFamilyNames);

        repo.GenericModelTypeNames.Add("보이드_사각형 : 대형");
        repo.ParameterNamesByFamily["보이드_사각형 : 대형"] = ["길이", "폭", "깊이"];
        vm.ReloadTypeNames();

        Assert.Contains("보이드_사각형 : 대형", vm.DrainPitVoidFamilyNames);
    }

    [Fact]
    public void ReloadTypeNames_선택한_패밀리가_남아있으면_유지한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        vm.SelectedDrainPitVoidFamily = SolidFamily;

        vm.ReloadTypeNames();

        Assert.Equal(SolidFamily, vm.SelectedDrainPitVoidFamily);
        Assert.Equal("Length", vm.SelectedDrainPitLengthParam);
    }

    [Fact]
    public void ReloadTypeNames_선택한_패밀리가_사라지면_자동추정으로_대체한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        vm.SelectedDrainPitVoidFamily = SolidFamily;

        repo.GenericModelTypeNames.Remove(SolidFamily);
        vm.ReloadTypeNames();

        Assert.Equal(VoidFamily, vm.SelectedDrainPitVoidFamily);
    }

    [Fact]
    public void 기본_Void_패밀리는_피트_이름을_보이드보다_먼저_고른다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.GenericModelTypeNames.Add("드레인피트 : 기본");
        repo.ParameterNamesByFamily["드레인피트 : 기본"] = ["가로", "세로", "깊이"];

        var vm = CreateViewModel(repo);

        Assert.Equal("드레인피트 : 기본", vm.SelectedDrainPitVoidFamily);
    }

    [Fact]
    public void 기본_Void_패밀리는_피트라도_원형류는_거른다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.GenericModelTypeNames.Insert(0, "드레인피트_원형 : 기본");
        repo.ParameterNamesByFamily["드레인피트_원형 : 기본"] = ["지름", "깊이"];

        var vm = CreateViewModel(repo);

        // 사각형만 지원하는 형상 계산과 어긋나므로 원형 피트 대신 보이드_사각형으로 내려간다.
        Assert.Equal(VoidFamily, vm.SelectedDrainPitVoidFamily);
    }

    [Fact]
    public void 가로_세로_매개변수를_Length_Width_기본값으로_연결한다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.GenericModelTypeNames.Add("드레인피트 : 기본");
        repo.ParameterNamesByFamily["드레인피트 : 기본"] = ["가로", "세로", "깊이", "주석"];

        var vm = CreateViewModel(repo);

        Assert.Equal("가로", vm.SelectedDrainPitLengthParam);
        Assert.Equal("세로", vm.SelectedDrainPitWidthParam);
        Assert.Equal("깊이", vm.SelectedDrainPitDepthParam);
    }

    [Fact]
    public void ReloadTypeNames_추정할_패밀리가_없으면_선택안함이다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);

        repo.GenericModelTypeNames.Clear();
        vm.ReloadTypeNames();

        Assert.Equal(NotSelected, vm.SelectedDrainPitVoidFamily);
        Assert.Equal(NotSelected, vm.SelectedDrainPitLengthParam);
        Assert.Equal([NotSelected], vm.DrainPitVoidParameterNames);
    }

    [Fact]
    public void ReloadTypeNames_사용자가_고른_매개변수는_유지한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        vm.SelectedDrainPitLengthParam = "주석";

        vm.ReloadTypeNames();

        Assert.Equal("주석", vm.SelectedDrainPitLengthParam);
        // 중복 배정 방지가 그대로 돌아 나머지는 자동 추정값이다.
        Assert.Equal("폭", vm.SelectedDrainPitWidthParam);
        Assert.Equal("깊이", vm.SelectedDrainPitDepthParam);
    }

    [Fact]
    public void ReloadTypeNames_패밀리의_매개변수_구성이_바뀌면_따라간다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        Assert.Equal("길이", vm.SelectedDrainPitLengthParam);

        // 같은 이름 패밀리를 편집해 재로드한 상황. 캐시를 비우지 않으면 낡은 이름이 남는다.
        repo.ParameterNamesByFamily[VoidFamily] = ["Length", "Width", "Depth"];
        vm.ReloadTypeNames();

        Assert.DoesNotContain("길이", vm.DrainPitVoidParameterNames);
        Assert.Equal("Length", vm.SelectedDrainPitLengthParam);
        Assert.Equal("Width", vm.SelectedDrainPitWidthParam);
        Assert.Equal("Depth", vm.SelectedDrainPitDepthParam);
    }

    [Fact]
    public void ReloadTypeNames_캐시무효화를_한번_호출한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        Assert.Equal(0, repo.InvalidateCallCount);

        vm.ReloadTypeNames();

        Assert.Equal(1, repo.InvalidateCallCount);
    }

    [Fact]
    public void 매개변수_선택에_null이_들어오면_선택안함으로_되돌리고_고정하지_않는다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);

        // ItemsSource를 비울 때 ComboBox가 역기록하는 null을 흉내낸다.
        vm.SelectedDrainPitLengthParam = null!;
        Assert.Equal(NotSelected, vm.SelectedDrainPitLengthParam);

        vm.ReloadTypeNames();

        // 고정되지 않았으므로 자동 추정값으로 되돌아온다.
        Assert.Equal("길이", vm.SelectedDrainPitLengthParam);
    }

    [Fact]
    public void ReloadTypeNames_기둥_보_헌치_선택도_보존하고_사라지면_기본값을_재추정한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        vm.SelectedColumnTypeName = "기둥류 : 기둥 B";
        vm.SelectedBeamTypeName = "일반보 : 보 B";
        vm.SelectedHaunchTypeName = "헌치류 : H1";

        vm.ReloadTypeNames();

        Assert.Equal("기둥류 : 기둥 B", vm.SelectedColumnTypeName);
        Assert.Equal("일반보 : 보 B", vm.SelectedBeamTypeName);
        Assert.Equal("헌치류 : H1", vm.SelectedHaunchTypeName);

        // 고른 값이 사라지면 pin이 풀리고 기본값(콘크리트 첫 항목 / DH_헌치)으로 재추정된다(docs/156 § F-3).
        repo.ColumnFamilyTypeNames.Remove("기둥류 : 기둥 B"); repo.ConcreteColumnTypeNames.Remove("기둥 B");
        repo.StructuralFramingFamilyTypeNames.Remove("일반보 : 보 B"); repo.ConcreteBeamTypeNames.Remove("보 B");
        repo.StructuralFramingFamilyTypeNames.Remove("헌치류 : H1");
        vm.ReloadTypeNames();

        Assert.Equal("기둥류 : 기둥 A", vm.SelectedColumnTypeName);
        Assert.Equal("일반보 : 보 A", vm.SelectedBeamTypeName);
        Assert.Equal("DH_헌치 : 헌치 300", vm.SelectedHaunchTypeName);
    }

    [Fact]
    public void 생성자에서_기둥과_보는_콘크리트_첫_항목을_기본값으로_잡는다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.Equal("기둥류 : 기둥 A", vm.SelectedColumnTypeName);
        Assert.Equal("일반보 : 보 A", vm.SelectedBeamTypeName);
        // "(사용 안 함)"은 목록에 그대로 남는다.
        Assert.Contains(NotUsed, vm.ColumnTypeNames);
        Assert.Contains(NotUsed, vm.BeamTypeNames);
    }

    [Fact]
    public void 콘크리트_유형이_없으면_사용안함을_유지한다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.ConcreteColumnTypeNames.Clear();
        repo.ConcreteBeamTypeNames.Clear();

        var vm = CreateViewModel(repo);

        Assert.Equal(NotUsed, vm.SelectedColumnTypeName);
        Assert.Equal(NotUsed, vm.SelectedBeamTypeName);
    }

    [Fact]
    public void 헌치_목록은_패밀리명_타입명_표기다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.Equal(HaunchTypeAuto, vm.HaunchTypeNames[0]);
        Assert.Contains("DH_헌치 : 헌치 300", vm.HaunchTypeNames);
        // 보와 헌치는 같은 구조 프레임 목록을 공용한다. 앞의 sentinel만 서로 다르다(docs/159 § 2).
        Assert.Equal(vm.BeamTypeNames.Skip(1), vm.HaunchTypeNames.Skip(1));
    }

    [Fact]
    public void 기둥_보_목록은_패밀리명_타입명_표기다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.All(vm.ColumnTypeNames.Skip(1), name => Assert.Contains(" : ", name));
        Assert.All(vm.BeamTypeNames.Skip(1), name => Assert.Contains(" : ", name));
    }

    [Fact]
    public void 보_목록은_재료로_거르지_않는다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        // 이 항목이 사라지면 재료 필터가 되살아난 것이다(docs/159 § 0-2).
        Assert.Contains("강재보류 : 강재보 A", vm.BeamTypeNames);
    }

    [Fact]
    public void 헌치_목록도_재료로_거르지_않는다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.Contains("강재보류 : 강재보 A", vm.HaunchTypeNames);
        // 목록이 넓어져도 기본값은 DH_헌치 패밀리다.
        Assert.Equal("DH_헌치 : 헌치 300", vm.SelectedHaunchTypeName);
    }

    [Fact]
    public void 헌치_기본값은_DH_헌치_패밀리의_첫_유형이다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.Equal("DH_헌치 : 헌치 300", vm.SelectedHaunchTypeName);
    }

    [Fact]
    public void DH_헌치가_없으면_패밀리명에_헌치가_든_항목을_고른다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.StructuralFramingFamilyTypeNames.RemoveAll(name => name.StartsWith("DH_헌치", StringComparison.Ordinal));

        var vm = CreateViewModel(repo);

        Assert.Equal("헌치류 : H1", vm.SelectedHaunchTypeName);
    }

    [Fact]
    public void 헌치_후보가_없으면_자동탐색을_유지한다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.StructuralFramingFamilyTypeNames.RemoveAll(name => name.Contains("헌치", StringComparison.Ordinal));

        var vm = CreateViewModel(repo);

        Assert.Equal(HaunchTypeAuto, vm.SelectedHaunchTypeName);
    }

    /// <summary>헌치를 켰는데 자동 탐색 후보가 없으면 생성 요청을 보내지 않는다 — 종전에는 Revit에서 VR-201로 전부 롤백됐다(docs/238 § A).</summary>
    [Fact]
    public void 헌치_후보가_없으면_생성_요청을_보내지_않는다()
    {
        var repo = new StubElementTypeQueryRepo();
        repo.StructuralFramingFamilyTypeNames.RemoveAll(name => name.Contains("헌치", StringComparison.Ordinal));
        var vm = CreateViewModel(repo);
        vm.HasDrainPit = false;
        var requested = false;
        vm.CreateModelAction = _ => requested = true;

        vm.CreateCommand.Execute(null);
        Assert.False(requested);

        vm.HasUpperHaunch = false;
        vm.HasLowerHaunch = false;
        vm.CreateCommand.Execute(null);
        Assert.True(requested);
    }

    /// <summary>이토에서 켠 중간슬래브가 다른 유형으로 넘어가지 않는다(docs/238 § D).</summary>
    [Fact]
    public void 유형을_바꾸면_중간슬래브가_꺼진다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.HasIntermediateSlab = true;

        vm.SelectedValveRoomType = "제수밸브실";

        Assert.False(vm.HasIntermediateSlab);
        Assert.False(vm.HasIntermediateWall);
    }

    /// <summary>숫자 칸 변환 오류가 남아 있으면 [생성]을 막는다(docs/238 § B).</summary>
    [Fact]
    public void 입력_오류가_있으면_생성_버튼이_비활성이다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        Assert.True(vm.CreateCommand.CanExecute(null));

        vm.UpdateInputValidation(errorAdded: true);
        Assert.False(vm.CreateCommand.CanExecute(null));

        vm.UpdateInputValidation(errorAdded: false);
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void ReloadTypeNames_사용자가_고르지_않았으면_기본값을_다시_추정한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        Assert.Equal("일반보 : 보 A", vm.SelectedBeamTypeName);

        // 창을 띄운 채 콘크리트 보 패밀리를 새로 로드한 상황.
        repo.StructuralFramingFamilyTypeNames.Insert(0, "일반보 : 보 0");
        repo.ConcreteBeamTypeNames.Insert(0, "보 0");
        vm.ReloadTypeNames();

        Assert.Equal("일반보 : 보 0", vm.SelectedBeamTypeName);
    }

    [Fact]
    public void ReloadTypeNames_사용자가_고른_사용안함은_유지한다()
    {
        var repo = new StubElementTypeQueryRepo();
        var vm = CreateViewModel(repo);
        // "(사용 안 함)"은 제수밸브실이 아닐 때의 유효한 최종 선택이라 pin된다(docs/156 § F-2).
        vm.SelectedColumnTypeName = NotUsed;
        vm.SelectedBeamTypeName = NotUsed;
        vm.SelectedHaunchTypeName = HaunchTypeAuto;

        vm.ReloadTypeNames();

        Assert.Equal(NotUsed, vm.SelectedColumnTypeName);
        Assert.Equal(NotUsed, vm.SelectedBeamTypeName);
        Assert.Equal(HaunchTypeAuto, vm.SelectedHaunchTypeName);
    }

    [Fact]
    public void 프로젝트정보에_남은_값을_창을_열_때_복원한다()
    {
        var projectInfo = new StubProjectInformationQueryRepo
        {
            Values = { ["밸브실유형"] = "제수밸브실", ["관로구간"] = "도수1-1", ["밸브실측점"] = "1+750.00" }
        };

        var vm = CreateViewModel(new StubElementTypeQueryRepo(), projectInfo);

        Assert.Equal("제수밸브실", vm.SelectedValveRoomType);
        Assert.Equal("도수1-1", vm.PipelineSection);
        Assert.Equal("1+750.00", vm.StationPoint);
    }

    [Fact]
    public void 프로젝트정보의_밸브실유형이_알_수_없는_값이면_기본값을_유지한다()
    {
        var projectInfo = new StubProjectInformationQueryRepo { Values = { ["밸브실유형"] = "감압밸브실" } };

        var vm = CreateViewModel(new StubElementTypeQueryRepo(), projectInfo);

        Assert.Equal("이토밸브실", vm.SelectedValveRoomType);
        Assert.Equal(string.Empty, vm.PipelineSection);
    }

    [Fact]
    public void 밸브실목록_선택하면_제원표_LW와_좌표_도북각_EL_내부높이를_채운다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 0);

        vm.AutoPlaceCommand.Execute(null);

        // 이토 DN300 → L1500·W1500. 측점 50 → 중심 (50, 0). 표고 10.0 = 관중심 → 관저 9.85 − 0.6 = 9.25, 원지반 15.0 − 0.25 = 14.75, 상부슬래브 400.
        Assert.Equal("이토밸브실", vm.SelectedValveRoomType);
        Assert.Equal("0+050", vm.StationPoint);
        Assert.Equal(1500d, vm.InnerLength);
        Assert.Equal(1500d, vm.InnerWidth);
        Assert.Equal(49.25d, vm.SharedCoordinateX, 4);
        Assert.Equal(-0.75d, vm.SharedCoordinateY, 4);
        Assert.Equal(0d, vm.TrueNorthToProjectNorthClockwiseDegrees, 6);
        Assert.Equal(9.25d, vm.FoundationTopEl, 6);
        Assert.Equal(5100d, vm.InnerHeight);
        Assert.Equal("a.xlsx · 선형", vm.PlacementSourceText);
        Assert.Empty(dialog.Warnings);
    }

    [Fact]
    public void 자동지정_뒤_LW를_바꾸면_중심을_유지하도록_좌하단을_다시_계산한다()
    {
        var vm = CreatePlacementViewModel(new StubDialogService(), rowIndex: 0);
        vm.AutoPlaceCommand.Execute(null);

        // 중심 (50, 0). L 10000 → X 45, W 3000 → Y −1.5.
        vm.InnerLength = 10000;
        vm.InnerWidth = 3000;

        Assert.Equal(45d, vm.SharedCoordinateX, 4);
        Assert.Equal(-1.5d, vm.SharedCoordinateY, 4);
    }

    [Fact]
    public void 좌표를_손으로_고친_뒤엔_LW를_바꿔도_좌표를_건드리지_않는다()
    {
        var vm = CreatePlacementViewModel(new StubDialogService(), rowIndex: 0);
        vm.AutoPlaceCommand.Execute(null);

        vm.SharedCoordinateX = 10;
        vm.InnerLength = 10000;

        Assert.Equal(10d, vm.SharedCoordinateX, 4);
        Assert.Equal(-0.75d, vm.SharedCoordinateY, 4);
    }

    [Fact]
    public void 제수_A형에_없는_관경이면_B형_제원을_쓴다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 1);
        // 기본 형식이 B형으로 바뀜(docs/311). A형 → B형 대체를 보려면 A형을 먼저 고른다.
        vm.SelectedSluiceVariant = "A형";

        vm.AutoPlaceCommand.Execute(null);

        Assert.Equal("제수밸브실", vm.SelectedValveRoomType);
        Assert.Equal("A형", vm.SelectedSluiceVariant);
        Assert.Equal(4100d, vm.InnerLength);
        Assert.Equal(2300d, vm.InnerWidth);
        Assert.Contains(dialog.Infos, x => x.Contains("B형을 적용"));
    }

    [Fact]
    public void 타이밸브실은_제원표가_없어_LW를_유지하고_경고한다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 2);

        vm.AutoPlaceCommand.Execute(null);

        Assert.Equal("타이밸브실", vm.SelectedValveRoomType);
        Assert.Equal(4000d, vm.InnerLength);
        Assert.Equal(2500d, vm.InnerWidth);
        Assert.Equal(9.25d, vm.FoundationTopEl, 6);
        Assert.Contains(dialog.Warnings, x => x.Contains("제원표에 타이밸브실 DN300 없음"));
    }

    [Fact]
    public void 공기밸브실은_관중심_위_깊이로_기초상면을_정한다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 3);

        vm.AutoPlaceCommand.Execute(null);

        // 표고 10.0 = 관중심, Hp = 200 + 300/2 = 350(docs/311) → 10.35. (14.75 − 10.35) × 1000 − 400 = 4000.
        Assert.Equal(10.35d, vm.FoundationTopEl, 6);
        Assert.Equal(4000d, vm.InnerHeight, 6);
        Assert.Equal(300d, vm.MainPipeDiameter);
        Assert.Equal(800d, vm.FoundationThickness);
        Assert.Equal(350d, vm.FoundationTopToPipeCenterDepth);
    }

    [Fact]
    public void 창을_열면_이토_DN1200_제원표_치수가_들어간다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        Assert.Equal("이토밸브실", vm.SelectedValveRoomType);
        Assert.Equal(3000d, vm.InnerLength);
        Assert.Equal(2500d, vm.InnerWidth);
        Assert.Equal(4500d, vm.InnerHeight);
        // 중간벽 정중앙: (L 3000 − Tw 300)/2.
        Assert.Equal(1350d, vm.IntermediateWallOffset);
        Assert.Equal(1200d, vm.PlacementPipeDiameter);
    }

    [Fact]
    public void 중간벽_Lw는_L_Tw_따라가고_손으로_고치면_유지한다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        vm.InnerLength = 5000;
        Assert.Equal(2350d, vm.IntermediateWallOffset);
        vm.IntermediateWallThickness = 500;
        Assert.Equal(2250d, vm.IntermediateWallOffset);

        vm.IntermediateWallOffset = 1000;
        vm.InnerLength = 6000;
        Assert.Equal(1000d, vm.IntermediateWallOffset);

        // [기본값]이면 손 수정 해제. Tw는 프리셋이 안 바꿈 → (3000 − 500)/2.
        vm.ResetCommand.Execute(null);
        Assert.Equal(1250d, vm.IntermediateWallOffset);
    }

    [Fact]
    public void 중간슬래브_H1_H2는_내부높이_Ts로_반씩_나누고_한쪽을_고치면_나머지가_흡수한다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.HasIntermediateSlab = true;

        // (4500 − 300)/2.
        Assert.Equal(2100d, vm.Floor1InnerHeight);
        Assert.Equal(2100d, vm.Floor2InnerHeight);
        vm.IntermediateSlabThickness = 500;
        Assert.Equal(2000d, vm.Floor1InnerHeight);

        vm.Floor1InnerHeight = 1500;
        Assert.Equal(2500d, vm.Floor2InnerHeight);
        vm.InnerHeight = 5000;
        Assert.Equal(1500d, vm.Floor1InnerHeight);
        Assert.Equal(3000d, vm.Floor2InnerHeight);
    }

    [Fact]
    public void 제수_보_Ox_Sx_Oy_Sy는_개수로_균등분배하고_손으로_고친_값은_유지한다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.SelectedValveRoomType = "제수밸브실";
        vm.InnerWidth = 6000;
        vm.InnerLength = 8000;

        vm.BeamCountX = 2;
        vm.BeamCountY = 3;
        Assert.Equal(2000d, vm.BeamOffsetX);
        Assert.Equal(2000d, vm.BeamSpacingX);
        Assert.Equal(2000d, vm.BeamOffsetY);
        Assert.Equal(2000d, vm.BeamSpacingY);

        // Ox만 고치면 Sx = 남은 구간 균등 (6000 − 2×1000)/1.
        vm.BeamOffsetX = 1000;
        Assert.Equal(4000d, vm.BeamSpacingX);
        // Sy만 고치면 Oy = 양쪽 여유 같게 (8000 − 1500×2)/2.
        vm.BeamSpacingY = 1500;
        Assert.Equal(2500d, vm.BeamOffsetY);

        vm.InnerWidth = 8000;
        Assert.Equal(1000d, vm.BeamOffsetX);
        Assert.Equal(6000d, vm.BeamSpacingX);
    }

    [Fact]
    public void 제수로_바꾸면_DN1650_B형_제원표_치수가_들어간다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        vm.SelectedValveRoomType = "제수밸브실";

        Assert.Equal(7000d, vm.InnerLength);
        Assert.Equal(5000d, vm.InnerWidth);
        Assert.Equal(4000d, vm.InnerHeight);
        Assert.Equal("B형", vm.SelectedSluiceVariant);
        Assert.Equal(1650d, vm.PlacementPipeDiameter);
    }

    [Fact]
    public void 공기로_바꾸면_DN1650_제원표_치수와_기초두께_Hp가_들어간다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        vm.SelectedValveRoomType = "공기밸브실";

        Assert.Equal(2000d, vm.InnerLength);
        Assert.Equal(2200d, vm.InnerWidth);
        Assert.Equal(1900d, vm.InnerHeight);
        Assert.Equal(1650d, vm.MainPipeDiameter);
        Assert.Equal(2150d, vm.FoundationThickness);
        Assert.Equal(1025d, vm.FoundationTopToPipeCenterDepth);
    }

    [Fact]
    public void 공기_본관직경을_바꾸면_기초두께와_Hp가_따라간다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.SelectedValveRoomType = "공기밸브실";

        vm.MainPipeDiameter = 800;

        Assert.Equal(1300d, vm.FoundationThickness);
        Assert.Equal(600d, vm.FoundationTopToPipeCenterDepth);
    }

    [Fact]
    public void 제원표_값은_사용자가_고칠_수_있다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.SelectedValveRoomType = "공기밸브실";

        vm.InnerLength = 2500;
        vm.FoundationThickness = 2000;

        Assert.Equal(2500d, vm.InnerLength);
        Assert.Equal(2000d, vm.FoundationThickness);
    }

    [Fact]
    public void 공기에서_이토로_바꾸면_기초두께가_500으로_돌아간다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());
        vm.SelectedValveRoomType = "공기밸브실";

        vm.SelectedValveRoomType = "이토밸브실";

        Assert.Equal(500d, vm.FoundationThickness);
    }

    [Fact]
    public void 타이는_제원표가_없어_기존_프리셋을_쓴다()
    {
        var vm = CreateViewModel(new StubElementTypeQueryRepo());

        vm.SelectedValveRoomType = "타이밸브실";

        Assert.Equal(4000d, vm.InnerLength);
        Assert.Equal(2500d, vm.InnerWidth);
        Assert.Equal(2500d, vm.InnerHeight);
    }

    [Fact]
    public void 엑셀_행을_고른_뒤엔_유형을_바꿔도_행_관경을_유지한다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 0);
        vm.AutoPlaceCommand.Execute(null);

        vm.SelectedValveRoomType = "제수밸브실";
        vm.ResetCommand.Execute(null);

        Assert.Equal(300d, vm.PlacementPipeDiameter);
    }

    [Fact]
    public void 관저고_기초상면_거리를_바꾸면_기초상면에_반영한다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 0);
        vm.InvertToFoundationTopMm = 800;

        vm.AutoPlaceCommand.Execute(null);

        // 관저 9.85 − 0.8 = 9.05.
        Assert.Equal(9.05d, vm.FoundationTopEl, 6);
        Assert.Contains("800", vm.PlacementRuleText);
    }

    [Fact]
    public void 고른_행과_측점이_다르면_경고하고_칸을_바꾸지_않는다()
    {
        var dialog = new StubDialogService();
        var vm = CreatePlacementViewModel(dialog, rowIndex: 0);
        vm.AutoPlaceCommand.Execute(null);
        var foundationTop = vm.FoundationTopEl;
        vm.FoundationTopEl = 0;

        vm.StationPoint = "0+060";
        vm.AutoPlaceCommand.Execute(null);

        Assert.Equal(9.25d, foundationTop, 6);
        Assert.Equal(0d, vm.FoundationTopEl);
        Assert.Contains(dialog.Warnings, x => x.Contains("측점이 엑셀에서 고른 행(0+050)과 다릅니다"));
    }

    [Fact]
    public void 위치자동지정_실패하면_칸을_바꾸지_않는다()
    {
        var dialog = new StubDialogService();
        // 5번째 행 측점 0+500은 선형(0~100) 밖.
        var vm = CreatePlacementViewModel(dialog, rowIndex: 4);
        var innerHeight = vm.InnerHeight;
        var innerLength = vm.InnerLength;

        vm.AutoPlaceCommand.Execute(null);

        Assert.Equal(0d, vm.SharedCoordinateX);
        Assert.Equal(0d, vm.FoundationTopEl);
        Assert.Equal(innerHeight, vm.InnerHeight);
        Assert.Equal(innerLength, vm.InnerLength);
        Assert.Contains(dialog.Warnings, x => x.Contains("VR-502"));
    }

    [Fact]
    public void 매핑창_취소하면_아무_칸도_바꾸지_않는다()
    {
        var dialog = new StubDialogService();
        var reader = new StubProfileReader();
        var vm = new ValveRoomViewModel(dialog, new StubElementTypeQueryRepo(), new StubProjectInformationQueryRepo(),
            excelReader: reader, fileDialog: new StubFileDialogService(), resolvePlacement: new ResolveValveRoomPlacementUseCase(reader));
        ExcelAlignmentMappingViewModel? shown = null;
        vm.ShowExcelMappingDialog = mapping => { mapping.Sheets[0].IsChecked = true; shown = mapping; return false; };
        vm.StationPoint = "24+5.50";

        vm.AutoPlaceCommand.Execute(null);

        // 시트를 고르면 매핑창 하단 목록이 열 추정만으로 채워진다.
        Assert.Equal(5, shown!.ValveRoomRows.Count);
        Assert.Equal("24+5.50", vm.StationPoint);
        Assert.Equal(0d, vm.SharedCoordinateX);
        Assert.Empty(dialog.Warnings);
    }

    [Fact]
    public void 문서에_저장된_표가_있으면_밸브실_창_기본값이_그_표를_쓴다()
    {
        var repo = new StubCatalogRepo
        {
            Stored = new DHBIMWATER.Core.Structures.ValveRoomDimensionTable([new DHBIMWATER.Core.Structures.ValveRoomStandardDimension("이토밸브실", null, 4, 1200, 3300, 2600, 4700, "")]),
        };

        var vm = new ValveRoomViewModel(new StubDialogService(), new StubElementTypeQueryRepo(), new StubProjectInformationQueryRepo(),
            dimensionCatalogRepo: repo);

        Assert.Equal(3300d, vm.InnerLength);
        Assert.Equal(2600d, vm.InnerWidth);
        Assert.Equal(4700d, vm.InnerHeight);
    }

    [Fact]
    public void 제원표_창에서_저장하면_다음_기본값부터_새_표를_쓴다()
    {
        var repo = new StubCatalogRepo();
        var vm = new ValveRoomViewModel(new StubDialogService(), new StubElementTypeQueryRepo(), new StubProjectInformationQueryRepo(),
            dimensionCatalogRepo: repo);
        vm.DimensionCatalogLauncher.ShowDialog = dialog =>
        {
            dialog.VisibleRows.Single(x => x.DiameterMm == 1200).LengthMm = 3300;
            return true;
        };

        vm.DimensionCatalogLauncher.OpenCommand.Execute(null);
        // 저장만으로는 입력칸을 안 바꾼다.
        Assert.Equal(3000d, vm.InnerLength);

        vm.ResetCommand.Execute(null);
        Assert.Equal(3300d, vm.InnerLength);
    }

    /// <summary>문서 제원표 저장소 스텁(docs/312).</summary>
    private sealed class StubCatalogRepo : IValveRoomDimensionCatalogRepo
    {
        public DHBIMWATER.Core.Structures.ValveRoomDimensionTable? Stored { get; set; }
        public DHBIMWATER.Core.Structures.ValveRoomDimensionTable? Load() => Stored;
        public void Save(DHBIMWATER.Core.Structures.ValveRoomDimensionTable table) => Stored = table;
    }

    private static ValveRoomViewModel CreatePlacementViewModel(StubDialogService dialog, int rowIndex = 0)
    {
        var reader = new StubProfileReader();
        var vm = new ValveRoomViewModel(dialog, new StubElementTypeQueryRepo(), new StubProjectInformationQueryRepo(),
            excelReader: reader, fileDialog: new StubFileDialogService(), resolvePlacement: new ResolveValveRoomPlacementUseCase(reader));
        // 매핑창 대신 열 자동 추정 결과로 확정한다.
        vm.ShowExcelMappingDialog = mapping =>
        {
            mapping.Sheets[0].IsChecked = true;
            mapping.SelectedValveRoomRow = mapping.ValveRoomRows[rowIndex];
            mapping.OkCommand.Execute(null);
            return mapping.Result is { Count: > 0 };
        };
        return vm;
    }

    private sealed class StubProfileReader : IExcelAlignmentSourceReader
    {
        public bool CanRead(string filePath) => true;
        public IReadOnlyList<string> GetSheetNames(string filePath) => ["선형"];
        public IReadOnlyList<IReadOnlyList<string?>> PreviewRows(string filePath, string sheetName, int maxRows) =>
        [
            ["측점", "X", "Y", "Z", "호칭지름(DN)", "밸브실유형", "원지반"],
            ["0+000", "0", "0", "10", "DN300", "", ""],
            ["0+050", "50", "0", "10", "DN300", "이토밸브실", "15"]
        ];
        public ShapefileReadResult Read(string filePath) => throw new NotSupportedException();
        public ShapefileReadResult Read(string filePath, ExcelAlignmentMapping mapping) => throw new NotSupportedException();
        public AlignmentProfileReadResult ReadProfile(string filePath, ExcelAlignmentMapping mapping)
        {
            Assert.Equal(0, mapping.StationColumnIndex);
            Assert.Equal((4, 5, 6), (mapping.VertexDiameterColumnIndex, mapping.ValveRoomTypeColumnIndex, mapping.GroundElColumnIndex));
            return new([new AlignmentProfileRow(0, 0, 0, 10), new AlignmentProfileRow(100, 100, 0, 11)], []);
        }
        public ValveRoomListReadResult ReadValveRoomList(string filePath, ExcelAlignmentMapping mapping, int? cumulativeDistanceColumn = null) => new("선형",
        [
            new ValveRoomListRow(2, "이토밸브실", "0+050", 300, 10.0, 15.0),
            new ValveRoomListRow(3, "제수밸브실", "0+050", 600, 10.0, 15.0),
            new ValveRoomListRow(4, "타이밸브실", "0+050", 300, 10.0, 15.0),
            new ValveRoomListRow(5, "공기밸브실", "0+050", 300, 10.0, 15.0),
            new ValveRoomListRow(6, "이토밸브실", "0+500", 300, 10.0, 15.0),
        ], []);
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter) => "a.xlsx";
        public string? SaveFile(string title, string filter, string defaultFileName = "") => null;
    }

    private static ValveRoomViewModel CreateViewModel(StubElementTypeQueryRepo repo,
        IProjectInformationQueryRepo? projectInfo = null)
        => new(new StubDialogService(), repo, projectInfo ?? new StubProjectInformationQueryRepo());

    /// <summary>프로젝트 정보에 이미 기록된 값을 흉내내는 스텁. 값이 없으면 빈 문자열(= Revit에서 매개변수가 없는 상태).</summary>
    private sealed class StubProjectInformationQueryRepo : IProjectInformationQueryRepo
    {
        public Dictionary<string, string> Values { get; } = new();
        public string GetTextParameter(string parameterName)
            => Values.TryGetValue(parameterName, out var value) ? value : string.Empty;
    }

    /// <summary>목록을 테스트 중에 바꿀 수 있는 스텁. 패밀리 재로드·편집 상황을 흉내낸다.</summary>
    private sealed class StubElementTypeQueryRepo : IElementTypeQueryRepo
    {
        // 콘크리트 항목이 목록 첫 항목이 아니도록 잡는다 — "첫 항목 폴백"과 "콘크리트 첫 항목"을 구분하기 위해서다.
        // 기둥·보 목록은 "패밀리명 : 타입명"이고, 콘크리트 후보 목록은 유형명 단독이다(docs/159 § B-2).
        public List<string> ColumnFamilyTypeNames { get; } = ["강재기둥류 : 강재기둥 A", "기둥류 : 기둥 A", "기둥류 : 기둥 B"];
        public List<string> ConcreteColumnTypeNames { get; } = ["기둥 A", "기둥 B"];
        public List<string> ConcreteBeamTypeNames { get; } = ["보 A", "보 B"];
        // 보·헌치가 공용하는 목록. 재료 필터가 없으므로 강재보도 들어 있다(docs/159 § A-2).
        public List<string> StructuralFramingFamilyTypeNames { get; } =
            ["DH_헌치 : 헌치 300", "DH_헌치 : 헌치 400", "강재보류 : 강재보 A", "일반보 : 보 A", "일반보 : 보 B", "헌치류 : H1"];
        // 아래 둘은 인터페이스 구현상 필요할 뿐, 밸브실 ViewModel은 더 이상 쓰지 않는다(배수지 창 전용, docs/159 § 1).
        public List<string> ColumnTypeNames { get; } = ["강재기둥 A", "기둥 A", "기둥 B"];
        public List<string> BeamTypeNames { get; } = ["강재보 A", "보 A", "보 B"];
        public List<string> GenericModelTypeNames { get; } = [VoidFamily, SolidFamily];
        public Dictionary<string, List<string>> ParameterNamesByFamily { get; } = new()
        {
            [VoidFamily] = ["길이", "폭", "깊이", "주석"],
            [SolidFamily] = ["Length", "Width", "Depth"],
        };
        public int InvalidateCallCount { get; private set; }

        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName) => GetPipeAccessoryInstanceParameterNames(familyTypeName);

        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName) => [];

        public void InvalidateFamilyCaches() => InvalidateCallCount++;
        public IEnumerable<string> GetColumnTypeNames() => ColumnTypeNames;
        public IEnumerable<string> GetColumnFamilyTypeNames() => ColumnFamilyTypeNames;
        public IEnumerable<string> GetBeamTypeNames() => BeamTypeNames;
        public IEnumerable<string> GetConcreteColumnTypeNames() => ConcreteColumnTypeNames;
        public IEnumerable<string> GetConcreteBeamTypeNames() => ConcreteBeamTypeNames;
        public IEnumerable<string> GetStructuralFramingFamilyTypeNames() => StructuralFramingFamilyTypeNames;
        public IEnumerable<string> GetGenericModelTypeNames() => GenericModelTypeNames;
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => ParameterNamesByFamily.TryGetValue(familyTypeName, out var names) ? names : [];

        public IEnumerable<string> GetSlabTypeNames() => [];
        public IEnumerable<string> GetWallTypeNames() => [];
        public IEnumerable<string> GetAdaptiveComponentTypeNames() => [];
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName) => [];
        public int GetAdaptiveBendPointCount(string familyTypeName) => -1;
        public int? GetFamilyTypeCategoryId(string familyTypeName) => null;
        public IEnumerable<string> GetPipingSystemTypeNames() => [];
        public IEnumerable<string> GetPipeTypeNames() => [];
        public IEnumerable<string> GetLevelNames() => [];
        public IEnumerable<string> GetPipeAccessoryTypeNames() => [];
        public IEnumerable<string> GetPipeFittingTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => 400;
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) => (400, 400);
    }

    private sealed class StubDialogService : IDialogService
    {
        public List<string> Warnings { get; } = new();
        public List<string> Infos { get; } = new();
        public void Info(string title, string message) => Infos.Add(message);
        public void Warn(string title, string message) => Warnings.Add(message);
        public bool Confirm(string title, string message) => true;
    }
}
