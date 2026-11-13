using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>펌프장 창 입력값 저장 → 새 창에서 복원(문서 ExtensibleStorage 대신 메모리 저장소).</summary>
public class PumpingStationInputStorageTests
{
    private sealed class MemoryRepo : IPumpingStationInputRepo
    {
        public string? Stored;
        public int SaveCount;
        public string? Load() => Stored;
        public void Save(string json) { Stored = json; SaveCount++; }
    }

    private sealed class NoTransaction : ITransactionContext
    {
        public void Begin(string name, bool suppressWarnings = false) { }
        public void Commit() { }
        public void Rollback() { }
        public IReadOnlyList<string> SuppressedWarnings => Array.Empty<string>();
        public void Dispose() { }
    }

    private sealed class StubDialogService : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }

    private static PumpingStationViewModel CreateVm(MemoryRepo repo)
    {
        var dialog = new StubDialogService();
        var basin = new DischargeBasinViewModel(dialog, null!);
        return new PumpingStationViewModel(null!, dialog, null!, null!, null!, null!, basin,
            inputRepo: repo, saveInput: new SavePumpingStationInputUseCase(new NoTransaction(), repo));
    }

    [Fact]
    public void SavedInputs_RestoreInNewWindow()
    {
        var repo = new MemoryRepo();
        var first = CreateVm(repo);
        first.SelectedPumpingStationType = "Type2";
        first.D = 1000;
        first.N = 5;
        first.B3 = 8123;
        first.L2 = 777;      // 계산값을 사용자가 고친 경우도 유지
        first.T6 = 350;
        first.SaveInputs();

        var second = CreateVm(repo);
        Assert.True(second.RestoreSavedInputs());

        Assert.Equal("Type2", second.SelectedPumpingStationType);
        Assert.Equal("측면부", second.SelectedEntranceType);
        Assert.Equal(1000, second.D);
        Assert.Equal(5, second.N);
        Assert.Equal(8123, second.B3);
        Assert.Equal(777, second.L2);
        Assert.Equal(350, second.T6);
        Assert.Equal(first.B7, second.B7);
        Assert.Equal(first.H4, second.H4);
    }

    [Fact]
    public void DischargeBasinInputs_RestoreInNewWindow()
    {
        var repo = new MemoryRepo();
        var first = CreateVm(repo);
        first.D = 1200;
        var basin = first.DischargeBasin;
        basin.SelectedPosition = DischargeBasinViewModel.PositionLeft;
        basin.SelectedConduit = DischargeBasinViewModel.ConduitPipe;
        basin.TA = 10.5;
        basin.ON = 3;
        basin.OD = 1500;
        basin.PlanRows.First(r => r.Key == "TB1").Value = 4100;   // 덮어쓴 권고안
        first.SaveInputs();

        var second = CreateVm(repo);
        Assert.True(second.RestoreSavedInputs());
        var restored = second.DischargeBasin;

        Assert.Equal(1200, restored.RefD);
        Assert.Equal(DischargeBasinViewModel.PositionLeft, restored.SelectedPosition);
        Assert.True(restored.IsPipe);
        Assert.Equal(10.5, restored.TA);
        Assert.Equal(3, restored.ON);
        Assert.Equal(1500, restored.OD);
        var tb1 = restored.PlanRows.First(r => r.Key == "TB1");
        Assert.Equal(4100, tb1.Value);
        Assert.True(tb1.IsEdited);
        // 덮어쓴 값이 하위 항목까지 같게 전파
        Assert.Equal(basin.PlanRows.First(r => r.Key == "TB2").Value, restored.PlanRows.First(r => r.Key == "TB2").Value);

        // 복원 직후 = 안 바뀜 → 닫을 때 저장 안 함
        var count = repo.SaveCount;
        second.SaveInputsIfChanged();
        Assert.Equal(count, repo.SaveCount);
        restored.OB = 2500;
        second.SaveInputsIfChanged();
        Assert.Equal(count + 1, repo.SaveCount);
    }

    [Fact]
    public void NoSavedInputs_KeepsDefaults()
    {
        var vm = CreateVm(new MemoryRepo());
        var d = vm.D;
        Assert.False(vm.RestoreSavedInputs());
        Assert.Equal(d, vm.D);
    }

    [Fact]
    public void SaveIfChanged_SkipsWhenUnchanged()
    {
        var repo = new MemoryRepo();
        var first = CreateVm(repo);
        first.B1 = 1500;
        first.SaveInputs();

        var second = CreateVm(repo);
        second.RestoreSavedInputs();
        second.SaveInputsIfChanged();
        Assert.Equal(1, repo.SaveCount);

        second.B1 = 1600;
        second.SaveInputsIfChanged();
        Assert.Equal(2, repo.SaveCount);
    }
}
