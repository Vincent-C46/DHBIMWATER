using System.IO;
using DHBIMWATER.Infrastructure.Repositories.Local;
using Xunit;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.UI.Tests.Gis;

public sealed class BendSettingsFileStoreTests
{
    [Fact]
    public void FileStore_roundtrips_all_settings()
    {
        var path = TempPath();
        try
        {
            var store = new JsonFileBendSettingsFileStore();
            var settings = new BendSettings(
                new StraightPipeSpecTable(new[] { new StraightPipeSpec(PipeKindCatalog.Water1, 100, 118, 7.5) }),
                new JointDeflectionTable(new[] { new JointDeflectionSpec(JointTypeCatalog.Tyton, 100, 5) }),
                new BendFittingCatalog(new[]
                {
                    new BendFittingEntry(100, 45, BendForm.BType, 120, 210, WallThicknessMm: 8),
                    // 강관 행 — 종류(F)·한 개 무게 축(docs/211 § C-2)
                    new BendFittingEntry(100, 45, BendForm.BType, 350, 370, 4.9, Material: PipeMaterial.Steel, Connection: BendConnection.Socket, FittingClass: SteelFittingClass.F20, WeightKg: 9.08)
                }),
                JointTypeCatalog.Tyton,
                BendConnection.Flanged,
                BendForm.AType,
                ActiveSteelFittingClass: SteelFittingClass.F20,
                SteelApplicationMode: JointApplicationMode.BothJoints);

            store.Save(path, settings);
            var loaded = store.Load(path);

            Assert.Equal(7.5, loaded.StraightPipes.Find(PipeKindCatalog.Water1, 100)!.ThicknessMm);
            Assert.Equal(8, loaded.Fittings.Find(100, 45)!.WallThicknessMm);
            Assert.Equal(5, loaded.JointDeflections.AllowableFor(JointTypeCatalog.Tyton, 100));
            Assert.Equal(JointTypeCatalog.Tyton, loaded.ActiveJointType);
            Assert.Equal(JointApplicationMode.BothJoints, loaded.ApplicationMode);
            Assert.Equal(BendConnection.Flanged, loaded.ActiveBendConnection);
            Assert.Equal(SteelFittingClass.F20, loaded.ActiveSteelFittingClass);
            Assert.Equal(JointApplicationMode.BothJoints, loaded.SteelApplicationMode);
            var steel = loaded.Fittings.Find(100, 45, PipeMaterial.Steel, BendConnection.Socket, SteelFittingClass.F20);
            Assert.NotNull(steel);
            Assert.Equal(9.08, steel!.WeightKg);
            Assert.Equal(0d, steel.ExtraLegLengthMm);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>강관 곡관 종류 필드가 없는 구 파일은 F12로 읽힌다(docs/211 § C-8).</summary>
    [Fact]
    public void FileStore_reads_old_json_without_steel_class_as_f12()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """
                {"StraightPipes":[],"JointDeflections":[],"Fittings":[],"ActiveJointType":"KP 메커니컬 조인트","ApplicationMode":"SingleJoint","ActiveBendConnection":"Socket"}
                """);
            var loaded = new JsonFileBendSettingsFileStore().Load(path);
            Assert.Equal(SteelFittingClass.F12, loaded.ActiveSteelFittingClass);
            // 강관 접합 적용 필드가 없는 구 파일은 편측이다 — 기존 강관 곡관 슬롯 수 1을 지킨다(docs/243 § B).
            Assert.Equal(JointApplicationMode.SingleJoint, loaded.SteelApplicationMode);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FileStore_rejects_corrupt_json()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Throws<InvalidDataException>(() => new JsonFileBendSettingsFileStore().Load(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FileStore_rejects_json_without_required_tables()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidDataException>(() => new JsonFileBendSettingsFileStore().Load(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"dhbimwater-pipe-settings-{Guid.NewGuid():N}.json");
}
