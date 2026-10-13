using PersonalQuant.Application.Quotes;
using PersonalQuant.Application.Terminal;

namespace PersonalQuant.Api.Contracts;

/// <summary>A watchlist without its entries.</summary>
/// <param name="Name">The list's name.</param>
/// <param name="Count">How many instruments it holds.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="UpdatedAtUtc">When it last changed.</param>
public sealed record WatchlistSummaryResponse(
    string Name,
    int Count,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Maps the application's summary onto the wire contract.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The response.</returns>
    public static WatchlistSummaryResponse From(WatchlistSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new WatchlistSummaryResponse(
            summary.Name,
            summary.Count,
            summary.CreatedAtUtc,
            summary.UpdatedAtUtc);
    }
}

/// <summary>Every watchlist.</summary>
/// <param name="Results">The lists, by name.</param>
public sealed record WatchlistListResponse(IReadOnlyList<WatchlistSummaryResponse> Results);

/// <summary>One instrument on a watchlist.</summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Ticker">Its current ticker.</param>
/// <param name="Name">Its name.</param>
/// <param name="ExchangeCode">Its venue.</param>
/// <param name="Status">Its listing state.</param>
/// <param name="Position">Its place in the list.</param>
/// <param name="AddedAtUtc">When it was added.</param>
public sealed record WatchlistItemResponse(
    Guid InstrumentId,
    string Ticker,
    string Name,
    string ExchangeCode,
    string Status,
    int Position,
    DateTimeOffset AddedAtUtc);

/// <summary>A watchlist with its instruments.</summary>
/// <param name="Name">The list's name.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="UpdatedAtUtc">When it last changed.</param>
/// <param name="Count">How many instruments it holds.</param>
/// <param name="Items">The instruments, in list order.</param>
public sealed record WatchlistResponse(
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Count,
    IReadOnlyList<WatchlistItemResponse> Items)
{
    /// <summary>Maps the application's view onto the wire contract.</summary>
    /// <param name="view">The view.</param>
    /// <returns>The response.</returns>
    public static WatchlistResponse From(WatchlistView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new WatchlistResponse(
            view.Name,
            view.CreatedAtUtc,
            view.UpdatedAtUtc,
            view.Items.Count,
            [.. view.Items.Select(item => new WatchlistItemResponse(
                item.InstrumentId.Value,
                item.Ticker,
                item.Name,
                item.ExchangeCode,
                item.Status.ToString(),
                item.Position,
                item.AddedAtUtc))]);
    }
}

/// <summary>One instrument's quote on the session.</summary>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Ticker">The ticker it trades under now.</param>
/// <param name="Venue">Its venue.</param>
/// <param name="PreviousSession">The session its close is measured against.</param>
/// <param name="RawPreviousClose">The previous close as printed, when there is a bar.</param>
/// <param name="Reference">
/// The price the change is measured from — the previous close restated for
/// actions going ex on the session and rounded to the tick — when both bars exist.
/// </param>
/// <param name="Close">The session's close, when there is a bar.</param>
/// <param name="Change">The fractional change (0.012 is 1.2%), when both bars exist.</param>
/// <param name="Volume">The session's volume, when there is a bar.</param>
/// <param name="Move"><c>Advancing</c>, <c>Declining</c>, <c>Unchanged</c> or <c>NoData</c>.</param>
/// <param name="AdjustedForAction">Whether an action going ex on the session restated the previous close.</param>
public sealed record QuoteResponse(
    Guid InstrumentId,
    string Ticker,
    string Venue,
    DateOnly PreviousSession,
    decimal? RawPreviousClose,
    decimal? Reference,
    decimal? Close,
    decimal? Change,
    long? Volume,
    string Move,
    bool AdjustedForAction)
{
    /// <summary>Projects a quote onto the wire contract.</summary>
    /// <param name="quote">The quote.</param>
    /// <returns>The response representation.</returns>
    public static QuoteResponse From(Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        return new QuoteResponse(
            quote.InstrumentId.Value,
            quote.Ticker.Value,
            quote.Venue.Value,
            quote.PreviousSession,
            quote.RawPreviousClose,
            quote.Reference,
            quote.Close,
            quote.Change,
            quote.Volume,
            quote.Move.ToString(),
            quote.AdjustedForAction);
    }
}

/// <summary>A watchlist's instruments quoted on one session.</summary>
/// <remarks>
/// The same quotes market breadth counts, on the same basis, which is stated on
/// the response as <see cref="BreadthResponse"/> states it: the current series,
/// every factor this system knows.
/// </remarks>
/// <param name="Name">The list's name.</param>
/// <param name="Session">The session quoted; null only for an empty list with no session named.</param>
/// <param name="SessionWasDefaulted">
/// Whether no session was asked for and this is the latest one any instrument
/// on the list has a bar for.
/// </param>
/// <param name="KnownAsOf">Always null: quotes read the current series.</param>
/// <param name="AnnouncementPolicy">Always <c>permissive</c>.</param>
/// <param name="Venues">The venues the instruments trade on.</param>
/// <param name="Quotes">One quote per instrument, in list order.</param>
public sealed record WatchlistQuotesResponse(
    string Name,
    DateOnly? Session,
    bool SessionWasDefaulted,
    DateTimeOffset? KnownAsOf,
    string AnnouncementPolicy,
    IReadOnlyList<BreadthVenueResponse> Venues,
    IReadOnlyList<QuoteResponse> Quotes)
{
    /// <summary>Projects quoted watchlist onto the wire contract.</summary>
    /// <param name="quoted">The quotes.</param>
    /// <returns>The response representation.</returns>
    public static WatchlistQuotesResponse From(WatchlistQuotes quoted)
    {
        ArgumentNullException.ThrowIfNull(quoted);

        return new WatchlistQuotesResponse(
            quoted.Watchlist.Name,
            quoted.Board.Session,
            quoted.Board.SessionWasDefaulted,
            KnownAsOf: null,
            AnnouncementPolicy: "permissive",
            [.. quoted.Board.Venues.Select(venue => new BreadthVenueResponse(venue.Code.Value, venue.PreviousSession))],
            [.. quoted.Quotes.Select(QuoteResponse.From)]);
    }
}

/// <summary>Creates a watchlist.</summary>
/// <param name="Name">Its name.</param>
public sealed record CreateWatchlistRequest(string? Name);

/// <summary>Renames a watchlist.</summary>
/// <param name="Name">Its new name.</param>
public sealed record RenameWatchlistRequest(string? Name);
