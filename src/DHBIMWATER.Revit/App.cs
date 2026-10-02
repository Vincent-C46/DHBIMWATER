using Autodesk.Revit.UI;
using DHBIMWATER.Revit.Commands;
using DHBIMWATER.Revit.UI;
using System.Reflection;

namespace DHBIMWATER.Revit
{
    public class App : IExternalApplication
    {
        // Static constructor로 AssemblyResolve 이벤트를 가장 먼저 등록
        public App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string? assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? assemblyName = new AssemblyName(args.Name).Name;
            string assemblyPath = string.Empty;

            if (assemblyFolder != null)
                assemblyPath = Path.Combine(assemblyFolder, assemblyName + ".dll");

            if (File.Exists(assemblyPath))
                return Assembly.LoadFrom(assemblyPath);

            return null;
        }

        public Result OnStartup(UIControlledApplication application)
        {
            // DI 컨테이너 초기화 및 서비스 등록
            //ServiceContainer.Build();

            // Revit UI 등록
            RibbonBuilder.CreateRibbonPanel(application);

            // 밸브실 창의 유형 목록 자동 갱신용 구독. DocumentChanged는 Revit 세션 수명의 이벤트라
            // 명령마다 걸고 창을 닫을 때 해제하면 API 컨텍스트가 아닌 곳에서 해제하게 된다(docs/157 § B).
            application.ControlledApplication.DocumentChanged += ValveRoomRefreshSubscription.OnDocumentChanged;
            // 패밀리 파라미터 공유 캐시 무효화(docs/258 § A) — 캐시가 창이 아니라 문서 수명이라, 패밀리 재로드·문서 닫기에 비운다.
            application.ControlledApplication.FamilyLoadedIntoDocument += DHBIMWATER.Infrastructure.Repositories.Revit.Modeling.FamilyParameterCache.OnFamilyLoadedIntoDocument;
            application.ControlledApplication.DocumentClosing += DHBIMWATER.Infrastructure.Repositories.Revit.Modeling.FamilyParameterCache.OnDocumentClosing;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // 종료 시 정리 작업
            application.ControlledApplication.DocumentChanged -= ValveRoomRefreshSubscription.OnDocumentChanged;
            application.ControlledApplication.FamilyLoadedIntoDocument -= DHBIMWATER.Infrastructure.Repositories.Revit.Modeling.FamilyParameterCache.OnFamilyLoadedIntoDocument;
            application.ControlledApplication.DocumentClosing -= DHBIMWATER.Infrastructure.Repositories.Revit.Modeling.FamilyParameterCache.OnDocumentClosing;
            return Result.Succeeded;
        }

    }
}
