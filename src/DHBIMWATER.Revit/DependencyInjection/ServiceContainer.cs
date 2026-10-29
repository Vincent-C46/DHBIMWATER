using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.DependencyInjection;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Infrastructure.DependencyInjection;
using DHBIMWATER.Revit.Interop;
using DHBIMWATER.Revit.Commands;
using DHBIMWATER.UI.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.DependencyInjection
{
    public class ServiceContainer
    {
        private static IServiceProvider? _serviceProvider;
        public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new DhbimwaterException(ErrorCodes.ServiceContainerNotBuilt, "서비스 컨테이너가 준비되지 않았습니다. 명령을 다시 실행하세요.");

        internal static void Build(UIApplication uiApp)
        {
            Dispose();

            ServiceCollection services = new ServiceCollection();
            services.AddSingleton<Func<IntPtr>>(() => uiApp.MainWindowHandle);

            // 현재 활성 문서를 가져오는 람다 메서드를 서비스로 등록 - 다른 서비스에서 주입하여 호출할 때 현재 문서를 반환
            services.AddSingleton<Func<Document?>>(() => uiApp.ActiveUIDocument?.Document);

            // 사용자 피킹(Selection)이 필요한 Repository용 — Document와 동일하게 람다로 지연 해석한다.
            services.AddSingleton<Func<UIDocument?>>(() => uiApp.ActiveUIDocument);
            // 백그라운드 패밀리 문서 열기용(docs/309) — 활성 문서와 무관한 Application.
            services.AddSingleton<Func<Autodesk.Revit.ApplicationServices.Application>>(() => uiApp.Application);
            services.AddSingleton<IRevitDispatcher, RevitDispatcher>();

            // Revit 관련 서비스 등록
            services.AddUIServices();                   // UI View/ViewModel
            services.AddRevitServices();                // Revit 관련 서비스
            services.AddApplicationServices();          // Application 서비스
            services.AddInfrastructureServices();       // Infrastructure 서비스
            services.AddSingleton<IAlignmentSnapshotStore, AlignmentSnapshotStore>();
            services.AddSingleton<IPipeCatalogSource, PipeCatalogAdapter>();
            services.AddSingleton<IPipeQuantityRules, PipeQuantityRulesAdapter>();
            services.AddTransient<ProjectLocationAdapter>();
            services.AddTransient<IProjectLocationCommandRepo>(provider => provider.GetRequiredService<ProjectLocationAdapter>());
            services.AddTransient<IProjectLocationQueryRepo>(provider => provider.GetRequiredService<ProjectLocationAdapter>());
            services.AddTransient<IProjectLocationCoordinateTransformRepo>(provider => provider.GetRequiredService<ProjectLocationAdapter>());

            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>현재 컨테이너를 폐기하지 않고 떼어 호출자에게 넘긴다(docs/236 § A).
        /// 모델리스 창이 자기 컨테이너(→ 싱글턴 RevitDispatcher 등)의 수명을 소유하게 하려는 것이다 —
        /// 창이 열린 동안 다른 명령이 <see cref="Build"/>로 공용 슬롯을 갈아도 창의 서비스는 살아 있다. 호출자가 닫을 때 폐기한다.</summary>
        internal static IDisposable? Detach()
        {
            var detached = _serviceProvider as IDisposable;
            _serviceProvider = null;
            return detached;
        }

        public static void Dispose()
        {
            if (_serviceProvider is IDisposable d)
                d.Dispose();

            _serviceProvider = null;
        }

        // notnull 제약 조건을 사용하여 T가 null이 될 수 없음을 보장
        public static T GetService<T>() where T : notnull
        {
            return ServiceProvider.GetRequiredService<T>();
        }
    }
}
