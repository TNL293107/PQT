using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.MarketData;

/// <summary>
/// Ingests the listed universe on a timer, when
/// <see cref="MarketDataOptions.IngestOnSchedule"/> is enabled.
/// </summary>
/// <remarks>
/// <para>
/// The host the ingestion pipeline was written for. Checkpointing, resume and
/// incremental ingestion only mean anything if something runs repeatedly, and
/// until this existed the pipeline was code that passed its tests and never
/// ran.
/// </para>
/// <para>
/// Off by default, and deliberately so: starting the API should not begin
/// calling an external source. A deployment turns it on once it has decided
/// which source it reads and how often it may.
/// </para>
/// <para>
/// There is still no HTTP trigger. A request that causes outbound calls to a
/// rate-limited third party stays behind the authentication that arrives in
/// Phase 18; a timer the operator configured is a different thing.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Factory used to resolve scoped services.</param>
/// <param name="options">Validated market data settings.</param>
/// <param name="logger">Logger for schedule progress.</param>
public sealed class MarketDataIngestionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketDataOptions> options,
    ILogger<MarketDataIngestionHostedService> logger) : BackgroundService
{
    /// <summary>
    /// The source the scheduled pass names, or null to let selection decide.
    /// </summary>
    /// <remarks>
    /// Naming none is correct while exactly one registered source can serve
    /// the request, and stops being correct the moment two can: selection
    /// reports the ambiguity rather than picking by registration order, and
    /// every run is skipped with both candidates named in its reason.
    /// </remarks>
    private SourceCode? _scheduledSource;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.IngestOnSchedule)
        {
            return;
        }

        // Read here rather than at construction. The setting matters only once
        // the schedule is actually running, and parsing it in a field
        // initialiser let a stale value stop a host that had no intention of
        // ingesting anything.
        if (!settings.TryBuildIngestionSource(out _scheduledSource, out var sourceProblem))
        {
            InfrastructureLog.IngestionScheduleMisconfigured(logger, sourceProblem!);
            return;
        }

        var interval = settings.BuildIngestionInterval();
        var period = TimeSpan.FromMinutes(settings.IngestionPeriodMinutes);

        // Long enough for migration and seeding to finish. Ingesting against a
        // schema that is still being created would fail every instrument on
        // the first pass.
        await Task.Delay(
                TimeSpan.FromSeconds(settings.IngestionStartupDelaySeconds), stoppingToken)
            .ConfigureAwait(false);

        using var timer = new PeriodicTimer(period);

        do
        {
            await RunPassAsync(interval, settings.IngestionUniverseLimit, stoppingToken)
                .ConfigureAwait(false);
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and not something to log as one.
            return false;
        }
    }

    /// <summary>
    /// Ingests every listed instrument once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scope per instrument, not per pass. Each run is its own unit of work,
    /// and sharing one change tracker across a few hundred instruments would
    /// make a failure on the last of them roll back the first.
    /// </para>
    /// <para>
    /// Only listed instruments. A pending security has nothing to fetch and a
    /// delisted one has nothing new, so asking about either spends a
    /// rate-limited provider call to be told so.
    /// </para>
    /// </remarks>
    private async Task RunPassAsync(
        BarInterval interval,
        int universeLimit,
        CancellationToken stoppingToken)
    {
        var started = DateTimeOffset.UtcNow;
        var universe = await LoadUniverseAsync(universeLimit, stoppingToken).ConfigureAwait(false);

        if (universe is null)
        {
            return;
        }

        var ingested = 0;
        var failed = 0;

        foreach (var instrumentId in universe)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (await IngestAsync(instrumentId, interval, stoppingToken).ConfigureAwait(false))
            {
                ingested++;
            }
            else
            {
                failed++;
            }
        }

        InfrastructureLog.IngestionPassCompleted(
            logger,
            universe.Count,
            ingested,
            failed,
            (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
    }

    private async Task<IReadOnlyList<InstrumentId>?> LoadUniverseAsync(
        int universeLimit,
        CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            if (!InstrumentListCriteria.TryCreate(
                    exchange: null,
                    assetType: null,
                    InstrumentStatus.Listed,
                    sector: null,
                    universeLimit,
                    offset: 0,
                    out var criteria,
                    out var problem))
            {
                InfrastructureLog.IngestionScheduleMisconfigured(logger, problem);
                return null;
            }

            var page = await scope.ServiceProvider
                .GetRequiredService<IInstrumentCatalog>()
                .ListAsync(criteria, stoppingToken)
                .ConfigureAwait(false);

            if (page.Total > page.Items.Count)
            {
                // Said out loud rather than silently truncated. A universe
                // larger than one pass covers means the rest is never ingested,
                // and nothing else would report it.
                InfrastructureLog.IngestionUniverseTruncated(
                    logger, page.Items.Count, page.Total);
            }

            var items = page.Items.AsEnumerable();

            if (options.Value.TryBuildIngestionTickers(out var allowed))
            {
                var kept = page.Items
                    .Where(item => allowed.Contains(item.Ticker.Value))
                    .ToList();

                // Said out loud. A pass that silently ingested less than the
                // master holds would leave a gap whose only explanation was a
                // configuration value nobody was looking at.
                var named = string.Join(", ", allowed);

                InfrastructureLog.IngestionUniverseRestricted(
                    logger, kept.Count, page.Items.Count, named);

                items = kept;
            }

            return [.. items.Select(item => item.InstrumentId)];
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            InfrastructureLog.IngestionPassFailed(logger, exception);
            return null;
        }
    }

    private async Task<bool> IngestAsync(
        InstrumentId instrumentId,
        BarInterval interval,
        CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            if (!IngestionInstruction.TryCreate(
                    instrumentId,
                    interval,
                    source: _scheduledSource,
                    fromUtc: null,
                    toUtc: null,
                    out var instruction,
                    out var problem))
            {
                InfrastructureLog.IngestionScheduleMisconfigured(logger, problem);
                return false;
            }

            // The run's own outcome — succeeded, failed or skipped — is
            // already recorded in the audit trail by the pipeline. What this
            // returns is only whether the attempt itself completed, so one
            // instrument's provider outage cannot end the pass.
            await scope.ServiceProvider
                .GetRequiredService<IMarketDataIngestionService>()
                .IngestAsync(instruction, stoppingToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            InfrastructureLog.IngestionPassFailed(logger, exception);
            return false;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is DbException
            or TimeoutException
            or InvalidOperationException
            or MarketDataProviderException;
}
