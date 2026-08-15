using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// A financial instrument in the instrument master.
/// </summary>
/// <remarks>
/// <para>
/// The answer to "what is FPT?" — the record every other domain joins on. It
/// carries identity and lifecycle only. Prices, fundamentals, corporate
/// actions and positions reference it; none of them live on it.
/// </para>
/// <para>
/// An instrument is never deleted. Delisting moves it to a terminal state and
/// the row is retained, because historical data continues to reference it and
/// deleting master data would silently corrupt every series built on top.
/// </para>
/// </remarks>
public sealed class Instrument : AuditableEntity
{
    /// <summary>Longest permitted instrument name.</summary>
    public const int MaxNameLength = 300;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Instrument()
    {
        Ticker = null!;
        Name = null!;
        Currency = null!;
        SearchTicker = null!;
        SearchName = null!;
    }

    private Instrument(
        InstrumentId id,
        ExchangeId exchangeId,
        Ticker ticker,
        string name,
        AssetType assetType,
        CurrencyCode currency)
    {
        Id = id;
        ExchangeId = exchangeId;
        Ticker = ticker;
        Name = name;
        AssetType = assetType;
        Currency = currency;
        Status = InstrumentStatus.Pending;
        SearchTicker = ToSearchTicker(ticker);
        SearchName = InstrumentSearchText.Normalise(name);
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public InstrumentId Id { get; private set; }

    /// <summary>Gets the venue the instrument is listed on.</summary>
    public ExchangeId ExchangeId { get; private set; }

    /// <summary>Gets the exchange ticker.</summary>
    public Ticker Ticker { get; private set; }

    /// <summary>Gets the security name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the broad asset class.</summary>
    public AssetType AssetType { get; private set; }

    /// <summary>Gets the currency the instrument is quoted in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public InstrumentStatus Status { get; private set; }

    /// <summary>
    /// Gets the industry the instrument is classified under, or
    /// <see langword="null"/> while it is unclassified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable because classification arrives from a source that does not
    /// cover everything an instrument master holds. An index is not in an
    /// industry, a newly imported security has not been mapped yet, and a fund
    /// spans several. Recording "unknown" as a catch-all sector would make
    /// those three indistinguishable and would quietly pollute every sector
    /// aggregate that summed over it.
    /// </para>
    /// <para>
    /// The sector is deliberately not stored alongside it. It is reached
    /// through the industry, so the two levels cannot disagree.
    /// </para>
    /// </remarks>
    public IndustryId? IndustryId { get; private set; }

    /// <summary>Gets the date the instrument began trading, once listed.</summary>
    public DateOnly? ListedOn { get; private set; }

    /// <summary>Gets the date the instrument stopped trading, once delisted.</summary>
    public DateOnly? DelistedOn { get; private set; }

    /// <summary>
    /// Gets the folded form of <see cref="Ticker"/> used by search.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It duplicates the ticker's text, and does so deliberately.
    /// <see cref="Ticker"/> is persisted through a value converter, so the
    /// query layer can compare it for equality but cannot pattern-match it —
    /// and prefix search over tickers is the single most used query in the
    /// terminal. A plain column carrying the same characters is the cheapest
    /// way to keep the canonical property strongly typed without pushing
    /// prefix filtering into application memory.
    /// </para>
    /// <para>
    /// The aggregate maintains it on every path that changes the ticker, so it
    /// cannot drift.
    /// </para>
    /// </remarks>
    public string SearchTicker { get; private set; }

    /// <summary>
    /// Gets the folded form of <see cref="Name"/> used by search.
    /// </summary>
    /// <remarks>
    /// Diacritics removed and case flattened by
    /// <see cref="InstrumentSearchText"/>, so that a query typed without
    /// Vietnamese accents still finds the security.
    /// </remarks>
    public string SearchName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the instrument still occupies its
    /// ticker on its exchange.
    /// </summary>
    /// <remarks>
    /// A delisted instrument releases its ticker for reuse by another issuer,
    /// which is why uniqueness is enforced over active instruments only.
    /// </remarks>
    public bool IsActive => Status != InstrumentStatus.Delisted;

    /// <summary>
    /// Records an instrument that is known but not yet trading.
    /// </summary>
    /// <param name="exchangeId">The venue it will list on.</param>
    /// <param name="ticker">The exchange ticker.</param>
    /// <param name="name">The security name.</param>
    /// <param name="assetType">The broad asset class.</param>
    /// <param name="currency">The quote currency.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>A new instrument in <see cref="InstrumentStatus.Pending"/>.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static Instrument Register(
        ExchangeId exchangeId,
        Ticker ticker,
        string name,
        AssetType assetType,
        CurrencyCode currency,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(currency);

        if (exchangeId.IsEmpty)
        {
            throw new DomainValidationException("An instrument must belong to an exchange.");
        }

        var instrument = new Instrument(
            InstrumentId.New(),
            exchangeId,
            ticker,
            RequireName(name),
            assetType,
            currency);

        instrument.MarkCreated(occurredAtUtc);
        return instrument;
    }

