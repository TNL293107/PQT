using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// One alias an instrument is known by outside this system.
/// </summary>
/// <remarks>
/// A projection of <see cref="InstrumentIdentifier"/> flattened for reading.
/// The aggregate's own identifier is not carried: nothing outside the import
/// pipeline acts on an alias row individually, and exposing its key would
/// invite something to.
/// </remarks>
/// <param name="Scheme">The naming system.</param>
/// <param name="Value">The value, in the form the scheme requires.</param>
/// <param name="Source">
/// The provider that issued it, or <see langword="null"/> for a scheme that
/// names a security everywhere.
/// </param>
public sealed record InstrumentAlias(IdentifierScheme Scheme, string Value, string? Source);

/// <summary>
/// How another instrument is connected to the one that was asked about.
/// </summary>
/// <remarks>
/// <para>
/// A relation of <em>identity</em>, which is what Phase 1 can answer
/// truthfully. A peer group — the other securities in the same industry — is a
/// different question, and answering it usefully needs the size, liquidity and
/// valuation data that arrives with the fundamentals phase. Returning industry
/// members here and calling them related would present an alphabetical list as
/// an analytical one.
/// </para>
/// <para>
/// There is deliberately no "shares an ISIN" relation. A global identifier
/// resolves to exactly one instrument — the schema enforces it — because a
/// Vietnamese security lists on one venue at a time, so two instruments
/// carrying one cannot arise. Adding the kind would be a branch the database
/// forbids from ever being taken. It becomes reachable the day a cross-listed
/// universe relaxes that constraint, and the enumeration is shaped to take it
/// then without breaking the response.
/// </para>
/// </remarks>
public enum InstrumentRelationKind
{
    /// <summary>
    /// The two instruments have held the same ticker on the same venue at
    /// different times.
    /// </summary>
    /// <remarks>
    /// Vietnamese tickers are released on delisting and reassigned, so this is
    /// the relation that lets a user see that the FPT they are looking at is
    /// not the FPT a chart from six years ago was drawn from.
    /// </remarks>
    TickerHistory = 1,
}

/// <summary>
/// An instrument related to another, and why.
/// </summary>
/// <param name="Instrument">The related instrument.</param>
/// <param name="Relation">How it is connected.</param>
/// <param name="Detail">The value the connection rests on — the shared ticker.</param>
public sealed record RelatedInstrument(
    InstrumentSearchResult Instrument,
    InstrumentRelationKind Relation,
    string Detail);
