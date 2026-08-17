using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>The identifier of a <see cref="RawMarketDataBatch"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct RawBatchId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static RawBatchId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A provider's response exactly as it arrived, kept beside the canonical
/// bars derived from it.
/// </summary>
/// <remarks>
/// <para>
/// Retained so that re-normalising from raw is always possible. Every
/// normaliser has bugs that are only found later — a mis-parsed timestamp, a
/// column read in the wrong order, a locale-dependent decimal separator — and
/// without the original payload the only remedy is to re-fetch, which for
/// historical data is often no longer possible and is never free.
/// </para>
/// <para>
/// The payload is stored verbatim, not pretty-printed or re-serialised.
/// Re-serialising would make the checksum describe this system's rendering of
/// the response rather than the response, and the point of the checksum is to
/// answer "did the provider send us something different this time?".
/// </para>
/// </remarks>
public sealed class RawMarketDataBatch
{
    /// <summary>Longest payload the system will retain.</summary>
    /// <remarks>
    /// A bound, not a target. A response larger than this is a range request
    /// that should have been chunked, and storing it would put multi-megabyte
    /// text into a table read on every re-normalisation.
    /// </remarks>
    public const int MaxPayloadLength = 4 * 1024 * 1024;

    /// <summary>Longest accepted content type.</summary>
    public const int MaxContentTypeLength = 128;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private RawMarketDataBatch()
    {
        Source = null!;
        Payload = null!;
        ContentType = null!;
        Checksum = null!;
    }

    private RawMarketDataBatch(
        RawBatchId id,
        SourceCode source,
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset requestedFromUtc,
        DateTimeOffset requestedToUtc,
        string payload,
        string contentType,
        string checksum,
        DateTimeOffset fetchedAtUtc)
    {
        Id = id;
        Source = source;
        InstrumentId = instrumentId;
        Interval = interval;
        RequestedFromUtc = requestedFromUtc;
        RequestedToUtc = requestedToUtc;
        Payload = payload;
        ContentType = contentType;
        Checksum = checksum;
        FetchedAtUtc = fetchedAtUtc;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public RawBatchId Id { get; private set; }

    /// <summary>Gets the provider the payload came from.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the instrument the request was for.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the request was for.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>Gets the inclusive start of the requested range.</summary>
    public DateTimeOffset RequestedFromUtc { get; private set; }

    /// <summary>Gets the exclusive end of the requested range.</summary>
    public DateTimeOffset RequestedToUtc { get; private set; }

    /// <summary>Gets the provider's response, unmodified.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets the media type the provider declared, such as <c>text/csv</c>.</summary>
    public string ContentType { get; private set; }

    /// <summary>
    /// Gets the lower-case hexadecimal SHA-256 of the payload.
    /// </summary>
    /// <remarks>
    /// Two fetches of the same range that produce the same checksum need not
    /// be normalised twice, and two that differ are a restatement worth
    /// noticing. It is an integrity and change-detection aid, not a security
    /// control.
    /// </remarks>
    public string Checksum { get; private set; }

    /// <summary>Gets the instant the response was received, in UTC.</summary>
    public DateTimeOffset FetchedAtUtc { get; private set; }

    /// <summary>Gets how large the payload is, in UTF-8 bytes.</summary>
    public int SizeBytes { get; private set; }

    /// <summary>
    /// Retains a provider response.
    /// </summary>
    /// <param name="source">The provider it came from.</param>
    /// <param name="instrumentId">The instrument requested.</param>
    /// <param name="interval">The resolution requested.</param>
    /// <param name="requestedFromUtc">The inclusive start of the requested range.</param>
    /// <param name="requestedToUtc">The exclusive end of the requested range.</param>
    /// <param name="payload">The response, verbatim.</param>
    /// <param name="contentType">The declared media type.</param>
    /// <param name="fetchedAtUtc">The instant it was received.</param>
    /// <returns>The retained batch.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static RawMarketDataBatch Retain(
        SourceCode source,
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset requestedFromUtc,
        DateTimeOffset requestedToUtc,
        string payload,
        string contentType,
        DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A raw batch must belong to an instrument.");
        }

        if (!interval.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{interval}' is not a bar resolution this system records.");
        }

        if (requestedToUtc <= requestedFromUtc)
        {
            throw new DomainValidationException(
                $"A requested range must end after it starts, but {requestedToUtc:O} does not follow {requestedFromUtc:O}.");
        }

        if (payload is null)
        {
            throw new DomainValidationException("A raw batch must carry the provider's response.");
        }

        if (payload.Length > MaxPayloadLength)
        {
            throw new DomainValidationException(
                $"A raw payload may not exceed {MaxPayloadLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        var bytes = Encoding.UTF8.GetBytes(payload);

        var batch = new RawMarketDataBatch(
            RawBatchId.New(),
            source,
            instrumentId,
            interval,
            requestedFromUtc,
            requestedToUtc,
            payload,
            RequireContentType(contentType),
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            fetchedAtUtc)
        {
            SizeBytes = bytes.Length,
        };

        return batch;
    }

    private static string RequireContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainValidationException("A raw batch must declare a content type.");
        }

        var trimmed = contentType.Trim();

        return trimmed.Length > MaxContentTypeLength
            ? throw new DomainValidationException(
                $"A content type may not exceed {MaxContentTypeLength} characters.")
            : trimmed;
    }
}
