using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>드래프팅뷰 도면의 생성·갱신 분기(docs/173).
/// 드래프팅뷰는 선·문자를 박아 넣은 정적 도면이라 일람표와 달리 저절로 갱신되지 않는다.
/// 갱신은 사용자가 승인한 경우에만 일어나야 하고, 승인하지 않으면 종전 동작(건너뛰기)이어야 한다.</summary>
public class CreatePipeMaterialDraftingUseCaseTests
{
    private const string ViewA = CreatePipeMaterialDraftingUseCase.NamePrefix + "_V-1";
    private const string ViewB = CreatePipeMaterialDraftingUseCase.NamePrefix + "_V-2";
    private const string ViewUnassigned = CreatePipeMaterialDraftingUseCase.UnassignedName;

    /// <summary>격점 V-1·V-2에 부재가 하나씩 있는 모델.</summary>
    private static PipeMaterialPart[] TwoJunctions()
        => [new("V-1", 1, "직관", "D160x6.00m"), new("V-2", 1, "곡관", "D160x45°")];

    private static CreatePipeMaterialDraftingUseCase UseCase(FakeDraftingRepo repo)
        => new(new StubTransaction(), repo);

    [Fact]   // T1
    public void Execute_CreatesAllWhenNoViewExists()
    {
        var repo = new FakeDraftingRepo(TwoJunctions());

        var result = UseCase(repo).Execute(updateExisting: false);

        Assert.Equal([ViewA, ViewB], result.Created);
        Assert.Empty(result.Updated);
        Assert.Empty(result.Skipped);
        Assert.Equal([ViewA, ViewB], repo.CreatedViews);
        Assert.Empty(repo.RedrawnViews);
    }

    [Fact]   // T2 — 종전 동작 회귀
    public void Execute_SkipsExistingWhenUpdateNotApproved()
    {
        var repo = new FakeDraftingRepo(TwoJunctions(), existing: [ViewA, ViewB]);

        var result = UseCase(repo).Execute(updateExisting: false);

        Assert.Equal([ViewA, ViewB], result.Skipped);
        Assert.Empty(result.Created);
        Assert.Empty(result.Updated);
        Assert.Empty(repo.RedrawnViews);      // 승인 없이는 뷰를 비우지 않는다
        Assert.Empty(repo.CreatedViews);
    }

    [Fact]   // T3
    public void Execute_RedrawsExistingWhenUpdateApproved()
    {
        var repo = new FakeDraftingRepo(TwoJunctions(), existing: [ViewA, ViewB]);

        var result = UseCase(repo).Execute(updateExisting: true);

        Assert.Equal([ViewA, ViewB], result.Updated);
        Assert.Empty(result.Created);
        Assert.Empty(result.Skipped);
        Assert.Equal([ViewA, ViewB], repo.RedrawnViews);
        Assert.Empty(repo.CreatedViews);
    }

    /// <summary>그 이름을 다른 종류의 뷰가 쓰고 있으면 리포지토리가 false를 돌려준다 —
    /// 예외로 트랜잭션을 롤백해 정상인 다른 격점 도면까지 날리면 안 된다(docs/173 § B).</summary>
    [Fact]   // T4
    public void Execute_SkipsWhenRedrawReportsNoDraftingView()
    {
        var repo = new FakeDraftingRepo(TwoJunctions(), existing: [ViewA, ViewB], redrawResult: false);

        var result = UseCase(repo).Execute(updateExisting: true);

        Assert.Equal([ViewA, ViewB], result.Skipped);
        Assert.Empty(result.Updated);
        Assert.Empty(result.Created);
    }

    [Fact]   // T5
    public void Execute_SplitsCreatedAndUpdated()
    {
        var repo = new FakeDraftingRepo(TwoJunctions(), existing: [ViewA]);

        var result = UseCase(repo).Execute(updateExisting: true);

        Assert.Equal([ViewA], result.Updated);
        Assert.Equal([ViewB], result.Created);
        Assert.Empty(result.Skipped);
    }

    [Fact]   // T6
    public void FindExistingViews_ReturnsOnlyExistingTargets()
    {
        var repo = new FakeDraftingRepo(TwoJunctions(), existing: [ViewB, "관계없는 뷰"]);

        Assert.Equal([ViewB], UseCase(repo).FindExistingViews());
    }

