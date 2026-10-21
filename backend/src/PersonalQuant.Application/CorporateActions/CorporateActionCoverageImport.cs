using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.CorporateActions;

/// <summary>
/// A source of corporate action coverage records: which issuers' notices were
/// read in full, over which ex-dates (ADR-032).
/// </summary>
public interface ICorporateActionCoverageProvider
{
    /// <summary>Gets the code the source's symbols are recorded under.</summary>
    SourceCode Code { get; }

    /// <summary>Lists every record the source holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows, as the source states them.</returns>
    Task<IReadOnlyList<ProviderCorporateActionCoverage>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>One coverage row as a source states it.</summary>
/// <param name="Symbol">The source's symbol for the issuer.</param>
/// <param name="CompleteFrom">The first ex-date the sweep vouches for, or null when unreadable.</param>
/// <param name="SweptOn">The day the notices were read, or null when unreadable.</param>
/// <param name="Source">Where they were read.</param>
/// <param name="LineNumber">Where the row is in the source, for a rejection to point at.</param>
public sealed record ProviderCorporateActionCoverage(
    string Symbol,
    DateOnly? CompleteFrom,
    DateOnly? SweptOn,
    string? Source,
    int LineNumber);

/// <summary>Reads and stages coverage records.</summary>
public interface ICorporateActionCoverageRepository
{
    /// <summary>Lists every coverage record of an issuer.</summary>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records, in no particular order; none for an issuer never swept.</returns>
    Task<IReadOnlyList<CorporateActionCoverage>> ListAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new record.</summary>
    /// <param name="coverage">The record.</param>
    void Add(CorporateActionCoverage coverage);
}

/// <summary>Why a coverage row was not recorded.</summary>
/// <param name="LineNumber">Where the row is in the source.</param>
/// <param name="Symbol">The symbol as the source states it.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record CorporateActionCoverageRejection(int LineNumber, string Symbol, string Reason);

/// <summary>What a coverage import did.</summary>
/// <param name="Source">The source's code.</param>
/// <param name="Recorded">Records written.</param>
/// <param name="AlreadyHeld">Rows identical to a record already held.</param>
/// <param name="Rejections">Rows not recorded, with why.</param>
public sealed record CorporateActionCoverageImportReport(
    string Source,
    int Recorded,
    int AlreadyHeld,
    IReadOnlyList<CorporateActionCoverageRejection> Rejections);

/// <summary>Records the coverage a source states (ADR-032).</summary>
public interface ICorporateActionCoverageImportService
{
    /// <summary>Reads the registered source and records what is new.</summary>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>What was recorded, held already or refused.</returns>
    Task<CorporateActionCoverageImportReport> ImportAsync(CancellationToken cancellationToken = default);
}
