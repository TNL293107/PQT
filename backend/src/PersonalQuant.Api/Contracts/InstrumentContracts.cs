using PersonalQuant.Application.Instruments;

namespace PersonalQuant.Api.Contracts;

/// <summary>
/// One instrument on the wire.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from <see cref="InstrumentSearchResult"/>, and from the
/// <c>Instrument</c> aggregate behind it. Value objects are flattened to
/// strings and enumerations to their names, so that the JSON a client sees is
/// stable even when the domain's internals move — and so that no persistence
/// concept leaks into a public schema.
/// </para>
/// <para>
/// Enumerations serialise as names rather than numbers on purpose. A client
/// reading <c>"Equity"</c> cannot silently misinterpret a reordered enum, and
/// the response stays readable in a log or a browser.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The canonical identifier every module joins on.</param>
/// <param name="Ticker">The exchange ticker, for display.</param>
/// <param name="Name">The security name.</param>
/// <param name="AssetType">The broad asset class.</param>
/// <param name="Exchange">The venue's operating code.</param>
/// <param name="Currency">The ISO 4217 quote currency.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="MatchKind">
/// Why this instrument matched the query. Absent outside search results,
/// where nothing was ranked.
/// </param>
public sealed record InstrumentResponse(
    Guid InstrumentId,
    string Ticker,
    string Name,
    string AssetType,
    string Exchange,
    string Currency,
    string Status,
    string? MatchKind)
{
    /// <summary>Projects an application result onto the wire contract.</summary>
    /// <param name="result">The result to project.</param>
    /// <returns>The response representation.</returns>
    public static InstrumentResponse From(InstrumentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new InstrumentResponse(
            result.InstrumentId.Value,
            result.Ticker.Value,
            result.Name,
            result.AssetType.ToString(),
            result.ExchangeCode.Value,
            result.Currency.Value,
            result.Status.ToString(),
            result.MatchKind?.ToString());
    }
}

/// <summary>
/// The result of an instrument search.
/// </summary>
/// <remarks>
/// The folded query is echoed back because the server normalises it — a
/// client that sent <c>" fpt "</c> should be able to see that <c>FPT</c> is
/// what was actually searched for.
/// </remarks>
/// <param name="Query">The folded query the server searched for.</param>
/// <param name="Count">How many results are in this response.</param>
/// <param name="Limit">The bound that was applied.</param>
/// <param name="Results">The matches, strongest first.</param>
public sealed record InstrumentSearchResponse(
    string Query,
    int Count,
    int Limit,
    IReadOnlyList<InstrumentResponse> Results);

/// <summary>
/// The result of resolving a symbol.
/// </summary>
/// <remarks>
/// Ambiguity is reported as a body with candidates rather than as a bare
/// error, because the caller can act on it: the terminal asks the user which
/// venue was meant, and a command retries with the exchange supplied.
/// </remarks>
/// <param name="Query">The folded symbol that was resolved.</param>
/// <param name="Outcome">Resolved, NotFound or Ambiguous.</param>
/// <param name="Instrument">The instrument, when exactly one matched.</param>
/// <param name="Candidates">The competing instruments, when several matched.</param>
public sealed record InstrumentResolutionResponse(
    string Query,
    string Outcome,
    InstrumentResponse? Instrument,
    IReadOnlyList<InstrumentResponse> Candidates)
{
    /// <summary>Projects a resolution onto the wire contract.</summary>
    /// <param name="resolution">The resolution to project.</param>
    /// <returns>The response representation.</returns>
    public static InstrumentResolutionResponse From(InstrumentResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        return new InstrumentResolutionResponse(
            resolution.Query,
            resolution.Outcome.ToString(),
            resolution.Instrument is null ? null : InstrumentResponse.From(resolution.Instrument),
            [.. resolution.Candidates.Select(InstrumentResponse.From)]);
    }
}
