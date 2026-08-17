namespace PersonalQuant.Application.MarketData;

/// <summary>
/// Why a provider row was refused.
/// </summary>
/// <remarks>
/// <para>
/// Enumerated rather than left as free text so that rejections can be counted,
/// compared between sources and alerted on. "Four hundred rows rejected" is
/// noise; "four hundred rows rejected because the high was below the close" is
/// a column swapped at the provider, and the two sentences take the same
/// amount of storage.
/// </para>
/// <para>
/// Values are explicit because they are logged and will end up in dashboards
/// that outlive this enum's declaration order.
/// </para>
/// </remarks>
public enum BarRejectionReason
{
    /// <summary>A price was zero, negative, or beyond the accepted range.</summary>
    UnusablePrice = 1,

    /// <summary>
    /// The prices contradict each other — a high below the low, or a high or
    /// low outside the open and close.
    /// </summary>
    InconsistentPrices = 2,

    /// <summary>Volume was negative, or turnover contradicted it.</summary>
    UnusableQuantity = 3,

    /// <summary>The timestamp was not on a boundary for the interval.</summary>
    MisalignedTimestamp = 4,

    /// <summary>The timestamp fell outside the range that was requested.</summary>
    OutsideRequestedRange = 5,

    /// <summary>
    /// The same period appeared more than once in one response.
    /// </summary>
    DuplicateWithinBatch = 6,
}

/// <summary>
/// One provider row that did not become a bar, and why.
/// </summary>
/// <remarks>
/// The rejected row is kept alongside the reason. A count alone cannot be
/// investigated, and the offending values are usually the whole diagnosis —
/// prices in the wrong currency, a volume field holding turnover, timestamps
/// an hour out.
/// </remarks>
/// <param name="Bar">The row as the provider reported it.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Detail">A short, specific explanation.</param>
public sealed record BarRejection(ProviderBar Bar, BarRejectionReason Reason, string Detail);
