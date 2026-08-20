using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// A validated request to page through the instrument master.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="InstrumentSearchCriteria"/>, and not a superset of
/// it. Search answers "what could the user have meant?" and is ranked by
/// relevance; this answers "give me the universe matching these filters" and
/// is ordered deterministically, because a caller paging through it has to see
/// every row exactly once.
/// </para>
/// <para>
/// This is what a screener and a bulk export both read, so it is bounded the
/// same way search is — the instrument master is small today and is meant not
/// to be.
/// </para>
/// </remarks>
public sealed record InstrumentListCriteria
{
    /// <summary>Instruments returned when the caller does not ask for a number.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Most instruments a caller may request in one page.</summary>
    public const int MaxLimit = 500;

    private InstrumentListCriteria(
        ExchangeCode? exchange,
        AssetType? assetType,
        InstrumentStatus? status,
        ClassificationCode? sector,
        int limit,
        int offset)
    {
        Exchange = exchange;
        AssetType = assetType;
        Status = status;
        Sector = sector;
        Limit = limit;
        Offset = offset;
    }

    /// <summary>Gets the venue to restrict to, when one was given.</summary>
    public ExchangeCode? Exchange { get; }

    /// <summary>Gets the asset class to restrict to, when one was given.</summary>
    public AssetType? AssetType { get; }

    /// <summary>
    /// Gets the lifecycle state to restrict to, when one was given.
    /// </summary>
    /// <remarks>
    /// Absent means every state including delisted, which is the opposite of
    /// search's default. A list is the read that historical work uses, and
    /// silently omitting delisted rows from it is how survivorship bias gets
    /// into a universe.
    /// </remarks>
    public InstrumentStatus? Status { get; }

    /// <summary>Gets the sector to restrict to, when one was given.</summary>
    public ClassificationCode? Sector { get; }

    /// <summary>Gets the maximum number of instruments to return.</summary>
    public int Limit { get; }

    /// <summary>Gets how many instruments to skip.</summary>
    public int Offset { get; }

    /// <summary>
    /// Validates a list request.
    /// </summary>
    /// <param name="exchange">The venue to restrict to, or null.</param>
    /// <param name="assetType">The asset class to restrict to, or null.</param>
    /// <param name="status">The lifecycle state to restrict to, or null.</param>
    /// <param name="sector">The sector to restrict to, or null.</param>
    /// <param name="limit">The page size, or null for <see cref="DefaultLimit"/>.</param>
    /// <param name="offset">How many to skip, or null for none.</param>
    /// <param name="criteria">The validated criteria when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        ExchangeCode? exchange,
        AssetType? assetType,
        InstrumentStatus? status,
        ClassificationCode? sector,
        int? limit,
        int? offset,
        [NotNullWhen(true)] out InstrumentListCriteria? criteria,
        [NotNullWhen(false)] out string? problem)
    {
        criteria = null;

        var resolvedLimit = limit ?? DefaultLimit;

        if (resolvedLimit is < 1 or > MaxLimit)
        {
            problem = $"The page size must be between 1 and {MaxLimit}.";
            return false;
        }

        var resolvedOffset = offset ?? 0;

        if (resolvedOffset < 0)
        {
            problem = "The offset may not be negative.";
            return false;
        }

        if (assetType is Domain.Instruments.AssetType.Unspecified)
        {
            // Filtering for "unclassified" is a legitimate thing to want, but
            // it is a different query from filtering by asset class and it
            // would silently return nothing here.
            problem = "Filter by a specific asset class, not by the unspecified placeholder.";
            return false;
        }

        criteria = new InstrumentListCriteria(
            exchange, assetType, status, sector, resolvedLimit, resolvedOffset);
        problem = null;
        return true;
    }
}

/// <summary>
/// One page of the instrument master.
/// </summary>
/// <remarks>
/// The total is the count matching the filters, not the count in the page. A
/// caller cannot page sensibly without it, and the instrument master is small
/// enough that counting it is cheap — a judgement that would have to be
/// revisited if this ever spanned a global universe.
/// </remarks>
/// <param name="Items">The instruments in this page.</param>
/// <param name="Total">How many match the filters in total.</param>
/// <param name="Limit">The page size that was applied.</param>
/// <param name="Offset">How many were skipped.</param>
public sealed record InstrumentPage(
    IReadOnlyList<InstrumentSearchResult> Items,
    int Total,
    int Limit,
    int Offset);
