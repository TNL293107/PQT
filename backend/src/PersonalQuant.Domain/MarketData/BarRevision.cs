using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// What a bar was believed to be, over the interval of time it was believed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OhlcvBar"/> holds the current best statement of a period and
/// overwrites itself when a source restates it. This holds every statement,
/// including the superseded ones, each stamped with the window of observation
/// time it was current for. Together they answer the question a backtest has to
/// ask and the bar alone cannot: <em>what did this system believe on the day
/// the simulated decision was made?</em>
/// </para>
/// <para>
/// Append-only. A revision is never updated except to close its observation
/// window, and never deleted. A history that can be edited is not a history.
/// </para>
/// <para>
/// The snapshot is complete rather than a diff. A diff would have to be
/// replayed from the beginning to answer any question, and would make a single
/// corrupt row poison every later reconstruction.
/// </para>
/// <para>
/// <see cref="Revision"/> is an ordinal identity, not a time. It says
/// <em>which</em> statement this is; <see cref="ObservedFromUtc"/> says when
/// the system came to hold it. Neither substitutes for the other, and using the
/// revision number as an ordering over time is the mistake this type exists to
/// prevent.
/// </para>
/// </remarks>
public sealed class BarRevision
{
    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private BarRevision() => Source = null!;

    private BarRevision(
        InstrumentId instrumentId,
        BarInterval interval,
        DateTimeOffset openedAtUtc,
        int revision,
        Price open,
        Price high,
        Price low,
        Price close,
        long volume,
        decimal? turnover,
        SourceCode source,
        DateTimeOffset observedFromUtc,
        int transformationVersion,
        int validationVersion)
    {
        InstrumentId = instrumentId;
        Interval = interval;
        OpenedAtUtc = openedAtUtc;
        Revision = revision;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Turnover = turnover;
        Source = source;
        ObservedFromUtc = observedFromUtc;
        ObservedToUtc = null;
        TransformationVersion = transformationVersion;
        ValidationVersion = validationVersion;
    }

    /// <summary>Gets the instrument the bar belongs to.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets the resolution the bar was aggregated at.</summary>
    public BarInterval Interval { get; private set; }

    /// <summary>Gets the instant the period opened, in UTC.</summary>
    /// <remarks>
    /// Event time: when the market did this. Unrelated to
    /// <see cref="ObservedFromUtc"/>, which is when this system found out.
    /// </remarks>
    public DateTimeOffset OpenedAtUtc { get; private set; }

    /// <summary>
    /// Gets which statement of the period this is, counting from zero.
    /// </summary>
    /// <remarks>
    /// Matches <see cref="OhlcvBar.Revision"/> at the moment the snapshot was
    /// taken. An ordinal, not a timestamp.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>Gets the first traded price as this revision stated it.</summary>
    public Price Open { get; private set; }

    /// <summary>Gets the highest traded price as this revision stated it.</summary>
    public Price High { get; private set; }

    /// <summary>Gets the lowest traded price as this revision stated it.</summary>
    public Price Low { get; private set; }

    /// <summary>Gets the last traded price as this revision stated it.</summary>
    public Price Close { get; private set; }

    /// <summary>Gets the units traded as this revision stated them.</summary>
    public long Volume { get; private set; }

    /// <summary>Gets the cash value traded, when the source carried one.</summary>
    public decimal? Turnover { get; private set; }

    /// <summary>Gets the source that supplied this statement.</summary>
    /// <remarks>
    /// Kept per revision rather than per bar, because a restatement may come
    /// from a different provider than the original and "who said so, and when"
    /// is the whole point of the record.
    /// </remarks>
    public SourceCode Source { get; private set; }

    /// <summary>
    /// Gets the instant this system began holding this statement, in UTC.
    /// Inclusive.
    /// </summary>
    /// <remarks>
    /// Observation time. A query as of exactly this instant sees this revision.
    /// </remarks>
    public DateTimeOffset ObservedFromUtc { get; private set; }

