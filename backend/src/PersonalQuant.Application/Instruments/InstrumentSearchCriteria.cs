using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// A validated instrument search request.
/// </summary>
/// <remarks>
/// <para>
/// Nothing downstream re-checks these values, so the type cannot be
/// constructed in an invalid state: the text is folded and non-empty, and the
/// limit is inside a range the database can serve cheaply. That is what makes
/// it safe for the repository to build a query straight from it.
/// </para>
/// <para>
/// The bound on the limit matters more than it looks. Instrument search runs
/// on every keystroke of the terminal's command bar, so an unbounded result
/// count is a way to make the database do arbitrary work on behalf of an
/// anonymous caller.
/// </para>
/// </remarks>
public sealed record InstrumentSearchCriteria
{
    /// <summary>Results returned when the caller does not ask for a specific number.</summary>
    public const int DefaultLimit = 20;

    /// <summary>Largest result count a caller may request.</summary>
    public const int MaxLimit = 50;

    /// <summary>
    /// Longest accepted query.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than any Vietnamese ticker or company name fragment
    /// anyone types, and short enough that the pattern handed to the database
    /// stays trivial.
    /// </remarks>
    public const int MaxTextLength = 64;

    private InstrumentSearchCriteria(string text, int limit, bool includeInactive)
    {
        Text = text;
        Limit = limit;
        IncludeInactive = includeInactive;
    }

    /// <summary>
    /// Gets the folded query text, as produced by
    /// <see cref="InstrumentSearchText.Normalise"/>.
    /// </summary>
    public string Text { get; }

    /// <summary>Gets the maximum number of results to return.</summary>
    public int Limit { get; }

    /// <summary>
    /// Gets a value indicating whether delisted instruments are included.
    /// </summary>
    /// <remarks>
    /// Off by default. A delisted security is still a real record that history
    /// references, but offering it as a selection in a search box would let a
    /// user set the terminal's context to something that cannot be traded or
    /// quoted.
    /// </remarks>
    public bool IncludeInactive { get; }

    /// <summary>
    /// Validates and folds a raw search request.
    /// </summary>
    /// <param name="text">The caller's query. Folded, and required to be non-empty.</param>
    /// <param name="limit">
    /// The requested result count, or <see langword="null"/> for
    /// <see cref="DefaultLimit"/>.
    /// </param>
    /// <param name="includeInactive">Whether to include delisted instruments.</param>
    /// <param name="criteria">The validated criteria when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        string? text,
        int? limit,
        bool includeInactive,
        [NotNullWhen(true)] out InstrumentSearchCriteria? criteria,
        [NotNullWhen(false)] out string? problem)
    {
        criteria = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "A search query is required.";
            return false;
        }

        if (text.Length > MaxTextLength)
        {
            problem = $"A search query may not exceed {MaxTextLength} characters.";
            return false;
        }

        var resolvedLimit = limit ?? DefaultLimit;

        if (resolvedLimit is < 1 or > MaxLimit)
        {
            problem = $"The result limit must be between 1 and {MaxLimit}.";
            return false;
        }

        var normalised = InstrumentSearchText.Normalise(text);

        if (normalised.Length == 0)
        {
            // Reachable when the query is punctuation or combining marks only:
            // non-empty on the way in, nothing left after folding.
            problem = "A search query must contain at least one letter or digit.";
            return false;
        }

        criteria = new InstrumentSearchCriteria(normalised, resolvedLimit, includeInactive);
        problem = null;
        return true;
    }
}
