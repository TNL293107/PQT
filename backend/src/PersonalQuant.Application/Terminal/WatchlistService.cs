using PersonalQuant.Application.Abstractions;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.Application.Terminal;

/// <summary>Reads and writes watchlists.</summary>
public interface IWatchlistRepository
{
    /// <summary>Lists every watchlist with how many instruments it holds, by name.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The lists.</returns>
    Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a watchlist and its entries, tracked for change.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The list, or <see langword="null"/> when there is none by that name.</returns>
    Task<Watchlist?> FindForUpdateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Reads a watchlist with each entry's instrument, in list order.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The list, or <see langword="null"/> when there is none by that name.</returns>
    Task<WatchlistView?> FindViewAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether a list already uses a name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when one does.</returns>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether an instrument exists.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    Task<bool> InstrumentExistsAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new list.</summary>
    /// <param name="watchlist">The list.</param>
    void Add(Watchlist watchlist);

    /// <summary>Stages a list's deletion, entries included.</summary>
    /// <param name="watchlist">The list, as loaded by <see cref="FindForUpdateAsync"/>.</param>
    void Remove(Watchlist watchlist);
}

/// <summary>A watchlist without its entries.</summary>
/// <param name="Name">The list's name.</param>
/// <param name="Count">How many instruments it holds.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="UpdatedAtUtc">When it last changed.</param>
public sealed record WatchlistSummary(
    string Name,
    int Count,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>A watchlist with each entry's instrument.</summary>
/// <param name="Name">The list's name.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="UpdatedAtUtc">When it last changed.</param>
/// <param name="Items">The entries, in list order.</param>
public sealed record WatchlistView(
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<WatchlistItem> Items);

/// <summary>One instrument on a watchlist, as a reader sees it.</summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Ticker">Its current ticker.</param>
/// <param name="Name">Its name.</param>
/// <param name="ExchangeCode">The venue it is listed on.</param>
/// <param name="Status">Its listing state, so a delisted name on a list is visible as one.</param>
/// <param name="Position">Its place in the list.</param>
/// <param name="AddedAtUtc">When it was added.</param>
public sealed record WatchlistItem(
    InstrumentId InstrumentId,
    string Ticker,
    string Name,
    string ExchangeCode,
    InstrumentStatus Status,
    int Position,
    DateTimeOffset AddedAtUtc);

/// <summary>How a watchlist change ended.</summary>
public enum WatchlistOutcome
{
    /// <summary>The change was made.</summary>
    Changed = 1,

    /// <summary>Nothing needed changing: the list already said so.</summary>
    Unchanged = 2,

    /// <summary>There is no list by that name.</summary>
    NotFound = 3,

    /// <summary>The instrument does not exist.</summary>
    InstrumentNotFound = 4,

    /// <summary>Another list already uses the name.</summary>
    NameTaken = 5,

    /// <summary>The request breaks a rule of the list itself.</summary>
    Invalid = 6,
}

/// <summary>What a watchlist change did.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Message">Why, when it did not change anything.</param>
/// <param name="Watchlist">The list as it now stands, when it exists.</param>
public sealed record WatchlistChange(WatchlistOutcome Outcome, string? Message, WatchlistView? Watchlist)
{
    /// <summary>Gets a value indicating whether the list is now as the caller asked.</summary>
    public bool Succeeded => Outcome is WatchlistOutcome.Changed or WatchlistOutcome.Unchanged;
}

/// <summary>
/// The operator's watchlists, as every interface reaches them (ADR-026, ADR-027).
/// </summary>
public interface IWatchlistService
{
    /// <summary>Lists every watchlist.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The lists, by name.</returns>
    Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one watchlist with its instruments.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The list, or <see langword="null"/> when there is none by that name.</returns>
    Task<WatchlistView?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty watchlist.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WatchlistChange> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Renames a watchlist.</summary>
    /// <param name="name">Its current name.</param>
    /// <param name="newName">Its new name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WatchlistChange> RenameAsync(string name, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a watchlist and its entries.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WatchlistChange> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Appends an instrument to a watchlist.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WatchlistChange> AddAsync(string name, InstrumentId instrumentId, CancellationToken cancellationToken = default);

    /// <summary>Removes an instrument from a watchlist.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    Task<WatchlistChange> RemoveAsync(string name, InstrumentId instrumentId, CancellationToken cancellationToken = default);
}

/// <summary>The one implementation of <see cref="IWatchlistService"/>.</summary>
/// <remarks>
/// Every outcome a caller can cause is a <see cref="WatchlistChange"/>, not an
/// exception: the API turns them into status codes, the CLI into exit codes
/// and the console into a line of text, and none of the three should have to
/// know which exception type means "no such list".
/// </remarks>
internal sealed class WatchlistService(
    IWatchlistRepository watchlists,
    IUnitOfWork unitOfWork,
    IClock clock) : IWatchlistService
{
    /// <summary>The most watchlists a deployment may hold.</summary>
    /// <remarks>A bound on what an anonymous-looking loop with a stolen key can create.</remarks>
    public const int MaxWatchlists = 100;

    /// <inheritdoc />
    public Task<IReadOnlyList<WatchlistSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        watchlists.ListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<WatchlistView?> FindAsync(string name, CancellationToken cancellationToken = default) =>
        Watchlist.IsValidName(name)
            ? watchlists.FindViewAsync(name, cancellationToken)
            : Task.FromResult<WatchlistView?>(null);

    /// <inheritdoc />
    public async Task<WatchlistChange> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!Watchlist.IsValidName(name))
        {
            return InvalidName(name);
        }

        if (await watchlists.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return Taken(name);
        }

        var existing = await watchlists.ListAsync(cancellationToken).ConfigureAwait(false);

        if (existing.Count >= MaxWatchlists)
        {
            return new WatchlistChange(
                WatchlistOutcome.Invalid,
                $"This deployment already holds {MaxWatchlists} watchlists, the most it keeps.",
                null);
        }

        watchlists.Add(Watchlist.Create(name, clock.UtcNow));

        // The check above and the commit are two steps; a concurrent create can
        // take the name between them, and the database's unique index is what
        // says so.
        if (!await TrySaveAsync(cancellationToken).ConfigureAwait(false))
        {
            return Taken(name);
        }

        return await ChangedAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WatchlistChange> RenameAsync(
        string name,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (!Watchlist.IsValidName(newName))
        {
            return InvalidName(newName);
        }

        var watchlist = await LoadAsync(name, cancellationToken).ConfigureAwait(false);

        if (watchlist is null)
        {
            return Missing(name);
        }

        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return await UnchangedAsync(name, cancellationToken).ConfigureAwait(false);
        }

        if (await watchlists.ExistsAsync(newName, cancellationToken).ConfigureAwait(false))
        {
            return Taken(newName);
        }

        watchlist.Rename(newName, clock.UtcNow);

        if (!await TrySaveAsync(cancellationToken).ConfigureAwait(false))
        {
            return Taken(newName);
        }

        return await ChangedAsync(newName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WatchlistChange> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var watchlist = await LoadAsync(name, cancellationToken).ConfigureAwait(false);

        if (watchlist is null)
        {
            return Missing(name);
        }

        watchlists.Remove(watchlist);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new WatchlistChange(WatchlistOutcome.Changed, null, null);
    }

    /// <inheritdoc />
    public async Task<WatchlistChange> AddAsync(
        string name,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        var watchlist = await LoadAsync(name, cancellationToken).ConfigureAwait(false);

        if (watchlist is null)
        {
            return Missing(name);
        }

        if (!await watchlists.InstrumentExistsAsync(instrumentId, cancellationToken).ConfigureAwait(false))
        {
            return new WatchlistChange(
                WatchlistOutcome.InstrumentNotFound,
                $"There is no instrument {instrumentId}.",
                null);
        }

        bool added;

        try
        {
            added = watchlist.Add(instrumentId, clock.UtcNow);
        }
        catch (DomainValidationException exception)
        {
            return new WatchlistChange(WatchlistOutcome.Invalid, exception.Message, null);
        }

        // A concurrent add of the same instrument loses to the primary key,
        // and the list is then exactly as this request asked: unchanged, not
        // failed.
        if (!added || !await TrySaveAsync(cancellationToken).ConfigureAwait(false))
        {
            return await UnchangedAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return await ChangedAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WatchlistChange> RemoveAsync(
        string name,
        InstrumentId instrumentId,
        CancellationToken cancellationToken = default)
    {
        var watchlist = await LoadAsync(name, cancellationToken).ConfigureAwait(false);

        if (watchlist is null)
        {
            return Missing(name);
        }

        // Removing what is not there leaves the list as asked, which is the
        // answer a retried request needs.
        if (!watchlist.Remove(instrumentId, clock.UtcNow))
        {
            return await UnchangedAsync(name, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ChangedAsync(name, cancellationToken).ConfigureAwait(false);
    }

    private static WatchlistChange InvalidName(string name) =>
        new(
            WatchlistOutcome.Invalid,
            $"'{name}' is not a watchlist name: lowercase letters, digits, _ and -, "
                + $"starting with a letter or digit, at most {Watchlist.MaxNameLength} characters.",
            null);

    private static WatchlistChange Taken(string name) =>
        new(WatchlistOutcome.NameTaken, $"A watchlist named '{name}' already exists.", null);

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (UniqueConstraintException)
        {
            return false;
        }
    }

    private static WatchlistChange Missing(string name) =>
        new(WatchlistOutcome.NotFound, $"There is no watchlist named '{name}'.", null);

    private Task<Watchlist?> LoadAsync(string name, CancellationToken cancellationToken) =>
        Watchlist.IsValidName(name)
            ? watchlists.FindForUpdateAsync(name, cancellationToken)
            : Task.FromResult<Watchlist?>(null);

    private async Task<WatchlistChange> ChangedAsync(string name, CancellationToken cancellationToken) =>
        new(
            WatchlistOutcome.Changed,
            null,
            await watchlists.FindViewAsync(name, cancellationToken).ConfigureAwait(false));

    private async Task<WatchlistChange> UnchangedAsync(string name, CancellationToken cancellationToken) =>
        new(
            WatchlistOutcome.Unchanged,
            null,
            await watchlists.FindViewAsync(name, cancellationToken).ConfigureAwait(false));
}