    /// <summary>모델에 없는 격점의 도면 뷰는 갱신 대상이 아니다 — 대상 목록 자체가 부재에서 나온다.</summary>
    [Fact]   // T6-2
    public void FindExistingViews_IgnoresViewsWithoutParts()
    {
        var repo = new FakeDraftingRepo([], existing: [ViewA, ViewB]);

        Assert.Empty(UseCase(repo).FindExistingViews());
    }

    [Fact]   // T7
    public void Execute_TreatsUnassignedTableLikeAnyOther()
    {
        var parts = new PipeMaterialPart[] { new("V-1", 1, "직관", "D160x6.00m"), new("", 2, "곡관", "D160x45°") };
        var repo = new FakeDraftingRepo(parts, existing: [ViewUnassigned]);

        var result = UseCase(repo).Execute(updateExisting: true);

        Assert.Equal([ViewUnassigned], result.Updated);
        Assert.Equal([ViewA], result.Created);
    }

    /// <summary>갱신 경로도 생성 경로와 같은 행 집계를 넘겨야 한다 — 두 표가 갈라지면 안 된다.</summary>
    [Fact]
    public void Execute_PassesSameRowsToRedrawAsCreate()
    {
        var parts = new PipeMaterialPart[]
        {
            new("V-1", 1, "직관", "D160x6.00m"),
            new("V-1", 1, "직관", "D160x6.00m"),
        };

        var created = new FakeDraftingRepo(parts);
        UseCase(created).Execute(updateExisting: false);

        var redrawn = new FakeDraftingRepo(parts, existing: [ViewA]);
        UseCase(redrawn).Execute(updateExisting: true);

        var fromCreate = Assert.Single(created.Calls);
        var fromRedraw = Assert.Single(redrawn.Calls);
        Assert.Equal(fromCreate.Title, fromRedraw.Title);
        Assert.Equal(fromCreate.Rows, fromRedraw.Rows);
        Assert.Equal(2, Assert.Single(fromRedraw.Rows).Count);   // 같은 부재 2개는 한 행 수량 2
    }

    [Fact]
    public void Execute_PassesImageOptionToCreateAndRedraw()
    {
        var parts = new[] { new PipeMaterialPart("V-1", 1, "직관", "D160x6.00m") };
        var created = new FakeDraftingRepo(parts);
        UseCase(created).Execute(includeTypeImage: false);
        Assert.False(Assert.Single(created.Calls).IncludeTypeImage);

        var redrawn = new FakeDraftingRepo(parts, existing: [ViewA]);
        UseCase(redrawn).Execute(updateExisting: true, includeTypeImage: false);
        Assert.False(Assert.Single(redrawn.Calls).IncludeTypeImage);
    }

    private sealed class FakeDraftingRepo : IPipeMaterialDraftingRepo
    {
        private readonly IReadOnlyList<PipeMaterialPart> _parts;
        private readonly HashSet<string> _existing;
        private readonly bool _redrawResult;

        public FakeDraftingRepo(IReadOnlyList<PipeMaterialPart> parts,
            IEnumerable<string>? existing = null, bool redrawResult = true)
        {
            _parts = parts;
            _existing = new HashSet<string>(existing ?? [], StringComparer.Ordinal);
            _redrawResult = redrawResult;
        }

        public List<string> CreatedViews { get; } = [];
        public List<string> RedrawnViews { get; } = [];
        public List<(string ViewName, string Title, IReadOnlyList<PipeMaterialRow> Rows, bool IncludeTypeImage)> Calls { get; } = [];

        public IReadOnlyList<PipeMaterialPart> GetParts() => _parts;

        public IReadOnlySet<string> GetExistingViewNames() => _existing;

        public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true)
        {
            CreatedViews.Add(viewName);
            Calls.Add((viewName, title, rows, includeTypeImage));
        }

        public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true)
        {
            if (!_redrawResult) return false;
            RedrawnViews.Add(viewName);
            Calls.Add((viewName, title, rows, includeTypeImage));
            return true;
        }
    }

    private sealed class StubTransaction : ITransactionContext
    {
        public void Begin(string name, bool suppressWarnings = false) { }
        public void Commit() { }
        public void Rollback() { }
        public IReadOnlyList<string> SuppressedWarnings => Array.Empty<string>();
        public void Dispose() { }
    }
}
