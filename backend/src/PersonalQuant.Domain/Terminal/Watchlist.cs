using System.Text.RegularExpressions;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.Terminal;

/// <summary>Identifies a watchlist.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct WatchlistId(Guid Value)
{
    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static WatchlistId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A named, ordered list of instruments the operator wants to keep in view.
/// </summary>
/// <remarks>
/// <para>
/// Operator state, not a market fact: it lives in the <c>terminal</c> schema,
/// outside the system of record (ADR-027). It refers to instruments by their
/// canonical identifier and never by ticker, so a ticker change or a venue
/// transfer leaves the list pointing at the same security.
/// </para>
/// <para>
/// The name is the handle every interface uses — a URL segment, a CLI
/// argument, a console word — so it is constrained to characters that need no
/// quoting in any of them.
/// </para>
/// </remarks>
public sealed partial class Watchlist
{
    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The most instruments one list may hold.</summary>
    /// <remarks>
    /// A list longer than a screen is a universe, and universes have their own
    /// model with point-in-time membership. The bound also keeps one request
    /// from making the quote read arbitrarily wide.
    /// </remarks>
    public const int MaxEntries = 200;

    private readonly List<WatchlistEntry> _entries = [];

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Watchlist() => Name = null!;

    private Watchlist(WatchlistId id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Gets the list's identifier.</summary>
    public WatchlistId Id { get; private set; }

    /// <summary>Gets the list's name, unique across lists.</summary>
    public string Name { get; private set; }

    /// <summary>Gets when the list was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Gets when the list or its contents last changed.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Gets the instruments on the list, in the operator's order.</summary>
    public IReadOnlyList<WatchlistEntry> Entries =>
        [.. _entries.OrderBy(entry => entry.Position)];

    /// <summary>Creates an empty list.</summary>
    /// <param name="name">The list's name.</param>
    /// <param name="createdAtUtc">When it was created, in UTC.</param>
    /// <returns>The list.</returns>
    /// <exception cref="DomainValidationException">The name or the timestamp is not valid.</exception>
    public static Watchlist Create(string name, DateTimeOffset createdAtUtc)
    {
        RequireName(name);
        RequireUtc(createdAtUtc);

        return new Watchlist(WatchlistId.New(), name, createdAtUtc);
    }

    /// <summary>Gets a value indicating whether a string is acceptable as a list name.</summary>
    /// <param name="name">The candidate.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && NamePattern().IsMatch(name);

    /// <summary>Renames the list.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="changedAtUtc">When, in UTC.</param>
    /// <exception cref="DomainValidationException">The name or the timestamp is not valid.</exception>
    public void Rename(string name, DateTimeOffset changedAtUtc)
    {
        RequireName(name);
        RequireUtc(changedAtUtc);

        Name = name;
        UpdatedAtUtc = changedAtUtc;
    }

    /// <summary>Appends an instrument, unless it is already on the list.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="addedAtUtc">When, in UTC.</param>
    /// <returns><see langword="true"/> when it was added; <see langword="false"/> when it was already there.</returns>
    /// <exception cref="DomainValidationException">The list is full or the timestamp is not valid.</exception>
    public bool Add(InstrumentId instrumentId, DateTimeOffset addedAtUtc)
    {
        RequireUtc(addedAtUtc);

        // Already present is an answer, not an error: adding twice is what a
        // retried request does, and it must leave the list as one add would.
        if (_entries.Exists(entry => entry.InstrumentId == instrumentId))
        {
            return false;
        }

        if (_entries.Count >= MaxEntries)
        {
            throw new DomainValidationException(
                $"Watchlist '{Name}' already holds {MaxEntries} instruments, the most a list may hold.");
        }

        var position = _entries.Count == 0 ? 0 : _entries.Max(entry => entry.Position) + 1;

        _entries.Add(new WatchlistEntry(Id, instrumentId, position, addedAtUtc));
        UpdatedAtUtc = addedAtUtc;

        return true;
    }

    /// <summary>Removes an instrument, if it is on the list.</summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="removedAtUtc">When, in UTC.</param>
    /// <returns><see langword="true"/> when it was removed; <see langword="false"/> when it was not there.</returns>
    /// <exception cref="DomainValidationException">The timestamp is not valid.</exception>
    public bool Remove(InstrumentId instrumentId, DateTimeOffset removedAtUtc)
    {
        RequireUtc(removedAtUtc);

        // Positions are left with a gap rather than renumbered: order is what
        // they carry, and rewriting every later row to close a gap nobody sees
        // would turn one delete into many updates.
        if (_entries.RemoveAll(entry => entry.InstrumentId == instrumentId) == 0)
        {
            return false;
        }

        UpdatedAtUtc = removedAtUtc;

        return true;
    }

    private static void RequireName(string name)
    {
        if (!IsValidName(name))
        {
            throw new DomainValidationException(
                $"'{name}' is not a watchlist name: lowercase letters, digits, _ and -, "
                    + $"starting with a letter or digit, at most {MaxNameLength} characters.");
        }
    }

    private static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {value.Offset}.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>One instrument on a watchlist.</summary>
public sealed class WatchlistEntry
{
    // EF Core materialises through this constructor.
    private WatchlistEntry()
    {
    }

    internal WatchlistEntry(
        WatchlistId watchlistId,
        InstrumentId instrumentId,
        int position,
        DateTimeOffset addedAtUtc)
    {
        WatchlistId = watchlistId;
        InstrumentId = instrumentId;
        Position = position;
        AddedAtUtc = addedAtUtc;
    }

    /// <summary>Gets the list the entry belongs to.</summary>
    public WatchlistId WatchlistId { get; private set; }

    /// <summary>Gets the instrument.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the entry's place in the list; lower comes first, gaps allowed.</summary>
    public int Position { get; private set; }

    /// <summary>Gets when the instrument was added.</summary>
    public DateTimeOffset AddedAtUtc { get; private set; }
}
