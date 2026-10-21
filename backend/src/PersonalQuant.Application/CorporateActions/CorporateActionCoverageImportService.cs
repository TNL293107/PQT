using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// Default <see cref="ICorporateActionCoverageImportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Symbols resolve exactly as corporate actions do: through the source's own
/// alias, with no fallback to a bare ticker, so a record is never attached to
/// a guess.
/// </para>
/// <para>
/// Append-only. A row restating a held record is left alone; a row with the
/// same sweep date and different terms is refused, because a coverage claim
/// decides whether a yield of zero is known, and is corrected by a new sweep
/// rather than by editing a file.
/// </para>
/// </remarks>
/// <param name="providers">Every registered coverage source.</param>
/// <param name="instruments">Resolves symbols.</param>
/// <param name="coverage">The coverage record.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
internal sealed class CorporateActionCoverageImportService(
    IEnumerable<ICorporateActionCoverageProvider> providers,
    IInstrumentRepository instruments,
    ICorporateActionCoverageRepository coverage,
    IUnitOfWork unitOfWork,
    IClock clock) : ICorporateActionCoverageImportService
{
    /// <inheritdoc />
    public async Task<CorporateActionCoverageImportReport> ImportAsync(CancellationToken cancellationToken = default)
    {
        var registered = providers.ToList();

        var provider = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                registered.Count == 0
                    ? "No corporate action coverage source is registered."
                    : "Several corporate action coverage sources are registered, which is not supported.");

        var rows = await provider.ListAsync(cancellationToken).ConfigureAwait(false);
        var occurredAtUtc = clock.UtcNow;
        var rejections = new List<CorporateActionCoverageRejection>();
        var recorded = 0;
        var alreadyHeld = 0;

        foreach (var row in rows)
        {
            void Reject(string reason) => rejections.Add(new CorporateActionCoverageRejection(row.LineNumber, row.Symbol, reason));

            if (row.CompleteFrom is not { } completeFrom || row.SweptOn is not { } sweptOn)
            {
                Reject("complete_from and swept_on are both required.");
                continue;
            }

            if (await ResolveAsync(row.Symbol, provider, cancellationToken).ConfigureAwait(false) is not { } instrumentId)
            {
                Reject($"'{row.Symbol}' is not a symbol the {provider.Code.Value} source has recorded for any instrument.");
                continue;
            }

            CorporateActionCoverage candidate;

            try
            {
                candidate = CorporateActionCoverage.Record(instrumentId, completeFrom, sweptOn, row.Source ?? string.Empty, occurredAtUtc);
            }
            catch (DomainValidationException invalid)
            {
                Reject(invalid.Message);
                continue;
            }

            var held = (await coverage.ListAsync(instrumentId, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(record => record.SweptOn == sweptOn);

            if (held is null)
            {
                coverage.Add(candidate);
                recorded++;
                continue;
            }

            if (held.CompleteFrom == candidate.CompleteFrom && string.Equals(held.Source, candidate.Source, StringComparison.Ordinal))
            {
                alreadyHeld++;
                continue;
            }

            Reject(
                $"A sweep of {sweptOn:yyyy-MM-dd} is already recorded from {held.CompleteFrom:yyyy-MM-dd}; "
                    + "a record is not edited. Sweep again and record the new date.");
        }

        if (recorded > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CorporateActionCoverageImportReport(provider.Code.Value, recorded, alreadyHeld, rejections);
    }

    private async Task<InstrumentId?> ResolveAsync(
        string symbol,
        ICorporateActionCoverageProvider provider,
        CancellationToken cancellationToken)
    {
        if (!ProviderSymbol.TryParse(symbol, out var parsed, out _)
            || !IdentifierValue.TryCreate(IdentifierScheme.ProviderSymbol, parsed.Raw, out var alias, out _))
        {
            return null;
        }

        var identifier = await instruments
            .FindIdentifierAsync(alias, provider.Code, cancellationToken)
            .ConfigureAwait(false);

        return identifier?.InstrumentId;
    }
}
