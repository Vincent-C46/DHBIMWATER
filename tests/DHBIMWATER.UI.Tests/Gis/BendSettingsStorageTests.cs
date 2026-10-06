using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using Xunit;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Repositories.Mock;

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

    [Fact]
    public void MockPlacementSave_preserves_tables_and_replaces_all_placement_settings()
    {
        var stored = BendSettings.Default with { ActiveJointType = "기존 Joint" };
        var updated = stored with
        {
            ActiveJointType = "새 Joint",
            ActiveBendConnection = BendConnection.Flanged,
            ActiveBendForm = BendForm.AType,
            UseRestraintGland = true,
            ActiveSteelFittingClass = SteelFittingClass.F20,
            SteelApplicationMode = JointApplicationMode.BothJoints
        };
        var repo = new MockBendSettingsRepo();
        repo.Save(stored);

        repo.SavePlacementSettings(updated);

        var result = repo.Load()!;
        Assert.Same(stored.StraightPipes, result.StraightPipes);
        Assert.Same(stored.JointDeflections, result.JointDeflections);
        Assert.Same(stored.Fittings, result.Fittings);
        Assert.Equal(updated.ActiveJointType, result.ActiveJointType);
        Assert.Equal(updated.ActiveBendConnection, result.ActiveBendConnection);
        Assert.Equal(updated.ActiveBendForm, result.ActiveBendForm);
        Assert.Equal(updated.UseRestraintGland, result.UseRestraintGland);
        Assert.Equal(updated.ActiveSteelFittingClass, result.ActiveSteelFittingClass);
        Assert.Equal(updated.SteelApplicationMode, result.SteelApplicationMode);
    }

    private static BendSettings Settings(string jointType) => BendSettings.Default with { ActiveJointType = jointType };

    private sealed class StubRepo : IBendSettingsRepo
    {
        private readonly BendSettings? _stored;
        public StubRepo(BendSettings? stored) => _stored = stored;
        public BendSettings? Load() => _stored;
        public void Save(BendSettings settings) => throw new NotSupportedException();
        public void SavePlacementSettings(BendSettings settings) => throw new NotSupportedException();
    }
}
