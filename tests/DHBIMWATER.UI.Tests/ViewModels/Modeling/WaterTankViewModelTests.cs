using DHBIMWATER.Application.DTOs.Revit.Reservoir;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.UI.ViewModels.Modeling;
using System.Reflection;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

public class WaterTankViewModelTests
{
    [Fact]
    public void Defaults_ExposeLargeReservoirDimensionsInMeters()
    {
        var vm = CreateViewModel();

        Assert.Equal(25, vm.W);
        Assert.Equal(30, vm.L);
        Assert.Equal(4, vm.M1);
        Assert.Equal(4, vm.M2);
        Assert.Equal(4, vm.M3);
        Assert.Equal(4, vm.M4);
        Assert.Equal(2.5, vm.Wh);
        Assert.Equal(2.5, vm.Lh);
        Assert.Equal(2, vm.Hh);
        Assert.Equal(4, vm.H1F);
        Assert.Equal(30, vm.Lv);
        Assert.Equal(5, vm.Wv);
        Assert.Equal(2.5, vm.We);
        Assert.Equal(1, vm.Wp);
        Assert.Equal(1, vm.Hp);
        Assert.Equal(0, vm.Ltt);
        Assert.Equal(0, vm.Lvt);
        Assert.Equal(32.4, vm.CRT, 2);
        Assert.Equal(2.95, vm.H2F, 2);
    }

    [Fact]
    public void BuildCreationRequestDto_ConvertsMeterInputsToMillimetersForCalculator()
    {
        var vm = CreateViewModel();

        var dto = BuildCreationRequestDto(vm);

        Assert.Equal(25000, dto.TankDto.W);
        Assert.Equal(30000, dto.TankDto.L);
        Assert.Equal(4000, dto.TankDto.M1);
        Assert.Equal(4000, dto.TankDto.M2);
        Assert.Equal(4000, dto.TankDto.M3);
        Assert.Equal(4000, dto.TankDto.M4);
        Assert.Equal(2500, dto.TankDto.Wh);
        Assert.Equal(2500, dto.TankDto.Lh);
        Assert.Equal(2000, dto.TankDto.Hh);
        Assert.Equal(0, dto.TankDto.Ltt);
        Assert.Equal(4000, dto.ValveDto.H1F);
        Assert.Equal(30000, dto.ValveDto.Lv);
        Assert.Equal(5000, dto.ValveDto.Wv);
        Assert.Equal(0, dto.ValveDto.Lvt);
        Assert.Equal(2500, dto.ValveDto.We);
        Assert.Equal(1000, dto.ValveDto.Wp);
        Assert.Equal(1000, dto.ValveDto.Hp);
        Assert.Equal(100, dto.ValveDto.TrOff);
        Assert.Equal(300, dto.ValveDto.WpThk);
    }

    // docs/325 § E: 저장 → 새 창에서 복원하면 같은 값
    [Fact]
    public void SavedInputs_RestoreIntoNewWindow()
    {
        var repo = new MemoryReservoirInputRepo();
        var save = new DHBIMWATER.Application.UseCases.AutoGenerator.SaveReservoirInputUseCase(new StubTransaction(), repo);
        var vm = new WaterTankViewModel(null!, new StubDialogService(), new StubFileDialogService(), inputRepo: repo, saveInput: save)
        {
            W = 18.5,
            ColumnB = 700,
            BeamH = 800,
            HaunchB = 250,
            HasMiddleHaunch = false,
        };
        vm.SaveInputs();

        var restored = new WaterTankViewModel(null!, new StubDialogService(), new StubFileDialogService(), inputRepo: repo, saveInput: save);
        Assert.True(restored.RestoreSavedInputs());
        Assert.Equal(18.5, restored.W);
        Assert.Equal(700, restored.ColumnB);
        Assert.Equal(800, restored.BeamH);
        Assert.Equal(250, restored.HaunchB);
        Assert.False(restored.HasMiddleHaunch);
        Assert.True(restored.HasUpperHaunch);

        var dto = BuildCreationRequestDto(restored);
        Assert.Equal(700, dto.ThicknessDto.ColumnB);
        Assert.False(dto.ThicknessDto.HasMiddleHaunch);
    }

    private sealed class MemoryReservoirInputRepo : IReservoirInputRepo
    {
        private string? _json;
        public string? Load() => _json;
        public void Save(string json) => _json = json;
    }

    private sealed class StubTransaction : ITransactionContext
    {
        public void Begin(string name, bool suppressWarnings = false) { }
        public void Commit() { }
        public void Rollback() { }
        public IReadOnlyList<string> SuppressedWarnings => Array.Empty<string>();
        public void Dispose() { }
    }

    private static WaterTankViewModel CreateViewModel()
        => new(
            useCase: null!,
            dialogService: new StubDialogService(),
            fileDialogService: new StubFileDialogService());

    private static ReservoirCreationRequestDto BuildCreationRequestDto(WaterTankViewModel vm)
    {
        var method = typeof(WaterTankViewModel).GetMethod(
            "BuildCreationRequestDto",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<ReservoirCreationRequestDto>(method!.Invoke(vm, null));
    }

    private sealed class StubDialogService : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter) => null;
        public string? SaveFile(string title, string filter, string defaultFileName = "") => null;
    }
}
