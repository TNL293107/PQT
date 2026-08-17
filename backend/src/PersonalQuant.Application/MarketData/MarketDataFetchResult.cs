namespace PersonalQuant.Application.MarketData;

/// <summary>
/// One row exactly as a provider reported it, before any rule has been
/// applied to it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately made of primitives. <see cref="Domain.MarketData.OhlcvBar"/>
/// cannot be constructed in an invalid state, which is exactly why a provider
/// must not be the one constructing it — a row that fails validation has to
/// survive long enough to be reported as rejected, and a type that refuses to
/// hold it would force every provider to decide for itself what to do with
/// bad data.
/// </para>
/// <para>
/// The timestamp is the period's <em>opening</em> instant, and providers that
/// report the closing edge convert on the way out. That conversion is the one
/// piece of period semantics an implementation genuinely owns, because only it
/// knows which convention its source uses.
/// </para>
/// </remarks>
/// <param name="OpenedAtUtc">The instant the period opened, in UTC.</param>
/// <param name="Open">The first traded price.</param>
/// <param name="High">The highest traded price.</param>
/// <param name="Low">The lowest traded price.</param>
/// <param name="Close">The last traded price.</param>
/// <param name="Volume">Units traded.</param>
/// <param name="Turnover">Cash value traded, when the source reports it.</param>
public sealed record ProviderBar(
    DateTimeOffset OpenedAtUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Turnover);

/// <summary>
/// What a provider returned for one request.
/// </summary>
/// <remarks>
/// Both halves travel together on purpose. The parsed rows are what the
/// pipeline validates and stores; the payload is what makes it possible to
/// throw those rows away and derive them again when the parsing turns out to
/// have been wrong. Returning only the rows would make every normalisation bug
/// permanent for any range that can no longer be re-fetched.
/// </remarks>
/// <param name="Payload">The response, verbatim.</param>
/// <param name="ContentType">The declared media type, such as <c>text/csv</c>.</param>
/// <param name="Bars">The rows parsed from the payload, unvalidated.</param>
public sealed record MarketDataFetchResult(
    string Payload,
    string ContentType,
    IReadOnlyList<ProviderBar> Bars)
{
    /// <summary>
    /// A response that carried no rows.
    /// </summary>
    /// <param name="payload">The response, verbatim.</param>
    /// <param name="contentType">The declared media type.</param>
    /// <returns>An empty result.</returns>
    public static MarketDataFetchResult Empty(string payload, string contentType) =>
        new(payload, contentType, []);
}
