using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Infrastructure.Configuration;
using PersonalQuant.Infrastructure.Diagnostics;

namespace PersonalQuant.Infrastructure.MarketData;

/// <summary>
/// Runs the trading calendar and instrument imports once at start-up, when
/// <see cref="MarketDataOptions.ImportReferenceDataOnStartup"/> is enabled.
/// </summary>
/// <remarks>
/// <para>
/// The host the import pipelines were written for. Without something that
/// calls them they are code that passes its tests and never runs, and the
/// instrument master holds only what the development seed put there.
/// </para>
/// <para>
/// Ordered deliberately: the calendar first, then instruments. An instrument
/// import can create securities the calendar knows nothing about, but a
/// completeness figure computed before the calendar exists is meaningless, and
/// the cheaper of the two goes first so a failure in it is visible before the
/// longer one starts.
/// </para>
/// <para>
/// Both imports are additive and idempotent, so running at every start-up is
/// safe. A failure is logged rather than thrown: reference data being stale is
/// a worse outcome than an outage only if the outage is worse than stale
/// reference data, and it is not.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Factory used to resolve scoped services.</param>
/// <param name="options">Validated market data settings.</param>
/// <param name="logger">Logger for import progress.</param>
public sealed class ReferenceDataImportHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketDataOptions> options,
    ILogger<ReferenceDataImportHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ImportReferenceDataOnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();

        await ImportCalendarAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        await ImportInstrumentsAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ImportCalendarAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Absent rather than misconfigured. A deployment with no calendar
        // source reports completeness as unmeasured, which is a supported
        // state and not something to log an error about.
        if (services.GetService<ITradingCalendarProvider>() is null)
        {
            return;
        }

        try
        {
            var report = await services
                .GetRequiredService<ITradingCalendarImportService>()
                .ImportAsync(cancellationToken)
                .ConfigureAwait(false);

            InfrastructureLog.TradingCalendarImportCompleted(
                logger, report.Source, report.Created, report.AlreadyHeld, report.Rejections.Count);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            InfrastructureLog.ReferenceDataImportFailed(logger, exception, "trading calendar");
        }
    }

    private async Task ImportInstrumentsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (services.GetService<IInstrumentProvider>() is null)
        {
            return;
        }

        try
        {
            var report = await services
                .GetRequiredService<IInstrumentImportService>()
                .ImportAsync(source: null, cancellationToken)
                .ConfigureAwait(false);

            InfrastructureLog.InstrumentImportCompleted(
                logger, report.Source, report.Created, report.Matched, report.Rejected);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            InfrastructureLog.ReferenceDataImportFailed(logger, exception, "instrument list");
        }
    }

    /// <summary>
    /// Reports whether a failure is one the process should survive.
    /// </summary>
    /// <remarks>
    /// A database that is not ready, a source that cannot be read, or a
    /// composition problem such as two sources registered under one code. All
    /// of them leave the API able to serve what it already holds, which is
    /// better than refusing to start.
    /// </remarks>
    private static bool IsRecoverable(Exception exception) =>
        exception is DbException
            or TimeoutException
            or InvalidOperationException
            or MarketDataProviderException;
}
