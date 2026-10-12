using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Infrastructure.Repositories.DB;
using DHBIMWATER.Infrastructure.Repositories.Gis;
using DHBIMWATER.Infrastructure.Repositories.Mock;
using DHBIMWATER.Infrastructure.Repositories.Revit.Gis;
using DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;
using DHBIMWATER.Infrastructure.Repositories.Revit.Piping;
using DHBIMWATER.Infrastructure.Repositories.Revit;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Infrastructure.Services.Common;
using DHBIMWATER.Infrastructure.Services.Didas;
using DHBIMWATER.Infrastructure.Services.Mock;
using DHBIMWATER.Infrastructure.Services.Revit;
using DHBIMWATER.Infrastructure.Services.Revit.Parameter;
using DHBIMWATER.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using DHBIMWATER.Infrastructure.Repositories.Local;

namespace DHBIMWATER.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Infrastructure 서비스 등록 (Revit 환경용)
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Revit 실제 구현 등록
        services.AddSingleton<IGenericModelRepository, RevitGenericModelRepository>();
        services.AddTransient<ITransactionContext, RevitTransactionContext>();

        #region Element 관련
        services.AddTransient<ILevelQueryRepo, RevitLevelQueryRepo>();
        services.AddTransient<ILevelCommandRepo, RevitLevelCommandRepo>();
        services.AddTransient<IElementTypeQueryRepo, RevitElementTypeQueryRepo>();
        services.AddTransient<IElementTypeCommandRepo, RevitElementTypeCommandRepo>();
        services.AddTransient<IWallCommandRepo, RevitWallCommandRepo>();
        services.AddTransient<IGenericModelCommandRepo, RevitGenericModelCommandRepo>();
        services.AddTransient<IPipeCommandRepo, RevitPipeMepCommandRepo>();
        services.AddTransient<IPipeCommandRepo, RevitPipeGenericModelCommandRepo>();
        services.AddTransient<IPipeCommandRepo, RevitPipeSegmentCommandRepo>();
        services.AddTransient<IPipeMaterialScheduleRepo, RevitPipeMaterialScheduleRepo>();
        services.AddTransient<IPipeMaterialDraftingRepo, RevitPipeMaterialDraftingRepo>();
        services.AddTransient<IJunctionDetailRepo, RevitJunctionDetailRepo>();
        services.AddTransient<IJunctionSymbolShapeRepo, RevitJunctionSymbolShapeRepo>();
        services.AddTransient<IPipeMaterialBackfillRepo, RevitPipeMaterialBackfillRepo>();
        services.AddTransient<IPipeJointConnectionRepo, RevitPipeJointConnectionRepo>();   // docs/266 § 1 연결 패스
        services.AddTransient<IBeamCommandRepo, RevitBeamCommandRepo>();
        services.AddTransient<IColumnCommandRepo, RevitColumnCommandRepo>();
        services.AddTransient<ISlabCommandRepo, RevitSlabCommandRepo>();
        services.AddTransient<IFoundationCommandRepo, RevitFoundationCommandRepo>();
        services.AddTransient<IAirValveVoidCommandRepo, RevitAirValveVoidCommandRepo>();
        services.AddTransient<IDrainPitVoidCommandRepo, RevitDrainPitVoidCommandRepo>();
        services.AddTransient<IElementParameterCommandRepo, RevitElementParameterCommandRepo>();
        services.AddTransient<IProjectInformationQueryRepo, RevitProjectInformationQueryRepo>();
        services.AddTransient<IProjectInformationCommandRepo, RevitProjectInformationCommandRepo>();
        services.AddTransient<IOpeningCommandRepo, RevitOpeningCommandRepo>();
        services.AddTransient<IStairCommandRepo, RevitStairCommandRepo>();
        services.AddTransient<IDirectShapeCommandRepo, RevitDirectShapeCommandRepo>();
        services.AddTransient<IViewCommandRepo, RevitViewCommandRepo>();
        services.AddTransient<ISetParameterRepo, RevitSetParameterRepo>();
        services.AddTransient<ISharedParameterRepository, RevitSharedParameterRepository>();
        services.AddTransient<IPipeAlignmentCommandRepo, RevitPipeAlignmentCommandRepo>();
        services.AddTransient<IPipeAlignmentQueryRepo, RevitPipeAlignmentQueryRepo>();
        services.AddTransient<IShapefileReader, ShapefileReader>();
        services.AddTransient<IAlignmentSourceReader, ShapefileReader>();
        services.AddTransient<IAlignmentSourceReader, DxfAlignmentReader>();
        services.AddTransient<IAlignmentSourceReader, DwgAlignmentReader>();
        services.AddTransient<IAlignmentSourceReader, ExcelAlignmentReader>();
        services.AddTransient<IExcelAlignmentSourceReader, ExcelAlignmentReader>();
        services.AddTransient<IAlignmentStraightPlacementRepo, RevitAlignmentStraightPlacementRepo>();
        services.AddTransient<IAlignmentStraightDirectShapeRepo, RevitAlignmentStraightDirectShapeRepo>();
        services.AddTransient<IAlignmentPipePlacementRepo, RevitAlignmentPipePlacementRepo>();
        services.AddTransient<IBendSettingsRepo, RevitBendSettingsRepo>();
        services.AddTransient<IPipeLayoutFamilySettingsRepo, RevitPipeLayoutFamilySettingsRepo>();
        services.AddTransient<IConcreteSettingsRepo, RevitConcreteSettingsRepo>();
        services.AddTransient<IAdaptiveBendPlacementRepo, RevitAdaptiveBendPlacementRepo>();
        services.AddTransient<IBendDirectShapeRepo, RevitBendDirectShapeRepo>();
        services.AddTransient<IReducerDirectShapeRepo, RevitReducerDirectShapeRepo>();
        services.AddTransient<IGenericModelRepository, RevitGenericModelRepository>();
        services.AddTransient<IExteriorWallClassifierRepo, RevitExteriorWallClassifierRepo>();
        services.AddTransient<IValveRoomOutlinePickRepo, RevitValveRoomOutlinePickRepo>();
        services.AddTransient<IValveRoomPipingPickRepo, RevitValveRoomPipingPickRepo>();
        #endregion

        services.AddTransient<IExcelReader, ExcelReader>();
        #region Service 등록
        services.AddTransient<IFileDialogService, WpfFileDialogService>();
        services.AddTransient<IDialogService, RevitDialogService>();
        services.AddTransient<IGuideLineService, RevitGuideLineService>();
        services.AddSingleton<IUsageLogger, DidasUsageService>();   // Didas 로그 연계
        #endregion
        return services;
    }

    /// <summary>
    /// Mock Infrastructure 서비스 등록 (Sandbox/테스트 환경용)
    /// </summary>
    public static IServiceCollection AddMockInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IRevitDispatcher, DirectRevitDispatcher>();
        // Mock 구현 등록 (Revit 없이 동작)
        services.AddSingleton<IGenericModelRepository, MockGenericModelRepository>();
        services.AddTransient<ITransactionContext, MockTransactionContext>();

        #region Element 관련
        services.AddTransient<ILevelQueryRepo, MockLevelQueryRepo>();
        services.AddTransient<ILevelCommandRepo, MockLevelCommandRepo>();
        services.AddTransient<IElementTypeQueryRepo, MockElementTypeQueryRepo>();
        services.AddTransient<IElementTypeCommandRepo, MockElementTypeCommandRepo>();
        services.AddTransient<IProjectLocationQueryRepo, MockProjectLocationQueryRepo>();
        services.AddTransient<IWallCommandRepo, MockWallCommandRepo>();
        services.AddTransient<IGenericModelCommandRepo, MockGenericModelCommandRepo>();
        services.AddTransient<IBeamCommandRepo, MockBeamCommandRepo>();
        services.AddTransient<ISlabCommandRepo, MockSlabCommandRepo>();
        services.AddTransient<IFoundationCommandRepo, MockFoundationCommandRepo>();
        services.AddTransient<IAirValveVoidCommandRepo, MockAirValveVoidCommandRepo>();
        services.AddTransient<IDrainPitVoidCommandRepo, MockDrainPitVoidCommandRepo>();
        services.AddTransient<IElementParameterCommandRepo, MockElementParameterCommandRepo>();
        services.AddTransient<IProjectInformationQueryRepo, MockProjectInformationQueryRepo>();
        services.AddTransient<IProjectInformationCommandRepo, MockProjectInformationCommandRepo>();
        services.AddTransient<IOpeningCommandRepo, MockOpeningCommandRepo>();
        services.AddTransient<IDirectShapeCommandRepo, MockDirectShapeCommandRepo>();
        services.AddTransient<IViewCommandRepo, MockViewCommandRepo>();
        services.AddTransient<IPipeMaterialScheduleRepo, MockPipeMaterialScheduleRepo>();
        services.AddTransient<IPipeMaterialDraftingRepo, MockPipeMaterialDraftingRepo>();
        services.AddTransient<IJunctionDetailRepo, MockJunctionDetailRepo>();
        services.AddTransient<IJunctionSymbolShapeRepo, MockJunctionSymbolShapeRepo>();
        services.AddTransient<IPipeMaterialBackfillRepo, MockPipeMaterialBackfillRepo>();
        services.AddTransient<IPipeJointConnectionRepo, MockPipeJointConnectionRepo>();

        services.AddTransient<ISetParameterRepo, MockSetParameterRepo>();
        services.AddTransient<ISharedParameterRepository, MockSharedParameterRepository>();
        services.AddTransient<IShapefileReader, ShapefileReader>();
        services.AddTransient<IAlignmentSourceReader, ShapefileReader>();
        services.AddTransient<IAlignmentSourceReader, DxfAlignmentReader>();
        services.AddTransient<IAlignmentSourceReader, DwgAlignmentReader>();
        services.AddTransient<IAlignmentSourceReader, ExcelAlignmentReader>();
        services.AddTransient<IExcelAlignmentSourceReader, ExcelAlignmentReader>();
        services.AddTransient<IAlignmentStraightPlacementRepo, MockAlignmentStraightPlacementRepo>();
        services.AddTransient<IAlignmentStraightDirectShapeRepo, MockAlignmentStraightDirectShapeRepo>();
        services.AddTransient<IAlignmentPipePlacementRepo, MockAlignmentPipePlacementRepo>();
        services.AddTransient<IAlignmentSourceReader, DxfAlignmentReader>();
        services.AddTransient<IAlignmentStraightPlacementRepo, RevitAlignmentStraightPlacementRepo>();
        services.AddTransient<IAlignmentStraightDirectShapeRepo, RevitAlignmentStraightDirectShapeRepo>();
        services.AddTransient<IAlignmentPipePlacementRepo, RevitAlignmentPipePlacementRepo>();
        // TODO: Mock 블록에 Revit Repo가 덮어쓰기 등록됨 — 별도 확인 필요
        services.AddTransient<IBendSettingsRepo, MockBendSettingsRepo>();
        services.AddTransient<IPipeLayoutFamilySettingsRepo, MockPipeLayoutFamilySettingsRepo>();
        services.AddTransient<IConcreteSettingsRepo, MockConcreteSettingsRepo>();
        services.AddTransient<IAdaptiveBendPlacementRepo, MockAdaptiveBendPlacementRepo>();
        services.AddTransient<IBendDirectShapeRepo, MockBendDirectShapeRepo>();
        services.AddTransient<IReducerDirectShapeRepo, MockReducerDirectShapeRepo>();
        #endregion

        services.AddTransient<IExcelReader, ExcelReader>();


        #region Service 등록
        services.AddTransient<IFileDialogService, WpfFileDialogService>();
        services.AddTransient<IDialogService, MockDialogService>();
        services.AddTransient<IGuideLineService, MockGuideLineService>();
        services.AddSingleton<IUsageLogger, MockUsageLogger>();
        #endregion

        return services;
    }
}
