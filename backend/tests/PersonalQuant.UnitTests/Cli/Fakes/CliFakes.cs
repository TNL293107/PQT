using System.Text;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Cli.CommandLine;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Cli.Fakes;

/// <summary>
/// Collects what a command wrote, so a test can assert on the rendering
/// without a console.
/// </summary>
internal sealed class RecordedOutput
{
    private readonly StringBuilder _result = new();
    private readonly StringBuilder _problems = new();

    /// <summary>
    /// Builds the writer pair a command writes through.
    /// </summary>
    /// <remarks>
    /// The buffers are held rather than the writers. A <see cref="StringWriter"/>
    /// releases nothing, but a type holding one has to be disposable to say so,
    /// and that would put a using block around every harness in every CLI test
    /// to release two objects that hold no handle. Writing through to a builder
    /// keeps the assertion surface a plain string.
    /// </remarks>
    public RecordedOutput() =>
        Output = new Output(new StringWriter(_result), new StringWriter(_problems));

    /// <summary>Gets the writer pair a command was handed.</summary>
    public Output Output { get; }

    /// <summary>Gets everything written as a result.</summary>
    public string Result => _result.ToString();

    /// <summary>Gets everything written as a refusal.</summary>
    public string Problems => _problems.ToString();
}

/// <summary>
/// A promise a command must not redeem.
/// </summary>
/// <remarks>
/// The regression guard for the ordering rule: a command line that is refused,
/// and a command that reads only declarations, must never construct the
/// repository. Eagerly resolved, the database options are validated first, and
/// a typo is answered with four lines about a missing Postgres password.
/// </remarks>
internal static class Unreachable
{
    public static Lazy<TService> Service<TService>()
        where TService : notnull =>
        new(() => throw new InvalidOperationException(
            $"{typeof(TService).Name} was constructed. The command should not have needed it."));
}

/// <summary>Answers with whatever schema comparison a test declared.</summary>
internal sealed class FakeSchemaState : ISchemaState
{
    private SchemaComparison _comparison = new(0, null, []);

    public void Holds(int appliedCount, string? lastApplied, params string[] pending) =>
        _comparison = new SchemaComparison(appliedCount, lastApplied, pending);

    public Task<SchemaComparison> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_comparison);
}

/// <summary>Answers with whatever calendar coverage a test declared.</summary>
internal sealed class FakeTradingCalendar : ITradingCalendar
{
    private readonly List<VenueCalendarCoverage> _coverage = [];

    /// <summary>
    /// Declares a venue whose calendar was transcribed through a date, or one
    /// that declares nothing at all.
    /// </summary>
    public void Covers(string code, DateOnly? through) =>
        _coverage.Add(new VenueCalendarCoverage(
            ExchangeId.New(),
            ExchangeCode.Create(code),
            through is { } end
                ? CalendarCoverage.Create(new DateOnly(2022, 1, 1), end.AddDays(1))
                : null));

    public Task<TradingCalendarWindow> LoadAsync(
        ExchangeId exchangeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The deployment commands do not load a window.");

    public Task<IReadOnlyList<VenueCalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VenueCalendarCoverage>>([.. _coverage]);
}

/// <summary>A clock frozen wherever a test put it.</summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>Resolves whichever tickers a test declared.</summary>
internal sealed class FakeInstrumentResolver : IInstrumentResolver
{
    private readonly Dictionary<string, InstrumentSearchResult> _byTicker =
        new(StringComparer.OrdinalIgnoreCase);

    public int ResolveCount { get; private set; }

    /// <summary>Declares a listed instrument, and returns it.</summary>
    public InstrumentSearchResult Add(string ticker, string exchange = "HOSE")
    {
        var instrument = new InstrumentSearchResult(
            InstrumentId.New(),
            Ticker.Create(ticker),
            $"{ticker} Corporation",
            AssetType.Equity,
            ExchangeCode.Create(exchange),
            CurrencyCode.Create("VND"),
            InstrumentStatus.Listed,
            MatchKind: null);

        _byTicker[ticker] = instrument;

        return instrument;
    }

