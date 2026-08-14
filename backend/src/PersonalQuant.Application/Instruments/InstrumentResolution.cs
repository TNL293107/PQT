namespace PersonalQuant.Application.Instruments;

/// <summary>How an attempt to resolve a symbol ended.</summary>
public enum InstrumentResolutionOutcome
{
    /// <summary>Exactly one instrument answers to the symbol.</summary>
    Resolved = 1,

    /// <summary>No active instrument answers to the symbol.</summary>
    NotFound = 2,

    /// <summary>
    /// More than one active instrument answers to the symbol, and the caller
    /// has to choose.
    /// </summary>
    Ambiguous = 3,
}

/// <summary>
/// The outcome of resolving a symbol to a canonical instrument.
/// </summary>
/// <remarks>
/// <para>
/// Ambiguity is a first-class outcome rather than an error, because it is
/// normal: ticker uniqueness is enforced per venue, so the same three letters
/// can be live on HOSE and on UPCOM at once. Collapsing that to "not found" or
/// silently picking the first row would eventually attach one company's prices
/// to another's identifier.
/// </para>
/// <para>
/// Construct through the factory methods; the combinations of outcome and
/// payload that make no sense are not expressible.
/// </para>
/// </remarks>
public sealed record InstrumentResolution
{
    private InstrumentResolution(
        InstrumentResolutionOutcome outcome,
        string query,
        InstrumentSearchResult? instrument,
        IReadOnlyList<InstrumentSearchResult> candidates)
    {
        Outcome = outcome;
        Query = query;
        Instrument = instrument;
        Candidates = candidates;
    }

    /// <summary>Gets how the attempt ended.</summary>
    public InstrumentResolutionOutcome Outcome { get; }

    /// <summary>Gets the folded symbol that was looked up.</summary>
    public string Query { get; }

    /// <summary>
    /// Gets the resolved instrument, or <see langword="null"/> unless
    /// <see cref="Outcome"/> is <see cref="InstrumentResolutionOutcome.Resolved"/>.
    /// </summary>
    public InstrumentSearchResult? Instrument { get; }

    /// <summary>
    /// Gets the competing instruments when the symbol is ambiguous, and an
    /// empty list otherwise.
    /// </summary>
    public IReadOnlyList<InstrumentSearchResult> Candidates { get; }

    /// <summary>Records that the symbol identified exactly one instrument.</summary>
    /// <param name="query">The folded symbol.</param>
    /// <param name="instrument">The instrument it identifies.</param>
    /// <returns>A resolved outcome.</returns>
    public static InstrumentResolution Resolved(string query, InstrumentSearchResult instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        return new InstrumentResolution(
            InstrumentResolutionOutcome.Resolved, query, instrument, []);
    }

    /// <summary>Records that no active instrument answers to the symbol.</summary>
    /// <param name="query">The folded symbol.</param>
    /// <returns>A not-found outcome.</returns>
    public static InstrumentResolution NotFound(string query) =>
        new(InstrumentResolutionOutcome.NotFound, query, null, []);

    /// <summary>Records that several instruments answer to the symbol.</summary>
    /// <param name="query">The folded symbol.</param>
    /// <param name="candidates">Every instrument that answers to it.</param>
    /// <returns>An ambiguous outcome.</returns>
    public static InstrumentResolution Ambiguous(
        string query,
        IReadOnlyList<InstrumentSearchResult> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new InstrumentResolution(
            InstrumentResolutionOutcome.Ambiguous, query, null, candidates);
    }
}
