using DHBIMWATER.Application.UseCases;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Application.UseCases.Gis;
using Microsoft.Extensions.DependencyInjection;

namespace DHBIMWATER.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Application 관련 서비스 등록
        services.AddTransient<CountGenericModelUseCase>();
        services.AddTransient<CreateReservoirUseCase>();
        services.AddTransient<CreatePumpingStationUseCase>();
        services.AddTransient<CreateValvePipingUseCase>();
        services.AddTransient<SavePipeLayoutFamilySettingsUseCase>();
        services.AddTransient<SaveConcreteSettingsUseCase>();
        services.AddTransient<CreateValveRoomUseCase>();
        // 토출조는 ViewModel이 인터페이스로 받는다 — UI 단위 테스트에서 Revit 의존 없이 스텁으로 갈아끼우기 위해서다.
        services.AddTransient<ICreateDischargeBasinUseCase, CreateDischargeBasinUseCase>();
        services.AddTransient<ClassifyExteriorWallsUseCase>();
        services.AddTransient<AlignmentSourceLoader>();
        services.AddTransient<ModelPipeAlignmentUseCase>();
        services.AddTransient<AnalyzePipeNetworkUseCase>();
        services.AddTransient<BendSettingsProvider>();
        services.AddTransient<SaveBendSettingsUseCase>();
        services.AddTransient<MigratePipeTablesUseCase>();
        services.AddTransient<CreatePipeMaterialScheduleUseCase>();
        services.AddTransient<CreatePipeMaterialDraftingUseCase>();
        services.AddTransient<BackfillPipeMaterialParametersUseCase>();

        return services;
    }
}