    public Task<InstrumentResolution> ResolveAsync(
        string? symbol,
        ExchangeCode? exchange = null,
        CancellationToken cancellationToken = default)
    {
        ResolveCount++;

        var query = symbol?.Trim().ToUpperInvariant() ?? string.Empty;

        return Task.FromResult(
            _byTicker.TryGetValue(query, out var instrument)
                ? InstrumentResolution.Resolved(query, instrument)
                : InstrumentResolution.NotFound(query));
    }

    public Task<InstrumentSearchResult?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byTicker.Values.FirstOrDefault(
            instrument => instrument.InstrumentId == id));
}

/// <summary>Records the instructions it was given, and answers with a script.</summary>
internal sealed class FakeIngestionService : IMarketDataIngestionService
{
    private readonly Queue<Func<IngestionInstruction, IngestionRun>> _script = new();

    public List<IngestionInstruction> Instructions { get; } = [];

    /// <summary>Declares what the next call returns.</summary>
    public FakeIngestionService Then(Func<IngestionInstruction, IngestionRun> outcome)
    {
        _script.Enqueue(outcome);

        return this;
    }

    public Task<IngestionRun> IngestAsync(
        IngestionInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        Instructions.Add(instruction);

        var next = _script.Count > 0
            ? _script.Dequeue()
            : throw new InvalidOperationException(
                $"The pipeline was called {Instructions.Count} times and the script has "
                    + "no answer left. A backfill loop that does not terminate would look "
                    + "exactly like this.");

        return Task.FromResult(next(instruction));
    }
}

/// <summary>Answers with whatever membership a test declared, known or not.</summary>
internal sealed class FakeUniverseCatalog : IUniverseCatalog
{
    private Func<UniverseCode, DateOnly, UniverseConstituents>? _answer;

    public int ReadCount { get; private set; }

    /// <summary>Gets the date the last read asked about.</summary>
    public DateOnly LastAsOf { get; private set; }

    public void Knows(params InstrumentId[] members) =>
        _answer = (code, asOf) => UniverseConstituents.Known(code, asOf, members);

    public void DoesNotKnow(UniverseUnknownReason reason) =>
        _answer = (code, asOf) => UniverseConstituents.Unknown(code, asOf, reason);

    public Task<UniverseConstituents> ConstituentsAsOfAsync(
        UniverseCode code,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        LastAsOf = asOf;

        var answer = _answer ?? throw new InvalidOperationException("No membership was declared.");

        return Task.FromResult(answer(code, asOf));
    }
}

/// <summary>Holds findings in memory and closes them the way the aggregate does.</summary>
internal sealed class FakeDataQualityService : IDataQualityService
{
    private readonly List<DataQualityIssue> _findings = [];

    public DataQualityIssue Add(
        InstrumentId instrumentId,
        DataQualityIssueKind kind,
        DateTimeOffset sessionAtUtc,
        string detail)
    {
        var finding = DataQualityIssue.Raise(
            instrumentId,
            BarInterval.OneDay,
            sessionAtUtc,
            kind,
            detail,
            DataRules.ValidationVersion,
            sessionAtUtc.AddDays(1));

        _findings.Add(finding);

        return finding;
    }

    public Task<DataQualityReport?> ScoreAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The CLI does not score.");

    public Task<IReadOnlyList<DataQualityIssue>> ListOpenIssuesAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DataQualityIssue>>(
            [.. _findings
                .Where(finding =>
                    finding.InstrumentId == instrumentId
                    && finding.Interval == interval
                    && finding.IsOpen)
                .Take(limit)]);

    public Task<DataQualityIssue?> ResolveIssueAsync(
        DataQualityIssueId issueId,
        DataQualityResolution outcome,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var finding = _findings.FirstOrDefault(candidate => candidate.Id == issueId);

        if (finding is null)
        {
            return Task.FromResult<DataQualityIssue?>(null);
        }

        if (outcome == DataQualityResolution.Explained)
        {
            finding.Explain(reason, DateTimeOffset.UnixEpoch);
        }
        else
        {
            finding.Dismiss(reason, DateTimeOffset.UnixEpoch);
        }

        return Task.FromResult<DataQualityIssue?>(finding);
    }
}
