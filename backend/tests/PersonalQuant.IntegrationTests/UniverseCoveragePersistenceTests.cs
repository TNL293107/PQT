using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies that a universe's coverage gaps are recorded, and recorded once.
/// </summary>
/// <remarks>
/// The rules the review applies are decided above the database and are proved
/// without one. What needs a database is the promise that a nightly run cannot
/// stack duplicates of the same gap, which is a partial unique index and
/// nothing else.
/// </remarks>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class UniverseCoveragePersistenceTests(DependencyContainerFixture containers)
{
    private static readonly SourceCode Source = SourceCode.Create("TEST");
    private static readonly DateTimeOffset DefinedAt = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_universe_nobody_sourced_is_recorded_as_a_finding()
    {
        // The requirement in one test: a defined universe with no membership
        // leaves a record, so an empty history and a complete one are never
        // indistinguishable.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var universe = await DefineAsync(scope, UniverseCode.Create("UCA"));

        // Act
        var report = await scope.Review.ReviewAsync(TestContext.Current.CancellationToken);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.Raised >= 1);

        await using var reader = await CreateScopeAsync();
        var open = await reader.Universes.ListOpenFindingsAsync(
            universe.Id, TestContext.Current.CancellationToken);

        var finding = Assert.Single(open);
        Assert.Equal(UniverseCoverageFindingKind.NoMembershipRecorded, finding.Kind);
        Assert.True(finding.IsOpen);
        Assert.Contains("UCA", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_gap_cannot_stand_open_twice()
    {
        // The index, not the review's own checking. A second writer — a manual
        // run beside the scheduled one — must collide here rather than double
        // the list an operator reads.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var universe = await DefineAsync(scope, UniverseCode.Create("UCB"));

        scope.Universes.Add(UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "First.",
            DefinedAt));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var second = await CreateScopeAsync();
        second.Universes.Add(UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "Second.",
            DefinedAt));

        // Act & Assert
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => second.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            "ux_universe_coverage_findings_open",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_closed_finding_does_not_block_the_gap_returning()
    {
        // The index is partial for this reason. A gap can genuinely come back —
        // history sourced, then a claim widened past it — and a closed record
        // of the last time must not stop the new one being written.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var universe = await DefineAsync(scope, UniverseCode.Create("UCC"));

        var first = UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "First.",
            DefinedAt);
        first.Explain("Sourced.", DefinedAt.AddDays(1));

        scope.Universes.Add(first);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var second = await CreateScopeAsync();
        second.Universes.Add(UniverseCoverageFinding.Raise(
            universe.Id,
            UniverseCoverageFindingKind.NoMembershipRecorded,
            "Returned.",
            DefinedAt.AddDays(2)));

        // Act
        await second.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var open = await reader.Universes.ListOpenFindingsAsync(
            universe.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Returned.", Assert.Single(open).Detail);
    }

    [Fact]
    public async Task A_second_review_of_the_same_gap_records_nothing_new()
    {
        // Idempotence through the store rather than through a fake: the review
        // reads what it wrote last time and leaves it alone.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var universe = await DefineAsync(scope, UniverseCode.Create("UCD"));

        await scope.Review.ReviewAsync(TestContext.Current.CancellationToken);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await using var again = await CreateScopeAsync();
        await again.Review.ReviewAsync(TestContext.Current.CancellationToken);
        await again.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await using var reader = await CreateScopeAsync();
        var open = await reader.Universes.ListOpenFindingsAsync(
            universe.Id, TestContext.Current.CancellationToken);

        Assert.Single(open);
    }

    private static async Task<Universe> DefineAsync(UniverseScope scope, UniverseCode code)
    {
        var universe = Universe.Define(
            UniverseId.New(),
            code,
            $"{code.Value} Test Universe",
            UniverseKind.Index,
            Source,
            DefinedAt);

        scope.Universes.Add(universe);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return universe;
    }

    private async Task<UniverseScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new UniverseScope(factory);
    }

    private sealed class UniverseScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public UniverseScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Universes = _scope.ServiceProvider.GetRequiredService<IUniverseRepository>();
            Review = _scope.ServiceProvider.GetRequiredService<IUniverseCoverageReview>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IUniverseRepository Universes { get; }

        public IUniverseCoverageReview Review { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
