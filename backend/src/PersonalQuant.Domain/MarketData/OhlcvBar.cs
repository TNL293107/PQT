using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// One period of trading in one instrument: open, high, low, close, volume.
/// </summary>
/// <remarks>
/// <para>
/// The unit every later phase computes on. Its identity is the instrument,
/// the interval and the opening instant together — there is no surrogate key,
/// because those three <em>are</em> the identity, and giving a time series a
/// generated key is how the same period ends up stored twice.
/// </para>
/// <para>
/// The structural invariants are enforced here rather than by whatever
/// happens to be writing. A bar whose high is below its close is not a bar; it
/// is a parsing error, a column swapped at the provider, or a corrupt feed,
/// and every one of those is invisible once the row is stored. Phase 3 adds
/// the checks that need context — a gap against the previous close, a missing
/// session, a quality score. These are the ones a single row can answer on its
/// own.
/// </para>
/// <para>
/// A bar is immutable except through <see cref="Revise"/>. Providers do
/// restate history, and pretending otherwise would mean either refusing a
/// correction or overwriting silently; instead the revision is counted and
/// stamped, so a series that has been restated can be told from one that has
/// not.
/// </para>
/// </remarks>
public sealed class OhlcvBar
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private OhlcvBar() => Source = null!;

    private OhlcvBar(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset openedAtUtc,
        Price open,
        Price high,
        Price low,
        Price close,
        long volume,
        decimal? turnover,
        SourceCode source,
        DateTimeOffset ingestedAtUtc)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        OpenedAtUtc = openedAtUtc;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Turnover = turnover;
        Source = source;
        IngestedAtUtc = ingestedAtUtc;
        Revision = 0;
        TransformationVersion = DataRules.TransformationVersion;
        ValidationVersion = DataRules.Unvalidated;
    }

    /// <summary>Gets the instrument the bar belongs to.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the bar was aggregated at.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>
    /// Gets the instant the period opened, in UTC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The opening edge, never the closing one. Both conventions exist in the
    /// wild and they differ by exactly one interval, which is the single
    /// easiest way to shift a whole series by one period and never notice.
    /// </para>
    /// <para>
    /// A daily bar opens at midnight UTC on the session's trading date. That
    /// works because every venue this system covers trades at UTC+7, so a
    /// Vietnamese session lies wholly inside one UTC day and the local trading
    /// date and the UTC date are the same. It is a convention, not a law: a
    /// venue whose session crosses a UTC midnight would need the trading date
    /// carried separately, and this is the assumption to revisit first when
    /// one is added.
    /// </para>
    /// </remarks>
    public DateTimeOffset OpenedAtUtc { get; private set; }

    /// <summary>Gets the first traded price of the period.</summary>
    public Price Open { get; private set; }

    /// <summary>Gets the highest traded price of the period.</summary>
    public Price High { get; private set; }

    /// <summary>Gets the lowest traded price of the period.</summary>
    public Price Low { get; private set; }

    /// <summary>Gets the last traded price of the period.</summary>
    public Price Close { get; private set; }

    /// <summary>
    /// Gets the number of units traded during the period.
    /// </summary>
    /// <remarks>
    /// Zero is legitimate and common: an illiquid UPCOM security can go a
    /// whole session without a trade, and the exchange still publishes the
    /// period. Negative is not, and is rejected.
    /// </remarks>
    public long Volume { get; private set; }

    /// <summary>
    /// Gets the cash value traded during the period, when the source reports
    /// it.
    /// </summary>
    /// <remarks>
    /// Vietnamese venues publish it and it is not reconstructible from the
    /// other fields — volume times any single price is an approximation, not
    /// the traded value. Nullable because not every source carries it, and a
    /// computed stand-in would be indistinguishable from a reported one.
    /// </remarks>
    public decimal? Turnover { get; private set; }

    /// <summary>Gets where the bar came from.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>Gets the instant the bar first entered the system, in UTC.</summary>
    public DateTimeOffset IngestedAtUtc { get; private set; }

    /// <summary>
    /// Gets the instant the bar was last restated, or <see langword="null"/>
    /// when it never has been.
    /// </summary>
    public DateTimeOffset? RevisedAtUtc { get; private set; }

    /// <summary>
    /// Gets how many times the source has restated this period.
    /// </summary>
    /// <remarks>
    /// Zero for a bar that has never changed. A series carrying revisions is
    /// not wrong, but it is a different thing from one that has not, and a
    /// backtest that cannot tell them apart cannot explain why its results
    /// moved.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>
    /// Gets the version of the normalisation rules that produced the bar.
    /// </summary>
    /// <remarks>
    /// Stamped when the bar is recorded and again when it is restated, because
    /// a restatement runs through the same normaliser.
    /// </remarks>
    public int TransformationVersion { get; private set; }

    /// <summary>
    /// Gets the version of the quality rules the bar has passed, or
    /// <see cref="DataRules.Unvalidated"/> when it has passed none.
    /// </summary>
    /// <remarks>
    /// This is what makes a rule change tractable: after bumping the rules, the
    /// bars still carrying the old version are exactly the ones that need
    /// re-checking, and they can be found with a query rather than by
    /// re-validating the whole series.
    /// </remarks>
    public int ValidationVersion { get; private set; }

    /// <summary>Gets the instant the period closed, in UTC.</summary>
    public DateTimeOffset ClosedAtUtc => OpenedAtUtc + Interval.ToDuration();

    /// <summary>
    /// Records a bar, rejecting one that cannot describe real trading.
    /// </summary>
    /// <param name="instrumentId">The instrument the bar belongs to.</param>
    /// <param name="interval">The resolution.</param>
    /// <param name="openedAtUtc">The instant the period opened, in UTC and on a boundary.</param>
    /// <param name="open">The first traded price.</param>
    /// <param name="high">The highest traded price.</param>
    /// <param name="low">The lowest traded price.</param>
    /// <param name="close">The last traded price.</param>
    /// <param name="volume">Units traded. Zero or more.</param>
    /// <param name="turnover">Cash value traded, when reported.</param>
    /// <param name="source">Where the bar came from.</param>
    /// <param name="ingestedAtUtc">The instant it entered the system.</param>
    /// <returns>The new bar.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static OhlcvBar Record(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset openedAtUtc,
        Price open,
        Price high,
        Price low,
        Price close,
        long volume,
        decimal? turnover,
        SourceCode source,
        DateTimeOffset ingestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A bar must belong to an instrument.");
        }

        if (!interval.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{interval}' is not a bar resolution this system records.");
        }

        if (!interval.IsAligned(openedAtUtc))
        {
            throw new DomainValidationException(
                $"A {interval} bar cannot open at {openedAtUtc:O}; it is not on a period boundary in UTC.");
        }

        if (ingestedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Ingestion timestamps must be UTC, but the offset was {ingestedAtUtc.Offset}.");
        }

        RequireConsistentPrices(open, high, low, close);
        RequireUsableQuantities(volume, turnover);

        return new OhlcvBar(
            instrumentId,
            interval,
            openedAtUtc,
            open,
            high,
            low,
            close,
            volume,
            turnover,
            source,
            ingestedAtUtc);
    }

    /// <summary>
    /// Applies a restatement from the source, if it changes anything.
    /// </summary>
    /// <remarks>
    /// Returns whether the bar moved, which is the difference the ingestion
    /// pipeline reports on: re-fetching a range that has not changed is the
    /// normal case and must not be counted as a revision, while a period that
    /// genuinely moved is worth knowing about.
    /// </remarks>
    /// <param name="open">The restated first traded price.</param>
    /// <param name="high">The restated highest traded price.</param>
    /// <param name="low">The restated lowest traded price.</param>
    /// <param name="close">The restated last traded price.</param>
    /// <param name="volume">The restated units traded.</param>
    /// <param name="turnover">The restated cash value traded.</param>
    /// <param name="source">The source supplying the restatement.</param>
    /// <param name="revisedAtUtc">The instant the restatement was applied.</param>
    /// <returns><see langword="true"/> when the bar changed.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public bool Revise(
        Price open,
        Price high,
        Price low,
        Price close,
        long volume,
        decimal? turnover,
        SourceCode source,
        DateTimeOffset revisedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (revisedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Revision timestamps must be UTC, but the offset was {revisedAtUtc.Offset}.");
        }

        RequireConsistentPrices(open, high, low, close);
        RequireUsableQuantities(volume, turnover);

        var unchanged =
            Open == open
            && High == high
            && Low == low
            && Close == close
            && Volume == volume
            && Turnover == turnover
            && Source == source;

        if (unchanged)
        {
            return false;
        }

        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Turnover = turnover;
        Source = source;
        RevisedAtUtc = revisedAtUtc;
        Revision++;
        TransformationVersion = DataRules.TransformationVersion;

        // The values moved, so whatever the quality rules concluded about the
        // old ones no longer applies. Leaving the stamp in place would let a
        // restated bar inherit a clean bill of health it never earned.
        ValidationVersion = DataRules.Unvalidated;

        return true;
    }

    /// <summary>
    /// Records that the bar has been checked by a version of the quality rules.
    /// </summary>
    /// <remarks>
    /// Says the rules ran, not that they found nothing. A bar with an open
    /// quality issue against it is still validated — the checking happened, and
    /// what it concluded is recorded separately.
    /// </remarks>
    /// <param name="version">The rule version that ran.</param>
    public void MarkValidated(int version) => ValidationVersion = version;

    private static void RequireConsistentPrices(Price open, Price high, Price low, Price close)
    {
        // The four checks below are the whole of what a single bar can be
        // asked about itself. Each one catches a real failure: a swapped
        // column pair, a high taken from a different period, a low carried
        // over from a previous session.
        if (high < low)
        {
            throw new DomainValidationException(
                $"A bar cannot have a high of {high} below its low of {low}.");
        }

        if (high < open || high < close)
        {
            throw new DomainValidationException(
                $"A bar's high of {high} must be at least its open of {open} and its close of {close}.");
        }

        if (low > open || low > close)
        {
            throw new DomainValidationException(
                $"A bar's low of {low} must be at most its open of {open} and its close of {close}.");
        }
    }

    private static void RequireUsableQuantities(long volume, decimal? turnover)
    {
        if (volume < 0)
        {
            throw new DomainValidationException($"A bar cannot have a volume of {volume}.");
        }

        if (turnover is < 0m)
        {
            throw new DomainValidationException($"A bar cannot have a turnover of {turnover}.");
        }

        // Turnover is cash changing hands. If nothing traded there is none,
        // and a positive value against zero volume means the two fields came
        // from different periods.
        if (volume == 0 && turnover is > 0m)
        {
            throw new DomainValidationException(
                $"A bar with no volume cannot have a turnover of {turnover}.");
        }
    }
}
