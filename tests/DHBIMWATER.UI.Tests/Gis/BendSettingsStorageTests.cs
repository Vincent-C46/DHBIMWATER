using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class BendSettingsStorageTests
{
    [Theory]
    [InlineData(true, BendSettingsSource.Project)]
    [InlineData(false, BendSettingsSource.BuiltInDefault)]
    public void Provider_uses_project_then_built_in_default(bool hasProject, BendSettingsSource expected)
    {
        var project = Settings("프로젝트");
        var provider = new BendSettingsProvider(new StubRepo(hasProject ? project : null));

        var (settings, source) = provider.Load();

        Assert.Equal(expected, source);
        Assert.Equal(expected switch
        {
            BendSettingsSource.Project => project.ActiveJointType,
            _ => BendSettings.Default.ActiveJointType
        }, settings.ActiveJointType);
    }

    /// <summary>강관 행이 없는 프로젝트 저장본은 읽을 때 기본 강관 행이 병합된다(docs/211 § E-1). 주철관 편집값은 그대로다.</summary>
    [Fact]
    public void Provider_merges_steel_defaults_into_ductile_only_project_settings()
    {
        var edited = new BendSettings(
            new StraightPipeSpecTable(new[] { new StraightPipeSpec(PipeKindCatalog.Water1, 100, 118, 9.9) }),
            JointDeflectionTable.Default,
            new BendFittingCatalog(BendFittingCatalog.Default.Entries.Where(x => x.Material == PipeMaterial.DuctileIron).ToList()),
            JointTypeCatalog.Tyton);

        var (settings, source) = new BendSettingsProvider(new StubRepo(edited)).Load();

        Assert.Equal(BendSettingsSource.Project, source);
        Assert.Equal(9.9, settings.StraightPipes.Find(PipeKindCatalog.Water1, 100)!.ThicknessMm);
        Assert.Equal(JointTypeCatalog.Tyton, settings.ActiveJointType);
        Assert.NotNull(settings.StraightPipes.Find(PipeKindCatalog.Steel290, 100));
        Assert.NotNull(settings.Fittings.Find(100, 45, PipeMaterial.Steel, BendConnection.Socket, SteelFittingClass.F12));
    }

    private static BendSettings Settings(string jointType) => BendSettings.Default with { ActiveJointType = jointType };

    private sealed class StubRepo : IBendSettingsRepo
    {
        private readonly BendSettings? _stored;
        public StubRepo(BendSettings? stored) => _stored = stored;
        public BendSettings? Load() => _stored;
        public void Save(BendSettings settings) => throw new NotSupportedException();
    }
}