    /// <summary>
    /// Gets the instant this statement was superseded, in UTC, or
    /// <see langword="null"/> while it is still the current one. Exclusive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is half-open — <c>[ObservedFromUtc, ObservedToUtc)</c> — so
    /// the closing edge of one revision and the opening edge of the next are
    /// the same instant and every instant falls in exactly one window.
    /// </para>
    /// <para>
    /// <see langword="null"/> means currently observed, not "unknown". There is
    /// exactly one such revision per period at any time.
    /// </para>
    /// </remarks>
    public DateTimeOffset? ObservedToUtc { get; private set; }

    /// <summary>
    /// Gets the version of the normalisation rules that produced this
    /// statement.
    /// </summary>
    public int TransformationVersion { get; private set; }

    /// <summary>
    /// Gets the version of the quality rules this statement had passed when the
    /// snapshot was taken.
    /// </summary>
    public int ValidationVersion { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is the statement currently held.
    /// </summary>
    public bool IsCurrent => ObservedToUtc is null;

    /// <summary>
    /// Takes a snapshot of a bar as it stands, opening its observation window.
    /// </summary>
    /// <remarks>
    /// Called when a bar is first recorded and again after every restatement
    /// that changed something. The bar has already validated its own values, so
    /// this copies rather than re-checks — a snapshot that could reject what the
    /// canonical row accepted would leave the two permanently out of step.
    /// </remarks>
    /// <param name="bar">The bar to snapshot.</param>
    /// <param name="observedFromUtc">
    /// The instant the system began holding this statement. Must be UTC, and is
    /// the same run instant that stamped the bar.
    /// </param>
    /// <returns>The open revision.</returns>
    /// <exception cref="DomainValidationException">
    /// <paramref name="observedFromUtc"/> is not UTC.
    /// </exception>
    public static BarRevision Snapshot(OhlcvBar bar, DateTimeOffset observedFromUtc)
    {
        ArgumentNullException.ThrowIfNull(bar);

        if (observedFromUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Observation timestamps must be UTC, but the offset was {observedFromUtc.Offset}.");
        }

        return new BarRevision(
            bar.InstrumentId,
            bar.Interval,
            bar.OpenedAtUtc,
            bar.Revision,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.Turnover,
            bar.Source,
            observedFromUtc,
            bar.TransformationVersion,
            bar.ValidationVersion);
    }

    /// <summary>
    /// Closes the observation window, marking this statement superseded.
    /// </summary>
    /// <remarks>
    /// The instant passed here is the same one that opens the succeeding
    /// revision, so the two windows meet exactly. Reading the clock twice would
    /// leave a gap that an as-of query could land inside, and a bar that has
    /// existed continuously would appear to vanish for the width of it.
    /// </remarks>
    /// <param name="observedToUtc">
    /// The instant the statement was superseded. Must be UTC and must not
    /// precede <see cref="ObservedFromUtc"/>.
    /// </param>
    /// <exception cref="DomainValidationException">The instant is not usable.</exception>
    /// <exception cref="DomainStateException">The window is already closed.</exception>
    public void Supersede(DateTimeOffset observedToUtc)
    {
        if (observedToUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException(
                $"Observation timestamps must be UTC, but the offset was {observedToUtc.Offset}.");
        }

        if (ObservedToUtc is not null)
        {
            throw new DomainStateException(
                "This revision was already superseded and cannot be closed twice.");
        }

        if (observedToUtc < ObservedFromUtc)
        {
            throw new DomainValidationException(
                "A revision cannot be superseded before it was observed.");
        }

        ObservedToUtc = observedToUtc;
    }

    /// <summary>
    /// Determines whether this statement was the one held at an instant.
    /// </summary>
    /// <param name="knownAsOfUtc">The observation instant to test.</param>
    /// <returns>
    /// <see langword="true"/> when the instant falls in
    /// <c>[ObservedFromUtc, ObservedToUtc)</c>.
    /// </returns>
    public bool WasKnownAt(DateTimeOffset knownAsOfUtc) =>
        ObservedFromUtc <= knownAsOfUtc
        && (ObservedToUtc is not { } until || until > knownAsOfUtc);
}
