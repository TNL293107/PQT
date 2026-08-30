using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.Universes;

/// <summary>
/// A named set of securities whose membership changes over time.
/// </summary>
/// <remarks>
/// <para>
/// The half of survivorship the instrument master does not already solve. The
/// master never deletes, so a delisted security keeps its identity and its
/// prices; what it cannot say is <em>when that security belonged to the set a
/// strategy was choosing from</em>. Selecting today's VN30 and running it over
/// 2018 picks the thirty that survived to today, which is a portfolio nobody
/// could have held.
/// </para>
/// <para>
/// The universe carries a coverage claim, and the memberships carry the facts.
/// Keeping the two apart is what lets an as-of read answer <em>unknown</em>
/// instead of <em>empty</em>, and that distinction is the reason this type is
/// not simply a code on a membership row.
/// </para>
/// </remarks>
public sealed class Universe : AuditableEntity
{
    /// <summary>Longest permitted universe name.</summary>
    public const int MaxNameLength = 200;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private Universe()
    {
        Code = null!;
        Name = null!;
        Source = null!;
    }

    private Universe(
        UniverseId id,
        UniverseCode code,
        string name,
        UniverseKind kind,
        SourceCode source)
    {
        Id = id;
        Code = code;
        Name = name;
        Kind = kind;
        Source = source;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public UniverseId Id { get; private set; }

    /// <summary>Gets the short code, such as <c>VN30</c>.</summary>
    public UniverseCode Code { get; private set; }

    /// <summary>Gets the full name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets what kind of set this is.</summary>
    public UniverseKind Kind { get; private set; }

    /// <summary>Gets where the membership history came from.</summary>
    /// <remarks>
    /// Provenance of the history, not of the index itself. Two sources for the
    /// same index will disagree about at least one review, and a set whose
    /// origin is unrecorded cannot be reconciled against either.
    /// </remarks>
    public SourceCode Source { get; private set; }

    /// <summary>
    /// Gets the span whose membership is claimed to be known, or
    /// <see langword="null"/> when nothing is claimed.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is the state every universe starts in and is not
    /// a defect: it says no membership history has been sourced. It must never
    /// be read as an empty set, which is why every read that lands outside the
    /// claim reports that it does not know rather than returning nothing.
    /// </remarks>
    public MembershipCoverage? Coverage { get; private set; }

    /// <summary>
    /// Defines a universe.
    /// </summary>
    /// <param name="id">The identifier to issue it under.</param>
    /// <param name="code">The short code.</param>
    /// <param name="name">The full name.</param>
    /// <param name="kind">What kind of set it is.</param>
    /// <param name="source">Where its membership history comes from.</param>
    /// <param name="occurredAtUtc">The instant it was defined.</param>
    /// <returns>The universe, claiming to know nothing yet.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static Universe Define(
        UniverseId id,
        UniverseCode code,
        string name,
        UniverseKind kind,
        SourceCode source,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(source);

        if (id.IsEmpty)
        {
            throw new DomainValidationException("A universe must have an identifier.");
        }

        if (!kind.IsDeclared())
        {
            throw new DomainValidationException($"'{kind}' is not a universe kind this system records.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("A universe must be named.");
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainValidationException(
                $"A universe name may be at most {MaxNameLength} characters.");
        }

        var universe = new Universe(id, code, trimmed, kind, source);
        universe.MarkCreated(occurredAtUtc);
        return universe;
    }

    /// <summary>
    /// States the span whose membership this universe claims to know.
    /// </summary>
    /// <remarks>
    /// Replaces any previous claim rather than accumulating spans. Sourcing
    /// older history widens the claim, and a single span is enough for the one
    /// question the claim answers — <em>is this date known?</em> — while a set
    /// of disjoint spans would turn every read into a range scan for a case
    /// nothing yet has.
    /// </remarks>
    /// <param name="coverage">The span now claimed.</param>
    /// <param name="source">Where that history came from.</param>
    /// <param name="occurredAtUtc">The instant the claim was made.</param>
    /// <exception cref="DomainValidationException">The instant is not UTC or predates creation.</exception>
    public void DeclareCoverage(
        MembershipCoverage coverage,
        SourceCode source,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(source);

        Coverage = coverage;
        Source = source;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Reports whether this universe claims to know its membership on a date.
    /// </summary>
    /// <param name="asOf">The date to test.</param>
    /// <returns>
    /// <see langword="true"/> only when a claim exists and covers the date. A
    /// universe that claims nothing knows nothing.
    /// </returns>
    public bool Knows(DateOnly asOf) => Coverage?.Covers(asOf) ?? false;
}
