using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Documentation;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Documentation;

public sealed class PipeMaterialRefreshViewModelTests
{
    [Fact]
    public void TaggedOnly_RunsOnlyWhenRecountAllChecked()
    {
        var vm = Create(new PipeMaterialBackfillScan(0, false, RecountCount: 5));

        Assert.False(vm.CanRun);
        vm.RecountAll = true;
        Assert.True(vm.CanRun);
    }

    [Fact]
    public void EmptyAdaptive_CanRunWithoutRecount_AndShowsSelectionScope()
    {
        var vm = Create(new PipeMaterialBackfillScan(0, true, AdaptiveCandidateCount: 2));

        Assert.True(vm.CanRun);
        Assert.False(vm.RecountAll);
        Assert.Equal(2, vm.AdaptiveCount);
        Assert.Equal("선택한 부재 범위", vm.ScopeText);
    }

    [Fact]
    public void NoSelection_ShowsDocumentScope()
        => Assert.Equal("문서 전체 (선택한 부재 없음)", Create(new PipeMaterialBackfillScan(1, false)).ScopeText);

    [Fact]
    public void UnresolvedMaterialOnly_RunsOnlyAfterFallbackChosen()
    {
        var vm = Create(new PipeMaterialBackfillScan(0, false, MaterialMissingCount: 3, MaterialUnresolvedCount: 3));

        Assert.True(vm.HasUnresolvedMaterial);
        Assert.False(vm.CanRun);
        vm.FallbackMaterial = vm.MaterialChoices.Single(x => x.Code == "DCIP");
        Assert.True(vm.CanRun);
    }

    [Fact]
    public void MepPipeCandidates_CanRun()
        => Assert.True(Create(new PipeMaterialBackfillScan(0, false, MepPipeCandidateCount: 1)).CanRun);

    private static PipeMaterialRefreshViewModel Create(PipeMaterialBackfillScan scan)
        => new(new BackfillPipeMaterialParametersUseCase(null!, new ScanRepo(scan), null!, null!), null!);

    private sealed class ScanRepo(PipeMaterialBackfillScan scan) : IPipeMaterialBackfillRepo
    {
        public PipeMaterialBackfillScan ScanCandidates() => scan;
        public PipeMaterialBackfillResult Backfill(BendSettings catalog) => new(0, 0, []);
    }
}
