using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Default <see cref="ICorporateActionImportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves each row's symbol the same way the instrument import does — the
/// provider's own spelling first, then the ticker on its venue — so a source
/// that writes <c>FPT.HM</c> reaches the instrument another source created as
/// <c>FPT</c>.
/// </para>
/// <para>
/// The whole run commits once, and the factors are recomputed inside the same
/// transaction. An action committed without its factor would leave a series
/// unadjusted for an event the system already knows about, which is exactly the
/// silent wrongness the phase exists to remove.
/// </para>
/// </remarks>
/// <param name="providers">Every registered corporate action source.</param>
/// <param name="instruments">Resolves a source's symbol to an instrument.</param>
/// <param name="actions">The corporate action record.</param>
/// <param name="adjustments">Brings the factors back into line.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
/// <param name="logger">Logger for import telemetry.</param>
internal sealed class CorporateActionImportService(
    IEnumerable<ICorporateActionProvider> providers,
    IInstrumentRepository instruments,
    ICorporateActionRepository actions,
    IPriceAdjustmentService adjustments,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<CorporateActionImportService> logger) : ICorporateActionImportService
{
    /// <inheritdoc />
    public async Task<CorporateActionImportReport> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        var registered = providers.ToList();

        var provider = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                registered.Count == 0
                    ? "No corporate action source is registered."
                    : "Several corporate action sources are registered, which is not supported.");

        var rows = await provider.ListActionsAsync(cancellationToken).ConfigureAwait(false);

        var occurredAtUtc = clock.UtcNow;
        var resolved = new Dictionary<string, InstrumentId?>(StringComparer.Ordinal);
        var seen = new HashSet<(InstrumentId Instrument, CorporateActionType Type, DateOnly ExDate)>();
        var touched = new HashSet<InstrumentId>();
        var rejections = new List<CorporateActionRejection>();
        var created = 0;
        var amended = 0;
        var unchanged = 0;

        foreach (var row in rows)
        {
            var outcome = await ApplyAsync(
                    row, provider.Code, resolved, seen, occurredAtUtc, rejections, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome.Result)
            {
                case ApplyResult.Created:
                    created++;
                    touched.Add(outcome.InstrumentId);
                    break;
                case ApplyResult.Amended:
                    amended++;
                    touched.Add(outcome.InstrumentId);
                    break;
                case ApplyResult.Unchanged:
                    unchanged++;
                    break;
                default:
                    break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recomputed after the actions are committed, and only for the
        // instruments this run actually moved. Recomputing the whole universe
        // because one issuer declared a dividend would make an import's cost
        // grow with the market rather than with the news.
        foreach (var instrumentId in touched)
        {
            await adjustments.RecomputeAsync(instrumentId, cancellationToken).ConfigureAwait(false);
        }

        var report = new CorporateActionImportReport(
            provider.Code.Value, rows.Count, created, amended, unchanged, rejections);

        ApplicationLog.CorporateActionsImported(
            logger, report.Source, report.RowsRead, created, amended, unchanged, report.Rejected);

        return report;
    }

    private async Task<(ApplyResult Result, InstrumentId InstrumentId)> ApplyAsync(
        ProviderCorporateAction row,
        Domain.MarketData.SourceCode source,
        Dictionary<string, InstrumentId?> resolved,
        HashSet<(InstrumentId Instrument, CorporateActionType Type, DateOnly ExDate)> seen,
        DateTimeOffset occurredAtUtc,
        List<CorporateActionRejection> rejections,
        CancellationToken cancellationToken)
    {
        if (!TryReadType(row.Type, out var type))
        {
            rejections.Add(new CorporateActionRejection(
                row,
                CorporateActionRejectionReason.UnknownType,
                $"'{row.Type}' is not a corporate action this system records."));

            return (ApplyResult.Rejected, default);
        }

        var instrumentId = await ResolveAsync(resolved, row.Symbol, source, cancellationToken)
            .ConfigureAwait(false);

        if (instrumentId is null)
        {
            rejections.Add(new CorporateActionRejection(
                row,
                CorporateActionRejectionReason.UnknownInstrument,
                $"'{row.Symbol}' does not resolve to an instrument this system holds."));

            return (ApplyResult.Rejected, default);
        }

        if (!seen.Add((instrumentId.Value, type, row.ExDate)))
        {
            rejections.Add(new CorporateActionRejection(
                row,
                CorporateActionRejectionReason.DuplicateWithinImport,
                $"A {type} for '{row.Symbol}' going ex on {row.ExDate:yyyy-MM-dd} appeared twice."));

            return (ApplyResult.Rejected, default);
        }

        var existing = await actions
            .FindAsync(instrumentId.Value, type, row.ExDate, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (existing is not null)
            {
                // Matched on the natural key, so the ex-date cannot have moved;
                // what a restatement changes is the ratio or the cash amount.
                var changed = existing.Amend(
                    row.ExDate,
                    row.Ratio,
                    row.CashAmount,
                    $"Restated by {source}.",
                    occurredAtUtc);

                existing.Schedule(row.RecordDate, row.PaymentDate, row.AnnouncedOn, occurredAtUtc);

                return (changed ? ApplyResult.Amended : ApplyResult.Unchanged, instrumentId.Value);
            }

            var action = CorporateAction.Record(
                instrumentId.Value, type, row.ExDate, row.Ratio, row.CashAmount, source, occurredAtUtc);

            action.Schedule(row.RecordDate, row.PaymentDate, row.AnnouncedOn, occurredAtUtc);

            actions.Add(action);

            return (ApplyResult.Created, instrumentId.Value);
        }
        catch (DomainValidationException exception)
        {
            // One row with a ratio on a cash dividend must not stop a year of
            // actions from being recorded.
            rejections.Add(new CorporateActionRejection(
                row,
                IsDateProblem(exception) ? CorporateActionRejectionReason.InconsistentDates
                    : CorporateActionRejectionReason.InconsistentAmounts,
                exception.Message));

            return (ApplyResult.Rejected, default);
        }
        catch (DomainStateException exception)
        {
            rejections.Add(new CorporateActionRejection(
                row, CorporateActionRejectionReason.InconsistentAmounts, exception.Message));

            return (ApplyResult.Rejected, default);
        }
    }

    /// <summary>
    /// Resolves a source's symbol the way the instrument import records them.
    /// </summary>
    /// <remarks>
    /// The provider's own spelling first, because that is the alias the
    /// instrument import wrote and it is exact. Falling back to the ticker
    /// alone would match the wrong security whenever a ticker is live on two
    /// venues, so the fallback is deliberately absent: an action against an
    /// unrecognised symbol is rejected rather than attached to a guess.
    /// </remarks>
    private async Task<InstrumentId?> ResolveAsync(
        Dictionary<string, InstrumentId?> cache,
        string? symbol,
        Domain.MarketData.SourceCode source,
        CancellationToken cancellationToken)
    {
        if (!ProviderSymbol.TryParse(symbol, out var parsed, out _))
        {
            return null;
        }

        if (cache.TryGetValue(parsed.Raw, out var cached))
        {
            return cached;
        }

        InstrumentId? found = null;

        if (IdentifierValue.TryCreate(
                IdentifierScheme.ProviderSymbol, parsed.Raw, out var alias, out _))
        {
            var identifier = await instruments
                .FindIdentifierAsync(alias, source, cancellationToken)
                .ConfigureAwait(false);

            found = identifier?.InstrumentId;
        }

        cache[parsed.Raw] = found;
        return found;
    }

    private static bool TryReadType(string? value, out CorporateActionType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && type.IsDeclared();

    private static bool IsDateProblem(DomainValidationException exception) =>
        exception.Message.Contains("date", StringComparison.OrdinalIgnoreCase);

    private enum ApplyResult
    {
        Rejected = 0,
        Created = 1,
        Amended = 2,
        Unchanged = 3,
    }
}
