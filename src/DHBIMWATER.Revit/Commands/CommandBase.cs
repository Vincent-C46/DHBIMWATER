using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Revit.DependencyInjection;

namespace DHBIMWATER.Revit.Commands
{
    public abstract class CommandBase : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // 공통 서비스 초기화
            ServiceContainer.Build(commandData.Application);

            try
            {
                return ExecuteInternal(commandData, ref message, elements);
            }
            catch (System.Exception ex)
            {
                // 코드 있는 예외는 "[코드] 메시지", 그 외는 [SYS-999] 원문(docs/205 § D-4). 스택은 로그 파일에.
                ErrorPresenter.Log(GetType().Name, ex);
                message = ErrorPresenter.Format(ex);
                return Result.Failed;
            }
            finally
            {
                // 서비스 정리
                ServiceContainer.Dispose();
            }
        }

        protected abstract Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements);
    }
}
