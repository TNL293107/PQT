using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// A trading venue on which instruments are listed.
/// </summary>
/// <remarks>
/// <para>
/// Still thin, and grown only where something consumes it. The daily price
/// limit arrived with data-quality validation, which is the first thing that
/// needed to know what move a venue permits; session times have not, because
/// nothing reads them until the backtester simulates an auction.
/// </para>
/// <para>
/// Identity is a surrogate <see cref="ExchangeId"/> rather than the code, so
/// that a venue rename or re-code does not orphan every instrument pointing at
/// it.
/// </para>
/// </remarks>
public sealed class Exchange : AuditableEntity
{
    /// <summary>Longest permitted exchange name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Length of an ISO 10383 Market Identifier Code.</summary>
    public const int MicLength = 4;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Exchange()
    {
        Code = null!;
        Name = null!;
        TimeZoneId = null!;
    }

    private Exchange(
        ExchangeId id,
        ExchangeCode code,
        string name,
        string timeZoneId,
        string? mic,
        PriceLimit? dailyPriceLimit)
    {
        Id = id;
        Code = code;
        Name = name;
        TimeZoneId = timeZoneId;
        Mic = mic;
        DailyPriceLimit = dailyPriceLimit;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public ExchangeId Id { get; private set; }

    /// <summary>Gets the operating code, such as <c>HOSE</c>.</summary>
    public ExchangeCode Code { get; private set; }

    /// <summary>Gets the full venue name.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the IANA time zone the venue trades in, such as
    /// <c>Asia/Ho_Chi_Minh</c>.
    /// </summary>
    /// <remarks>
    /// Every timestamp in the system is stored in UTC. This records the zone
    /// needed to reconstruct a trading day boundary, which is not derivable
    /// from a UTC instant alone.
    /// </remarks>
    public string TimeZoneId { get; private set; }

    /// <summary>
    /// Gets the ISO 10383 Market Identifier Code, when one is known.
    /// </summary>
    /// <remarks>
    /// Optional because MIC coverage for Vietnamese venues varies by provider.
    /// It is never used as identity.
    /// </remarks>
    public string? Mic { get; private set; }

    /// <summary>
    /// Gets the furthest a security may move from its previous close in one
    /// session, when the venue publishes a limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable, and left null rather than defaulted. A venue whose limit has
    /// not been recorded is not a venue with no limit, and guessing one would
    /// either raise false anomalies or hide real ones. The cross-session check
    /// is skipped where it is absent, and the absence is visible.
    /// </para>
    /// <para>
    /// An exchange-level value. It is a property of the venue's rules rather
    /// than of any security, and where an instrument is exempt — an index is
    /// calculated, not traded — the exemption belongs to the instrument's
    /// asset class.
    /// </para>
    /// </remarks>
    public PriceLimit? DailyPriceLimit { get; private set; }

    /// <summary>
    /// Registers a trading venue.
    /// </summary>
    /// <param name="code">The operating code.</param>
    /// <param name="name">The full venue name.</param>
    /// <param name="timeZoneId">The IANA time zone identifier.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <param name="mic">The ISO 10383 MIC, when known.</param>
    /// <param name="dailyPriceLimit">The published daily price limit, when known.</param>
    /// <returns>The new exchange.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static Exchange Register(
        ExchangeCode code,
        string name,
        string timeZoneId,
        DateTimeOffset occurredAtUtc,
        string? mic = null,
        PriceLimit? dailyPriceLimit = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        var exchange = new Exchange(
            ExchangeId.New(),
            code,
            RequireName(name),
            RequireTimeZone(timeZoneId),
            NormaliseMic(mic),
            dailyPriceLimit);

        exchange.MarkCreated(occurredAtUtc);
        return exchange;
    }

    /// <summary>
    /// Records the venue's published daily price limit.
    /// </summary>
    /// <remarks>
    /// Separate from registration because the limit is published market
    /// structure that a venue can revise, while the venue's identity is not.
    /// A revision applies from the moment it is recorded; bars already
    /// validated under the old limit carry the validation version that says
    /// so.
    /// </remarks>
    /// <param name="dailyPriceLimit">The limit, or null to record that none is known.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    public void SetDailyPriceLimit(PriceLimit? dailyPriceLimit, DateTimeOffset occurredAtUtc)
    {
        DailyPriceLimit = dailyPriceLimit;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Renames the venue.
    /// </summary>
    /// <param name="name">The new name.</param>
    /// <param name="occurredAtUtc">The instant the change takes effect.</param>
    /// <exception cref="DomainValidationException">The name is invalid.</exception>
    public void Rename(string name, DateTimeOffset occurredAtUtc)
    {
        Name = RequireName(name);
        MarkUpdated(occurredAtUtc);
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("An exchange name is required.");
        }

        var trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? throw new DomainValidationException(
                $"An exchange name may not exceed {MaxNameLength} characters.")
            : trimmed;
    }

    private static string RequireTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new DomainValidationException("An exchange time zone is required.");
        }

        var trimmed = timeZoneId.Trim();

        // Validated against the platform's database so an unusable identifier
        // cannot be stored and only discovered when a trading day is computed.
        return TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out _)
            ? trimmed
            : throw new DomainValidationException(
                $"'{trimmed}' is not a time zone this system recognises.");
    }

    private static string? NormaliseMic(string? mic)
    {
        if (string.IsNullOrWhiteSpace(mic))
        {
            return null;
        }

        var normalised = mic.Trim().ToUpperInvariant();

        if (normalised.Length != MicLength || !normalised.All(char.IsAsciiLetterOrDigit))
        {
            throw new DomainValidationException(
                $"'{mic}' is not a valid ISO 10383 MIC.");
        }

        return normalised;
    }
}
