using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public sealed class DrainPitItemViewModel : ViewModelBase
{
    private double _centerX;
    private double _centerY;
    private double _lengthX = 1000;
    private double _widthY = 1000;
    private double _depth = 1000;
    private string _name = "드레인피트";
    private string _corner = "좌하단";

    public double CenterX { get => _centerX; set => SetProperty(ref _centerX, value); }
    public double CenterY { get => _centerY; set => SetProperty(ref _centerY, value); }
    public double LengthX { get => _lengthX; set => SetProperty(ref _lengthX, value); }
    public double WidthY { get => _widthY; set => SetProperty(ref _widthY, value); }
    /// <summary>드레인 피트의 실제 굴착 깊이.</summary>
    public double Depth { get => _depth; set => SetProperty(ref _depth, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    /// <summary>밸브실 내부공간 코너.</summary>
    public string Corner { get => _corner; set => SetProperty(ref _corner, value); }
}
