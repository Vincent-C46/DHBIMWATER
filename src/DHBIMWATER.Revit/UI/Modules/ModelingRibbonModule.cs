using Autodesk.Revit.UI;
using DHBIMWATER.Revit.Commands;
using System.Reflection;

namespace DHBIMWATER.Revit.UI.Modules
{
    internal class ModelingRibbonModule : IRibbonModule
    {
        public IEnumerable<RibbonItem> Build(UIControlledApplication app, string ribbonTabName)
        {
            RibbonPanel panel = app.CreateRibbonPanel(ribbonTabName, "Modeling");

            // 버튼이름, 리본에 표시될 텍스트, 어셈블리 경로, 실행될 커맨드 클래스 풀네임
            PushButtonData reservoirBtn = new PushButtonData("ReservoirCommand", "배수지\n모델링", Assembly.GetExecutingAssembly().Location, RevitCommandType<WaterTankCommand>.FullName);
            PushButtonData pumpingStationBtn = new PushButtonData("PumpingStationCommand", "펌프장\n모델링", Assembly.GetExecutingAssembly().Location, RevitCommandType<PumpingStationCommand>.FullName);
            PushButtonData pipingBtn = new PushButtonData("PipeAlignmentModelingCommand", "관로\n모델링", Assembly.GetExecutingAssembly().Location, RevitCommandType<PipeAlignmentModelingCommand>.FullName);

            PushButtonData valveRoomBtn = new PushButtonData("ValveRoomCommand", "밸브실\n모델링", Assembly.GetExecutingAssembly().Location, RevitCommandType<ValveRoomCommand>.FullName);
            PushButtonData pipeLayoutBtn = new PushButtonData("PipeLayoutCommand", "밸브실\n배관", Assembly.GetExecutingAssembly().Location, RevitCommandType<PipeLayoutCommand>.FullName);

            PushButtonData coordinateLinkBtn = new PushButtonData("CoordinateLinkCommand", "기준점\n통합 링크", Assembly.GetExecutingAssembly().Location, RevitCommandType<CoordinateLinkCommand>.FullName);
            PushButtonData placementStationBtn = new PushButtonData("PlacementStationCommand", "측점 편집", Assembly.GetExecutingAssembly().Location, RevitCommandType<PlacementStationCommand>.FullName)
            {
                ToolTip = "배치된 관로·가변패밀리의 시점·종점 측점을 표에서 고치고, 바뀐 것만 다시 계산합니다."
            };

            reservoirBtn.LargeImage = RibbonButtonImages.GetIcon("water-tap.png");
            pumpingStationBtn.LargeImage = RibbonButtonImages.GetIcon("pump.png");
            pipingBtn.LargeImage = RibbonButtonImages.GetIcon("pipe_2.png");

            valveRoomBtn.LargeImage = RibbonButtonImages.GetIcon("valve.png");
            pipeLayoutBtn.LargeImage = RibbonButtonImages.GetIcon("pipe.png");
            coordinateLinkBtn.LargeImage = RibbonButtonImages.GetIcon("link.png");
            placementStationBtn.LargeImage = RibbonButtonImages.GetIcon("link.png");

            return
            [
                panel.AddItem(reservoirBtn),
                panel.AddItem(pumpingStationBtn),
                panel.AddItem(pipingBtn),
                panel.AddItem(valveRoomBtn),
                panel.AddItem(pipeLayoutBtn),
                panel.AddItem(coordinateLinkBtn),
                panel.AddItem(placementStationBtn),
            ];
        }
    }
}
