using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Families;
using DHBIMWATER.Application.UseCases.Families;
using DHBIMWATER.Core.Families;
using DHBIMWATER.UI.ViewModels.Utilities;
using Xunit;

namespace DHBIMWATER.UI.Tests.UseCases;

/// <summary>docs/309 — 일괄 처리 순서(실패 격리·롤백)와 창 폴더 불러오기(백업 제외·매핑 복원).</summary>
public sealed class FamilyTypeImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dhbim-typeimage-" + Guid.NewGuid().ToString("N"));

    public FamilyTypeImageTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Execute_FailureInOneFile_RollsBackAndContinues()
    {
        var image = Touch("a.png");
        var factory = new FakeFactory();
        var repo = new FakeRepo { FailFor = "bad.rfa" };
        var useCase = new ApplyFamilyTypeImagesUseCase(factory, repo);

        var results = useCase.Execute([("good.rfa", image), ("bad.rfa", image)]);

        Assert.True(results[0].Succeeded);
        Assert.Equal(3, results[0].TypeCount);
        Assert.False(results[1].Succeeded);
        Assert.Equal(["Begin", "Commit", "Save", "Dispose"], factory.Sessions["good.rfa"].Log);
        // 실패 파일은 저장 안 하고 롤백 후 닫기.
        Assert.Equal(["Begin", "Rollback", "Dispose"], factory.Sessions["bad.rfa"].Log);
    }

    [Fact]
    public void Execute_MissingImage_DoesNotOpenFamily()
    {
        var factory = new FakeFactory();
        var result = new ApplyFamilyTypeImagesUseCase(factory, new FakeRepo()).Execute([("x.rfa", Path.Combine(_root, "none.png"))]).Single();

        Assert.False(result.Succeeded);
        Assert.Empty(factory.Sessions);
    }

    [Fact]
    public void LoadFamilyFolder_SkipsBackupsAndRestoresMapping()
    {
        var families = Directory.CreateDirectory(Path.Combine(_root, "fam")).FullName;
        var images = Directory.CreateDirectory(Path.Combine(_root, "img")).FullName;
        Touch(@"fam\gate.rfa");
        Touch(@"fam\gate.0001.rfa");
        Touch(@"fam\check.rfa");
        Touch(@"fam\sub\nested.rfa");
        Touch(@"img\gate.png");
        var store = new FakeStore();
        store.Saved[families] = new TypeImageMapping(images, new Dictionary<string, string> { ["gate.rfa"] = "gate.png" });

        var vm = new FamilyTypeImageViewModel(new ApplyFamilyTypeImagesUseCase(new FakeFactory(), new FakeRepo()), store, new FakeDialog());
        vm.LoadFamilyFolder(families);

        Assert.Equal(["check.rfa", "gate.rfa"], vm.Rows.Select(r => r.FileName));
        Assert.Equal(images, vm.ImageFolder);
        Assert.Equal(["gate.png"], vm.ImageChoices);
        var gate = vm.Rows.Single(r => r.FileName == "gate.rfa");
        Assert.True(gate.IsChecked);
        Assert.Equal(Path.Combine(images, "gate.png"), gate.ResolvedImagePath);
        Assert.False(vm.Rows.Single(r => r.FileName == "check.rfa").IsChecked);
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class FakeSession(string path) : IFamilyDocumentSession
    {
        public List<string> Log { get; } = [];
        public string Path { get; } = path;
        public void Begin(string name) => Log.Add("Begin");
        public void Commit() => Log.Add("Commit");
        public void Rollback() => Log.Add("Rollback");
        public void Save() => Log.Add("Save");
        public void Dispose() => Log.Add("Dispose");
    }

    private sealed class FakeFactory : IFamilyDocumentSessionFactory
    {
        public Dictionary<string, FakeSession> Sessions { get; } = [];
        public IFamilyDocumentSession Open(string rfaPath) => Sessions[rfaPath] = new FakeSession(rfaPath);
        public string? GetOlderVersion(string rfaPath) => null;
    }

    private sealed class FakeRepo : IFamilyTypeImageRepo
    {
        public string? FailFor { get; init; }
        public int Apply(IFamilyDocumentSession session, string imagePath)
            => session.Path == FailFor ? throw new InvalidOperationException("실패") : 3;
    }

    private sealed class FakeStore : ITypeImageMappingStore
    {
        public Dictionary<string, TypeImageMapping> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
        public TypeImageMapping Load(string familyFolder) => Saved.TryGetValue(familyFolder, out var m) ? m : TypeImageMapping.Empty;
        public void Save(string familyFolder, TypeImageMapping mapping) => Saved[familyFolder] = mapping;
    }

    private sealed class FakeDialog : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }
}
