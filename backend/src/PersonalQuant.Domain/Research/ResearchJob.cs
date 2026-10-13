using System.Text.RegularExpressions;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Research;

/// <summary>Identifies a research job.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ResearchJobId(Guid Value)
{
    /// <summary>Gets a value indicating whether this is the unassigned identifier.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Creates a new identifier, ordered by creation time.</summary>
    /// <returns>The identifier.</returns>
    public static ResearchJobId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Where a research job is in its life.</summary>
/// <remarks>
/// Stored as lowercase text rather than an integer: the worker that moves a
/// job through these states is Python, and a column that reads
/// <c>running</c> is one contract, where a column that reads <c>2</c> is two.
/// </remarks>
public enum ResearchJobStatus
{
    /// <summary>Submitted and waiting for a worker.</summary>
    Queued = 1,

    /// <summary>Claimed by a worker that holds its lease.</summary>
    Running = 2,

    /// <summary>Finished, with a result.</summary>
    Succeeded = 3,

    /// <summary>Finished, with an error.</summary>
    Failed = 4,
}

/// <summary>
/// A request for research work, queued for the quant worker.
/// </summary>
/// <remarks>
/// <para>
/// The backend owns the table and creates jobs; the Python worker claims them
/// with a lease and records how they ended (ADR-016). That is why this type has
/// a way in and no way through: <see cref="Submit"/> is the only transition
/// written in .NET. Claiming, completing and failing are the worker's, done
/// in SQL under the <c>quant_research</c> role, and the state the worker
/// leaves is read back here as it stands.
/// </para>
/// <para>
/// A job is a table row rather than a message because it is transactional
/// with the data it refers to, survives a restart of either side, and behaves
/// identically on a laptop and in Compose. A crashed worker's job becomes
/// claimable again when its lease expires, so the run is lost but the request
/// is not.
/// </para>
/// </remarks>
public sealed partial class ResearchJob
{
    /// <summary>The longest kind name accepted.</summary>
    public const int MaxKindLength = 64;

    /// <summary>The longest payload accepted, in characters.</summary>
    public const int MaxPayloadLength = 64 * 1024;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private ResearchJob()
    {
        Kind = null!;
        Payload = null!;
    }

    private ResearchJob(ResearchJobId id, string kind, string payload, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Kind = kind;
        Payload = payload;
        Status = ResearchJobStatus.Queued;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Gets the job's identifier.</summary>
    public ResearchJobId Id { get; private set; }

    /// <summary>Gets what kind of work this is, which selects the worker's handler.</summary>
    public string Kind { get; private set; }

    /// <summary>Gets the handler's parameters, as a JSON object.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets where the job is in its life.</summary>
    public ResearchJobStatus Status { get; private set; }

    /// <summary>Gets the worker that holds or last held the job, if any.</summary>
    public string? ClaimedBy { get; private set; }

    /// <summary>Gets when the job was last claimed.</summary>
    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    /// <summary>Gets when the current claim lapses unless the job has finished.</summary>
    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    /// <summary>Gets how many times the job has been claimed.</summary>
    /// <remarks>
    /// More than one means a worker died holding it. The worker gives up after
    /// a bounded number, so a job that crashes its worker every time cannot
    /// keep the queue busy forever.
    /// </remarks>
    public int Attempts { get; private set; }

    /// <summary>Gets when the job was submitted.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Gets when the job finished, either way.</summary>
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>Gets why the job failed, if it did.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the handler's result, as a JSON object, if it succeeded.</summary>
    /// <remarks>
    /// Held here until the experiment store (U9) gives a run a record of its
    /// own; until then this is the only place a result lives.
    /// </remarks>
    public string? Result { get; private set; }

    /// <summary>
    /// Queues a job.
    /// </summary>
    /// <param name="kind">The kind of work: lowercase letters, digits and underscores.</param>
    /// <param name="payload">The parameters, a JSON object; validated by the caller.</param>
    /// <param name="createdAtUtc">When it was submitted.</param>
    /// <returns>The queued job.</returns>
    /// <exception cref="DomainValidationException">A value is not usable.</exception>
    public static ResearchJob Submit(string kind, string payload, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrEmpty(kind) || kind.Length > MaxKindLength || !KindPattern().IsMatch(kind))
        {
            throw new DomainValidationException(
                $"'{kind}' is not a job kind: lowercase letters, digits and underscores, "
                    + $"starting with a letter, at most {MaxKindLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadLength)
        {
            throw new DomainValidationException(
                $"A job's payload must be present and at most {MaxPayloadLength} characters.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Timestamps must be UTC, but the offset was {createdAtUtc.Offset}.");
        }

        return new ResearchJob(ResearchJobId.New(), kind, payload, createdAtUtc);
    }

    // \z, not $: in .NET $ also matches before a final newline, so a kind
    // ending in one would be queued under a name no handler has.
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KindPattern();
}
