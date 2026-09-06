namespace PersonalQuant.Domain.CorporateActions;

/// <summary>
/// What an adjusted read does with an action whose announcement date is not
/// known.
/// </summary>
/// <remarks>
/// <para>
/// A point-in-time read applies the actions the market had been told about by
/// the observation instant. When a source did not supply
/// <see cref="CorporateAction.AnnouncedOn"/> there is nothing to compare
/// against, and the two defensible answers are opposites: leave it out and
/// under-adjust, or put it in and risk look-ahead.
/// </para>
/// <para>
/// The choice is the caller's and is never made silently. "We do not know when
/// this was announced" must not become "we always knew" merely because a
/// column was null, and a series that quietly picked one reading would make
/// the two indistinguishable in the answer.
/// </para>
/// <para>
/// Values are explicit because they cross the API boundary and are recorded
/// alongside exported datasets.
/// </para>
/// </remarks>
public enum AnnouncementPolicy
{
    /// <summary>
    /// An action with no announcement date is excluded from an as-of read.
    /// </summary>
    /// <remarks>
    /// The setting for backtests and dataset export. It can only
    /// <em>under</em>-adjust, and under-adjustment shows up as a visible
    /// discontinuity in the series; look-ahead shows up as a better result,
    /// which nobody investigates.
    /// </remarks>
    Strict = 1,

    /// <summary>
    /// An action with no announcement date is applied regardless of the as-of.
    /// </summary>
    /// <remarks>
    /// The setting for charting and terminal display, where the question is
    /// "what does this series look like" rather than "what could a strategy
    /// have known". Most Vietnamese action records carry an ex-date and
    /// nothing else, so strict charting would show splits as crashes.
    /// </remarks>
    Permissive = 2,
}

/// <summary>Conveniences for <see cref="AnnouncementPolicy"/>.</summary>
public static class AnnouncementPolicyExtensions
{
    /// <summary>
    /// The policy an adjusted read uses when the caller does not name one.
    /// </summary>
    /// <remarks>
    /// Permissive, because the reachable callers are charts and the terminal.
    /// The strict reading is what a backtest and
    /// <see cref="AnnouncementPolicy.Strict"/> export ask for explicitly, and
    /// asking explicitly is the point: a caller who has thought about
    /// look-ahead says so.
    /// </remarks>
    public const AnnouncementPolicy Default = AnnouncementPolicy.Permissive;

    /// <summary>Reports whether the value is one this system declares.</summary>
    /// <param name="policy">The value to check.</param>
    /// <returns><see langword="true"/> when declared.</returns>
    public static bool IsDeclared(this AnnouncementPolicy policy) =>
        policy is AnnouncementPolicy.Strict or AnnouncementPolicy.Permissive;

    /// <summary>
    /// Reports whether an action was known to the market at an observation
    /// instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison is by day and inclusive of the announcement day itself.
    /// An action announced on the observation date was public on that date,
    /// and excluding it would misstate what a strategy trading that session
    /// could read.
    /// </para>
    /// <para>
    /// A <see langword="null"/> <paramref name="knownAsOf"/> is a read of the
    /// current series rather than a past one, and everything this system has
    /// recorded is current knowledge. The policy has nothing to decide.
    /// </para>
    /// </remarks>
    /// <param name="policy">How to treat an unknown announcement date.</param>
    /// <param name="announcedOn">When the action became public, when known.</param>
    /// <param name="knownAsOf">The observation instant, or null for now.</param>
    /// <returns><see langword="true"/> when the action may be applied.</returns>
    public static bool Admits(
        this AnnouncementPolicy policy,
        DateOnly? announcedOn,
        DateTimeOffset? knownAsOf)
    {
        if (knownAsOf is not { } cut)
        {
            return true;
        }

        if (announcedOn is not { } announced)
        {
            return policy == AnnouncementPolicy.Permissive;
        }

        return announced <= DateOnly.FromDateTime(cut.UtcDateTime);
    }
}
