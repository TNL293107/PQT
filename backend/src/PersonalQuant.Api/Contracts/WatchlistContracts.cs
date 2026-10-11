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

/// <summary>Creates a watchlist.</summary>
/// <param name="Name">Its name.</param>
public sealed record CreateWatchlistRequest(string? Name);

/// <summary>Renames a watchlist.</summary>
/// <param name="Name">Its new name.</param>
public sealed record RenameWatchlistRequest(string? Name);
