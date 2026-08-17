namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// The period one <see cref="OhlcvBar"/> covers.
/// </summary>
/// <remarks>
/// <para>
/// Values are the interval's length in minutes, and are explicit because they
/// are persisted and because the arithmetic depends on them. A daily bar is
/// 1440 rather than a separate concept: end-of-day data and intraday data
/// differ in resolution, not in kind, and giving them one type means one
/// storage layout, one deduplication rule and one alignment check instead of
/// two of each.
/// </para>
/// <para>
/// Tick data is deliberately absent. A tick is not a bar — it has no open,
/// high, low or close — and modelling it as a zero-length interval would put a
/// row shaped like a bar into a table that means something else.
/// </para>
/// </remarks>
public enum BarInterval
{
    /// <summary>Not specified. Never valid on a stored bar.</summary>
    Unspecified = 0,

    /// <summary>One minute.</summary>
    OneMinute = 1,

    /// <summary>Five minutes.</summary>
    FiveMinutes = 5,

    /// <summary>Fifteen minutes.</summary>
    FifteenMinutes = 15,

    /// <summary>Thirty minutes.</summary>
    ThirtyMinutes = 30,

    /// <summary>One hour.</summary>
    OneHour = 60,

    /// <summary>One trading day — the end-of-day series.</summary>
    OneDay = 1440,
}

/// <summary>
/// Arithmetic over <see cref="BarInterval"/>.
/// </summary>
public static class BarIntervals
{
    /// <summary>
    /// Returns how long an interval lasts.
    /// </summary>
    /// <param name="interval">The interval to measure.</param>
    /// <returns>The interval's duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The interval is <see cref="BarInterval.Unspecified"/> or not a declared
    /// value.
    /// </exception>
    public static TimeSpan ToDuration(this BarInterval interval) =>
        IsDeclared(interval)
            ? TimeSpan.FromMinutes((int)interval)
            : throw new ArgumentOutOfRangeException(
                nameof(interval), interval, "The bar interval is not a known resolution.");

    /// <summary>
    /// Reports whether an interval is one of the declared resolutions.
    /// </summary>
    /// <remarks>
    /// An enum in .NET holds any integer of its underlying type, so a value
    /// read back from a database or parsed from configuration has to be
    /// checked rather than assumed.
    /// </remarks>
    /// <param name="interval">The value to check.</param>
    /// <returns><see langword="true"/> when the interval is usable.</returns>
    public static bool IsDeclared(this BarInterval interval) =>
        interval is BarInterval.OneMinute
            or BarInterval.FiveMinutes
            or BarInterval.FifteenMinutes
            or BarInterval.ThirtyMinutes
            or BarInterval.OneHour
            or BarInterval.OneDay;

    /// <summary>
    /// Reports whether an instant is a legal opening time for an interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A five-minute bar opens at a five-minute boundary and a daily bar at
    /// midnight. A provider that returns 09:03 for a five-minute series is
    /// either sending a partial bar or has shifted the whole series, and both
    /// are corruption that is invisible once stored — every later query would
    /// return a plausible-looking answer computed from misaligned periods.
    /// </para>
    /// <para>
    /// Alignment is measured in UTC against the Unix epoch, which is a whole
    /// number of days and hours, so a UTC boundary is also a boundary in any
    /// zone whose offset is a whole number of minutes. Vietnam is UTC+7, so
    /// every venue this system covers aligns under both.
    /// </para>
    /// </remarks>
    /// <param name="interval">The interval the bar belongs to.</param>
    /// <param name="openedAtUtc">The bar's opening instant.</param>
    /// <returns><see langword="true"/> when the instant is on a boundary.</returns>
    public static bool IsAligned(this BarInterval interval, DateTimeOffset openedAtUtc) =>
        openedAtUtc.Offset == TimeSpan.Zero
        && openedAtUtc.UtcTicks % interval.ToDuration().Ticks == 0;
}
