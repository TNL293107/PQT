using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// A provider's decorated symbol, split into the parts the instrument master
/// can act on.
/// </summary>
/// <remarks>
/// <para>
/// Providers do not agree on how to write a symbol. The same security arrives
/// as <c>FPT</c>, <c>FPT.HM</c>, <c>FPT:VN</c>, <c>HOSE:FPT</c> and
/// <c>FPT-HNX</c>, and every one of those spellings has to reach the same
/// canonical instrument. Normalising them is the half of workstream 4 that
/// import needs; the other half, folding case and diacritics for discovery,
/// already lives in <see cref="InstrumentSearchText"/>.
/// </para>
/// <para>
/// The decoration is not thrown away — the original spelling is kept as an
/// alias so that the next import from the same provider is a lookup rather
/// than a re-parse. What this type produces is the <em>candidate</em> ticker
/// and venue, which deduplication then uses to find an instrument.
/// </para>
/// </remarks>
public sealed record ProviderSymbol
{
    /// <summary>Characters a provider may separate a symbol from a venue with.</summary>
    private static readonly char[] Separators = ['.', ':', '-', '/', '_', ' '];

    /// <summary>
    /// Segments that are decoration rather than a security, mapped to the
    /// venue they imply — or to <see langword="null"/> where they imply none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hint, never an assertion. A provider's suffix says which venue it
    /// believes the security trades on, and providers are wrong about that
    /// often enough — particularly after a UPCOM to HNX to HOSE transfer,
    /// where a vendor's suffix can lag the move by months. Import prefers an
    /// explicitly supplied exchange and treats this as a fallback.
    /// </para>
    /// <para>
    /// <c>VN</c> maps to nothing: it names the country, not a venue, and
    /// guessing HOSE from it would silently attach HNX and UPCOM securities to
    /// the wrong exchange. It still has to be listed, because otherwise
    /// <c>FPT:VN</c> reads as two candidate tickers and cannot be resolved at
    /// all.
    /// </para>
    /// <para>
    /// The cost of that list is that a security whose ticker is one of these
    /// strings could not be parsed from a decorated symbol. None exist:
    /// Vietnamese equity tickers are three letters, and the index symbols are
    /// <c>VNINDEX</c> and <c>VN30</c>. It is the assumption to revisit first if
    /// a two-letter listing ever appears.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string?> Decorations =
        new(StringComparer.Ordinal)
        {
            ["HM"] = "HOSE",
            ["HOSE"] = "HOSE",
            ["HSX"] = "HOSE",
            ["XSTC"] = "HOSE",
            ["HN"] = "HNX",
            ["HNX"] = "HNX",
            ["XHNX"] = "HNX",
            ["UP"] = "UPCOM",
            ["UPCOM"] = "UPCOM",
            ["UPCOM3"] = "UPCOM",

            // A country qualifier, recognised so it is not mistaken for a
            // second ticker, but carrying no venue. Only VN belongs here:
            // VNM is Vinamilk, a listed security, and treating it as a
            // country code would make its own symbol unparseable.
            ["VN"] = null,
        };

    private ProviderSymbol(string raw, Ticker ticker, ExchangeCode? venueHint)
    {
        Raw = raw;
        Ticker = ticker;
        VenueHint = venueHint;
    }

    /// <summary>Gets the provider's spelling, upper-cased and trimmed.</summary>
    public string Raw { get; }

    /// <summary>Gets the exchange ticker the symbol resolves to.</summary>
    public Ticker Ticker { get; }

    /// <summary>
    /// Gets the venue the provider's decoration suggests, when it suggests
    /// one this system recognises.
    /// </summary>
    public ExchangeCode? VenueHint { get; }

    /// <summary>
    /// Splits a provider symbol into a ticker and an optional venue hint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fails rather than guesses when the symbol contains two segments that
    /// could each be a ticker. <c>ABC.DEF</c> is not something to resolve by
    /// picking the first: one of them is the security and the other is a venue
    /// this system has never heard of, and getting it backwards attaches a
    /// price series to the wrong company.
    /// </para>
    /// <para>
    /// A bare symbol with no decoration is the ordinary case and parses to a
    /// ticker with no hint.
    /// </para>
    /// </remarks>
    /// <param name="value">The provider's symbol.</param>
    /// <param name="symbol">The parsed parts when successful.</param>
    /// <param name="problem">A caller-safe explanation when parsing fails.</param>
    /// <returns><see langword="true"/> when the symbol resolves to one ticker.</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out ProviderSymbol? symbol,
        [NotNullWhen(false)] out string? problem)
    {
        symbol = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = "A provider symbol is required.";
            return false;
        }

        var raw = value.Trim().ToUpperInvariant();
        var segments = raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            problem = $"'{value}' contains no symbol.";
            return false;
        }

        Ticker? ticker = null;
        ExchangeCode? venueHint = null;

        foreach (var segment in segments)
        {
            // Decoration is recognised first. Several of these — HN, HM, UP —
            // are also well-formed tickers, so testing for a ticker first
            // would consume the venue and leave the security nameless.
            if (Decorations.TryGetValue(segment, out var venue))
            {
                if (venue is not null)
                {
                    venueHint ??= ExchangeCode.Create(venue);
                }

                continue;
            }

            if (!Ticker.TryCreate(segment, out var candidate))
            {
                problem = $"'{segment}' in '{value}' is neither a ticker nor a venue.";
                return false;
            }

            if (ticker is not null)
            {
                problem =
                    $"'{value}' contains more than one possible ticker, so it cannot be resolved.";
                return false;
            }

            ticker = candidate;
        }

        if (ticker is null)
        {
            // Every segment was a venue decoration. Reachable for a symbol
            // like "HOSE.HN", which names two venues and no security.
            problem = $"'{value}' names no security.";
            return false;
        }

        symbol = new ProviderSymbol(raw, ticker, venueHint);
        problem = null;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Raw;
}