    /// <summary>
    /// Moves a pending instrument into trading.
    /// </summary>
    /// <param name="listedOn">The first trading date.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is not pending.</exception>
    public void List(DateOnly listedOn, DateTimeOffset occurredAtUtc) =>
        ListCore(listedOn, occurredAtUtc);

    /// <summary>
    /// Moves a pending instrument into trading without recording a first
    /// trading date.
    /// </summary>
    /// <remarks>
    /// For securities known to be trading whose listing date has not been
    /// sourced. Provider symbol lists routinely omit it, and refusing to
    /// record that a security trades because its first trading day is unknown
    /// would be a worse answer than recording it with
    /// <see cref="ListedOn"/> left empty. The date can be filled in later
    /// without disturbing identity.
    /// </remarks>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is not pending.</exception>
    public void List(DateTimeOffset occurredAtUtc) => ListCore(null, occurredAtUtc);

    /// <summary>
    /// Halts trading without removing the listing.
    /// </summary>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is not listed.</exception>
    public void Suspend(DateTimeOffset occurredAtUtc)
    {
        if (Status != InstrumentStatus.Listed)
        {
            throw new DomainStateException(
                $"Only a listed instrument can be suspended, but {Ticker} is {Status}.");
        }

        Status = InstrumentStatus.Suspended;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Resumes trading in a suspended instrument.
    /// </summary>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is not suspended.</exception>
    public void Resume(DateTimeOffset occurredAtUtc)
    {
        if (Status != InstrumentStatus.Suspended)
        {
            throw new DomainStateException(
                $"Only a suspended instrument can resume, but {Ticker} is {Status}.");
        }

        Status = InstrumentStatus.Listed;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Permanently removes the instrument from its exchange.
    /// </summary>
    /// <remarks>
    /// Terminal, and never a delete. The record and its history are retained.
    /// </remarks>
    /// <param name="delistedOn">The last trading date.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">
    /// The instrument never traded, or is already delisted.
    /// </exception>
    /// <exception cref="DomainValidationException">
    /// The delisting date precedes the listing date.
    /// </exception>
    public void Delist(DateOnly delistedOn, DateTimeOffset occurredAtUtc)
    {
        if (Status is InstrumentStatus.Pending)
        {
            throw new DomainStateException(
                $"{Ticker} never listed and so cannot be delisted.");
        }

        if (Status is InstrumentStatus.Delisted)
        {
            throw new DomainStateException($"{Ticker} is already delisted.");
        }

        if (ListedOn is { } listedOn && delistedOn < listedOn)
        {
            throw new DomainValidationException(
                $"{Ticker} cannot be delisted on {delistedOn:O}, before it listed on {listedOn:O}.");
        }

        Status = InstrumentStatus.Delisted;
        DelistedOn = delistedOn;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Corrects the security name.
    /// </summary>
    /// <param name="name">The new name.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">The name is invalid.</exception>
    public void Rename(string name, DateTimeOffset occurredAtUtc)
    {
        Name = RequireName(name);
        SearchName = InstrumentSearchText.Normalise(Name);
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Records that the instrument now trades under a different ticker.
    /// </summary>
    /// <remarks>
    /// A ticker change preserves identity. Every price, position and
    /// fundamental already recorded against this instrument stays attached to
    /// it, which is the entire reason the ticker is not the key.
    /// </remarks>
    /// <param name="ticker">The new exchange ticker.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is delisted.</exception>
    public void ChangeTicker(Ticker ticker, DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        if (Status is InstrumentStatus.Delisted)
        {
            throw new DomainStateException(
                $"{Ticker} is delisted and its ticker can no longer change.");
        }

        Ticker = ticker;
        SearchTicker = ToSearchTicker(ticker);
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Records that the instrument has transferred to a different venue.
    /// </summary>
    /// <remarks>
    /// Routine in Vietnam, where issuers progress from UPCOM to HNX to HOSE.
    /// Identity is preserved across the move.
    /// </remarks>
    /// <param name="exchangeId">The venue the instrument now lists on.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The instrument is delisted.</exception>
    /// <exception cref="DomainValidationException">The exchange is unassigned.</exception>
    public void TransferToExchange(ExchangeId exchangeId, DateTimeOffset occurredAtUtc)
    {
        if (exchangeId.IsEmpty)
        {
            throw new DomainValidationException("An instrument must belong to an exchange.");
        }

        if (Status is InstrumentStatus.Delisted)
        {
            throw new DomainStateException(
                $"{Ticker} is delisted and can no longer transfer exchange.");
        }

        ExchangeId = exchangeId;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Classifies an instrument whose asset type was not known at import.
    /// </summary>
    /// <param name="assetType">The resolved asset class.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">
    /// The supplied classification is <see cref="AssetType.Unspecified"/>.
    /// </exception>
    public void Reclassify(AssetType assetType, DateTimeOffset occurredAtUtc)
    {
        if (assetType == AssetType.Unspecified)
        {
            throw new DomainValidationException(
                $"{Ticker} cannot be reclassified back to unspecified.");
        }

        AssetType = assetType;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Classifies the instrument under an industry.
    /// </summary>
    /// <remarks>
    /// Idempotent and repeatable. A provider reclassifying a company is
    /// ordinary, and the aggregate records the current answer rather than
    /// refusing the change; classification is descriptive metadata and carries
    /// none of the identity guarantees the ticker and the lifecycle do.
    /// </remarks>
    /// <param name="industryId">The industry to classify it under.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">The industry is unassigned.</exception>
    public void AssignIndustry(IndustryId industryId, DateTimeOffset occurredAtUtc)
    {
        if (industryId.IsEmpty)
        {
            throw new DomainValidationException(
                $"{Ticker} cannot be classified under an unassigned industry.");
        }

        IndustryId = industryId;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Removes the instrument's classification.
    /// </summary>
    /// <remarks>
    /// For a mapping discovered to be wrong. Leaving a known-bad industry in
    /// place would keep the security inside a peer group it does not belong
    /// to, which is worse than reporting it as unclassified.
    /// </remarks>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    public void ClearIndustry(DateTimeOffset occurredAtUtc)
    {
        IndustryId = null;
        MarkUpdated(occurredAtUtc);
    }

    private void ListCore(DateOnly? listedOn, DateTimeOffset occurredAtUtc)
    {
        if (Status != InstrumentStatus.Pending)
        {
            throw new DomainStateException(
                $"Only a pending instrument can be listed, but {Ticker} is {Status}.");
        }

        Status = InstrumentStatus.Listed;
        ListedOn = listedOn;
        MarkUpdated(occurredAtUtc);
    }

    // A ticker is already upper-case ASCII alphanumerics by construction, so
    // folding it is a formality. It is routed through the same normaliser as
    // the name anyway, so that both search columns are guaranteed to have been
    // produced by one rule.
    private static string ToSearchTicker(Ticker ticker) =>
        InstrumentSearchText.Normalise(ticker.Value);

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("An instrument name is required.");
        }

        var trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? throw new DomainValidationException(
                $"An instrument name may not exceed {MaxNameLength} characters.")
            : trimmed;
    }
}
