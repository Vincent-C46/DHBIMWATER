namespace DHBIMWATER.Application.DTOs.Gis;

/// <summary>공유좌표를 Revit 모델 좌표로 변환한 결과(ft).</summary>
public readonly record struct ProjectLocationModelPoint(double XFeet, double YFeet, double ZFeet);
