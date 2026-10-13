using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Fundamentals;

/// <summary>
/// Supplies disclosed financial reports, each with the statement tables its
/// filing carries, exactly as the source rendered them.
/// </summary>
/// <remarks>
/// <para>
/// Like a market data provider, it stops at the wire. Cell text, column
/// headings and tab names come through unparsed. Turning them into periods
/// and values is the importer's job, done once, in one place, and testable
/// without a source.
/// </para>
/// <para>
/// Selection is explicit (ADR-029, the same rule as ADR-015): an import names
/// the provider it reads, and nothing falls back to another source.
/// </para>
/// </remarks>
public interface IFundamentalsProvider
{
    /// <summary>Gets the code that selects this provider and is stamped on what it supplies.</summary>
    SourceCode Code { get; }

    /// <summary>Gets where this provider's values are read, which every line it supplies carries.</summary>
    ValueBasis Basis { get; }

    /// <summary>Lists every filing the provider currently holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The filings.</returns>
    Task<IReadOnlyList<ProviderFiling>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One filing as a source rendered it.
/// </summary>
/// <param name="Reference">The source's key for the filing, stable across reads.</param>
/// <param name="Ticker">The issuer's ticker as the source lists it.</param>
/// <param name="ReportName">The report's name (<em>tên báo cáo</em>), which says its kind and scope.</param>
/// <param name="Summary">The filing's abstract (<em>trích yếu</em>).</param>
/// <param name="SubmittedOn">The venue-local date the source dates the filing.</param>
/// <param name="SubmittedAtUtc">The instant of filing, when the source gives a time.</param>
/// <param name="FiscalYear">The fiscal year the filing's form states.</param>
/// <param name="FiscalQuarter">The quarter the form states, or null when the form has none.</param>
/// <param name="Tables">The statement tables, one per tab.</param>
/// <param name="RawPayload">The document exactly as received, retained verbatim.</param>
/// <param name="ContentType">The payload's media type.</param>
/// <param name="FetchedAtUtc">When the filing was read from the source.</param>
public sealed record ProviderFiling(
    string Reference,
    string Ticker,
    string ReportName,
    string Summary,
    DateOnly SubmittedOn,
    DateTimeOffset? SubmittedAtUtc,
    int FiscalYear,
    int? FiscalQuarter,
    IReadOnlyList<ProviderStatementTable> Tables,
    string RawPayload,
    string ContentType,
    DateTimeOffset FetchedAtUtc);

/// <summary>One statement tab of a filing.</summary>
/// <param name="Name">The tab's name, such as <c>BCDKT</c>.</param>
/// <param name="ValueColumns">The headings of the value columns, in order.</param>
/// <param name="Rows">The rows.</param>
public sealed record ProviderStatementTable(
    string Name,
    IReadOnlyList<string> ValueColumns,
    IReadOnlyList<ProviderStatementRow> Rows);

/// <summary>One row of a statement tab.</summary>
/// <param name="Label">The line's label (<em>chỉ tiêu</em>).</param>
/// <param name="Code">The line's code (<em>mã số</em>), as written.</param>
/// <param name="Values">The cell text of each value column; empty for an empty cell.</param>
public sealed record ProviderStatementRow(
    string Label,
    string Code,
    IReadOnlyList<string> Values);
