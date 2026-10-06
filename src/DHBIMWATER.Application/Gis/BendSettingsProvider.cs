using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Gis;

/// <summary>규격 설정이 어디서 왔는지. 사용자에게 안내 문구를 다르게 보여주기 위해 구분한다.</summary>
public enum BendSettingsSource
{
    /// <summary>이 Revit 문서의 DataStorage에 저장된 값.</summary>
    Project,

    /// <summary>어디에도 저장된 값이 없어 코드 내장 기본값을 쓴 경우.</summary>
    BuiltInDefault
}

public sealed record BendSettingsResolution(BendSettings Settings, BendSettingsSource Source);

/// <summary>
/// 배치 설정은 프로젝트에서 읽고, 규격표는 활성 문서의 카탈로그에서 읽는다.
/// </summary>
public sealed class BendSettingsProvider
{
    private readonly IBendSettingsRepo _project;
    private readonly IPipeCatalogSource? _catalog;

    public BendSettingsProvider(IBendSettingsRepo project, IPipeCatalogSource? catalog = null)
    { _project = project; _catalog = catalog; }

    public BendSettingsResolution Load()
    {
        var project = _project.Load();
        if (_catalog is null)
            return project is not null
                ? new(project, BendSettingsSource.Project)
                : new(BendSettings.Default, BendSettingsSource.BuiltInDefault);
        var tables = _catalog.LoadProjectTables();
        var modes = project ?? BendSettings.Default;
        return new(modes with
        {
            StraightPipes = tables.StraightPipes,
            Fittings = tables.Fittings,
            JointDeflections = tables.JointDeflections
        }, tables.Source == "Project" ? BendSettingsSource.Project : BendSettingsSource.BuiltInDefault);
    }
}
