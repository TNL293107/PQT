namespace PersonalQuant.Domain.Universes;

/// <summary>
/// What kind of set a universe is.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is about where membership comes from and therefore how far
/// it can be trusted, which is why it is stored rather than inferred from the
/// code. An index membership is published by the index owner on a review
/// calendar; an exchange listing is derived from the instrument master this
/// system already keeps; a curated list is somebody's judgement.
/// </para>
/// <para>
/// Values are explicit because they are persisted and outlive this
/// declaration's order.
/// </para>
/// </remarks>
public enum UniverseKind
{
    /// <summary>
    /// Constituents of a published index, such as VN30.
    /// </summary>
    /// <remarks>
    /// Membership is a decision made by the index owner at a review, announced
    /// before it takes effect. It is the kind most worth recording historically
    /// and the hardest to source, which is exactly why an unsourced stretch
    /// must not read as an empty index.
    /// </remarks>
    Index = 1,

    /// <summary>
    /// Everything listed on a venue, such as every HOSE security.
    /// </summary>
    /// <remarks>
    /// Derivable from the instrument master's listing lifecycle rather than
    /// from an external source, and recorded as a universe anyway so that a
    /// dataset can name it the same way it names an index.
    /// </remarks>
    Exchange = 2,

    /// <summary>
    /// A list this system's operator maintains.
    /// </summary>
    /// <remarks>
    /// Research watchlists and hand-picked baskets. Its provenance is an
    /// operator, and a backtest run against one is making a claim about a set
    /// somebody chose — which is worth being unable to confuse with an index.
    /// </remarks>
    Custom = 3,
}

/// <summary>
/// Facts about a <see cref="UniverseKind"/> that the model switches on.
/// </summary>
public static class UniverseKinds
{
    /// <summary>
    /// Reports whether a kind is one of the declared values.
    /// </summary>
    /// <param name="kind">The value to check.</param>
    /// <returns><see langword="true"/> when the kind is usable.</returns>
    public static bool IsDeclared(this UniverseKind kind) =>
        kind is >= UniverseKind.Index and <= UniverseKind.Custom;
}
