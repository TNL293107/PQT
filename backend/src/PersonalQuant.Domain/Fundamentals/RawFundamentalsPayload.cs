using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// A filing exactly as a source supplied it, kept beside the report and lines
/// derived from it.
/// </summary>
/// <remarks>
/// <para>
/// Retained for the reason <see cref="RawMarketDataBatch"/> is: every
/// normaliser has bugs found later, a column read into the wrong period for
/// instance, and without the original the only remedy is to read the source
/// again, which for a disclosure portal that may be redesigned is not
/// guaranteed.
/// </para>
/// <para>
/// One row per distinct payload of a filing. Reading a filing again with
/// nothing changed adds nothing; a changed payload is retained beside the old
/// one, and the checksum is how the two are told apart.
/// </para>
/// </remarks>
public sealed class RawFundamentalsPayload
{
    /// <summary>Longest payload retained.</summary>
    public const int MaxPayloadLength = 4 * 1024 * 1024;

    /// <summary>Longest accepted content type.</summary>
    public const int MaxContentTypeLength = 128;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private RawFundamentalsPayload()
    {
        Source = null!;
        SourceReference = null!;
        Payload = null!;
        ContentType = null!;
        Checksum = null!;
    }

    private RawFundamentalsPayload(
        SourceCode source,
        string sourceReference,
        InstrumentId instrumentId,
        string payload,
        string contentType,
        string checksum,
        int sizeBytes,
        DateTimeOffset fetchedAtUtc)
    {
        Id = Guid.CreateVersion7();
        Source = source;
        SourceReference = sourceReference;
        InstrumentId = instrumentId;
        Payload = payload;
        ContentType = contentType;
        Checksum = checksum;
        SizeBytes = sizeBytes;
        FetchedAtUtc = fetchedAtUtc;
    }

    /// <summary>Gets the payload's identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the source it came from.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the source's key for the filing.</summary>
    public string SourceReference { get; private set; }

    /// <summary>Gets the issuer's instrument.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the payload, verbatim.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets the payload's media type.</summary>
    public string ContentType { get; private set; }

    /// <summary>Gets the SHA-256 of the payload's UTF-8 bytes, lower-case hex.</summary>
    public string Checksum { get; private set; }

    /// <summary>Gets the payload's size in UTF-8 bytes.</summary>
    public int SizeBytes { get; private set; }

    /// <summary>Gets when the filing was read from the source.</summary>
    public DateTimeOffset FetchedAtUtc { get; private set; }

    /// <summary>Computes the checksum a payload would be retained under.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The lower-case hex SHA-256 of its UTF-8 bytes.</returns>
    public static string ChecksumOf(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>Retains a payload.</summary>
    /// <param name="source">The source.</param>
    /// <param name="sourceReference">The source's key for the filing.</param>
    /// <param name="instrumentId">The issuer's instrument.</param>
    /// <param name="payload">The payload, verbatim.</param>
    /// <param name="contentType">Its media type.</param>
    /// <param name="fetchedAtUtc">When it was read.</param>
    /// <returns>The retained payload.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static RawFundamentalsPayload Retain(
        SourceCode source,
        string sourceReference,
        InstrumentId instrumentId,
        string payload,
        string contentType,
        DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A raw payload must belong to an instrument.");
        }

        var reference = sourceReference?.Trim();

        if (string.IsNullOrEmpty(reference) || reference.Length > FinancialReport.MaxSourceReferenceLength)
        {
            throw new DomainValidationException(
                $"A raw payload needs a source reference of at most {FinancialReport.MaxSourceReferenceLength} characters.");
        }

        if (payload is null)
        {
            throw new DomainValidationException("A raw payload must carry the source's document.");
        }

        if (payload.Length > MaxPayloadLength)
        {
            throw new DomainValidationException(
                $"A raw payload may not exceed {MaxPayloadLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        var type = contentType?.Trim();

        if (string.IsNullOrEmpty(type) || type.Length > MaxContentTypeLength)
        {
            throw new DomainValidationException(
                $"A raw payload must declare a content type of at most {MaxContentTypeLength} characters.");
        }

        if (fetchedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException($"Timestamps must be UTC, but the offset was {fetchedAtUtc.Offset}.");
        }

        return new RawFundamentalsPayload(
            source,
            reference,
            instrumentId,
            payload,
            type,
            ChecksumOf(payload),
            Encoding.UTF8.GetByteCount(payload),
            fetchedAtUtc);
    }
}
