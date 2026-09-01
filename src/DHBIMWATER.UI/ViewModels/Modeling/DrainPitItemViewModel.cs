using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public sealed class DrainPitItemViewModel : ViewModelBase
{
    private double _centerX;
    private double _centerY;
    private double _lengthX = 600;
    private double _widthY = 600;
    private double _depth = 300;
    private string _name = "드레인피트";
    private string _corner = "좌하단";

    public double CenterX { get => _centerX; set => SetProperty(ref _centerX, value); }
    public double CenterY { get => _centerY; set => SetProperty(ref _centerY, value); }
    // TODO: 기본 제원 확정 후 갱신
    public double LengthX { get => _lengthX; set => SetProperty(ref _lengthX, value); }
    public double WidthY { get => _widthY; set => SetProperty(ref _widthY, value); }
    public double Depth { get => _depth; set => SetProperty(ref _depth, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    /// <summary>밸브실 내부공간 코너. 체크박스 선택 UI는 후속 구현한다.</summary>
    public string Corner { get => _corner; set => SetProperty(ref _corner, value); }
}
