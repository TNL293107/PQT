using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.MarketData;

/// <summary>
/// What a market data source can serve, as the source itself declares it.
/// </summary>
/// <remarks>
/// <para>
/// Declared, never measured. Nothing probes a provider to discover what it can
/// do: a wrong declaration is a provider bug and is reported as one, against a
/// request the provider claimed it could serve. Discovering capability by
/// failure means three retries against an endpoint that was never going to
/// answer, and a gap in a series whose recorded reason is a timeout.
/// </para>
/// <para>
/// Supplied by the provider rather than configured about it. A deployment can
/// say which sources it has; only the source knows what it holds.
/// </para>
/// <para>
/// <strong>Absent is not unlimited.</strong> An empty exchange set means the
/// source has no venue restriction, which is true of a directory of CSV files
/// and never true of a vendor. A null <see cref="EarliestAvailable"/> means
/// <em>not stated</em> — not <em>unbounded</em> — and every surface that
/// renders it must say so. It is the same rule U2 applies to universe coverage:
/// an unstated claim and a complete one must never look alike.
/// </para>
/// </remarks>
public sealed record ProviderCapability
{
    /// <summary>Gets the code the source is registered and recorded under.</summary>
    public required SourceCode Code { get; init; }

    /// <summary>Gets the name an operator surface shows.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the venues the source covers, or an empty set for no restriction.
    /// </summary>
    /// <remarks>
    /// Empty is correct for a file source and is a mistake for a vendor: a feed
    /// that covers HOSE and not UPCOM has to be able to say so, or every UPCOM
    /// request becomes a retry loop.
    /// </remarks>
    public IReadOnlySet<ExchangeCode> Exchanges { get; init; } = new HashSet<ExchangeCode>();

    /// <summary>
    /// Gets the asset types the source covers, or an empty set for no
    /// restriction.
    /// </summary>
    public IReadOnlySet<AssetType> AssetTypes { get; init; } = new HashSet<AssetType>();

    /// <summary>Gets the resolutions the source serves.</summary>
    /// <remarks>
    /// The source of truth behind <see cref="IMarketDataProvider.SupportedIntervals"/>,
    /// which delegates here. A source that serves none could never answer
    /// anything, so an empty set is a composition error rather than a
    /// configuration.
    /// </remarks>
    public required IReadOnlySet<BarInterval> Intervals { get; init; }

    /// <summary>
    /// Gets the earliest date the source holds, or <see langword="null"/> when
    /// it does not state one.
    /// </summary>
    /// <remarks>
    /// Null is <em>unknown</em>. A request reaching back before a stated floor
    /// is clamped forward and the clamp is recorded on the run, so a short
    /// series has a recorded reason rather than an unexplained start date.
    /// </remarks>
    public DateOnly? EarliestAvailable { get; init; }

    /// <summary>Gets which fields actually arrive from the source.</summary>
    public required ProviderReportedFields ReportedFields { get; init; }

    /// <summary>Gets the constraints on how the source may be asked.</summary>
    public required ProviderLimitations Limitations { get; init; }

    /// <summary>
    /// Reports whether the source declares it covers a venue.
    /// </summary>
    /// <param name="exchange">The venue, or null when it is not known.</param>
    /// <returns>
    /// <see langword="true"/> when the source declares no restriction, or
    /// declares this venue. A venue that is not known cannot be refused.
    /// </returns>
    public bool Covers(ExchangeCode? exchange) =>
        Exchanges.Count == 0 || exchange is null || Exchanges.Contains(exchange);

    /// <summary>
    /// Reports whether the source declares it serves an asset type.
    /// </summary>
    /// <param name="assetType">The type, or null when it is not known.</param>
    /// <returns><see langword="true"/> when the source declares no restriction, or declares it.</returns>
    public bool Serves(AssetType? assetType) =>
        AssetTypes.Count == 0 || assetType is null || AssetTypes.Contains(assetType.Value);

    /// <summary>
    /// Reports whether the source declares it serves a resolution.
    /// </summary>
    /// <param name="interval">The resolution.</param>
    /// <returns><see langword="true"/> when the source serves it.</returns>
    public bool Serves(BarInterval interval) => Intervals.Contains(interval);

    /// <summary>
    /// Checks that a declaration is usable, throwing when it is not.
    /// </summary>
    /// <remarks>
    /// Called when the registry is composed, which is where a duplicate
    /// provider code is already rejected. A source that cannot answer anything
    /// is a deployment defect, and failing at start-up is cheaper than a
    /// nightly pass of skipped runs.
    /// </remarks>
    /// <param name="declaredBy">The code of the provider that declared it.</param>
    /// <exception cref="InvalidOperationException">The declaration is unusable.</exception>
    public void Validate(SourceCode declaredBy)
    {
        ArgumentNullException.ThrowIfNull(declaredBy);

        if (Code != declaredBy)
        {
            throw new InvalidOperationException(
                $"Provider '{declaredBy}' declares a capability for '{Code}'.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                $"Provider '{declaredBy}' declares no display name.");
        }

        if (Intervals.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider '{declaredBy}' declares no resolutions and could never answer a request.");
        }

        if (Limitations.MaxPeriodsPerCall is <= 0)
        {
            throw new InvalidOperationException(
                $"Provider '{declaredBy}' declares a call bound of {Limitations.MaxPeriodsPerCall}.");
        }

        if (Limitations.MinimumCallSpacing is { } spacing && spacing < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Provider '{declaredBy}' declares a negative minimum call spacing.");
        }
    }
}

/// <summary>
/// Which fields a source actually reports.
/// </summary>
/// <remarks>
/// Separated from <see cref="ProviderLimitations"/> because one is about what
/// arrives and the other about how it may be asked for. Two of these gate
/// research correctness rather than convenience, and that is why they are
/// declared rather than discovered when a backtest looks wrong.
/// </remarks>
public sealed record ProviderReportedFields
{
    /// <summary>Gets a value indicating whether cash traded is reported.</summary>
    public bool Turnover { get; init; }

    /// <summary>
    /// Gets which trades the reported volume counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vietnamese venues run two books. Continuous order matching —
    /// <em>khớp lệnh</em> — is one; negotiated block trades —
    /// <em>thỏa thuận</em> — are the other, and they are agreed off the book and
    /// reported separately. A feed may publish either, or their sum, and the
    /// number looks identical whichever it is.
    /// </para>
    /// <para>
    /// The difference is not a rounding error. Block trades are where
    /// institutional size actually moves, so a volume that excludes them
    /// understates liquidity by whatever proportion of the day's turnover was
    /// negotiated — and understates it worst on exactly the days a liquidity
    /// filter is deciding something. A universe screened on average daily
    /// volume, a participation-rate cap, an execution-cost model: each of them
    /// silently means something different depending on this value, and none of
    /// them can detect which.
    /// </para>
    /// <para>
    /// So it is declared rather than inferred, and
    /// <see cref="MarketData.VolumeBasis.Unspecified"/> is the honest default.
    /// A source that has not said must not be read as having said "everything".
    /// </para>
    /// </remarks>
    public VolumeBasis VolumeBasis { get; init; } = VolumeBasis.Unspecified;

    /// <summary>
    /// Gets a value indicating whether corporate actions carry an announcement
    /// date.
    /// </summary>
    /// <remarks>
    /// Gates U4's strict mode. Without announcement dates every action has to
    /// be treated as either always-known or never-known, and both are wrong in
    /// a way that only shows up as a backtest that is too good.
    /// </remarks>
    public bool AnnouncementDates { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source publishes corrections rather
    /// than silently rewriting history.
    /// </summary>
    /// <remarks>
    /// Gates reproducible backtests. A feed that rewrites the past in place
    /// cannot be replayed, so a result computed against it last month cannot be
    /// reproduced this month, and nothing says why.
    /// </remarks>
    public bool Restatements { get; init; }
}

/// <summary>
/// Which trades a source's reported volume counts.
/// </summary>
/// <remarks>
/// Not enforced, and deliberately so for now. The ingestion pipeline refuses to
/// mix a raw series with a source-adjusted one because two sources declare
/// opposing adjustment conventions and a mixture is reachable today. Only one
/// registered source states a volume basis at all, so a rule refusing a mixture
/// would be guarding against a case that cannot yet occur. When a second source
/// states a different basis, the refusal belongs beside V9 in the ingestion
/// service, built the same way and for the same reason.
/// </remarks>
public enum VolumeBasis
{
    /// <summary>
    /// The source does not state what its volume counts.
    /// </summary>
    /// <remarks>
    /// The default, and not a synonym for "everything". A directory of CSV
    /// files exported by somebody else genuinely does not know, and reading
    /// that silence as a claim is how an unstated basis becomes an assumed one.
    /// </remarks>
    Unspecified = 0,

    /// <summary>
    /// Continuous order-book matching only — <em>khớp lệnh</em>.
    /// </summary>
    /// <remarks>
    /// Excludes negotiated block trades. Understates traded size, by a margin
    /// that varies by security and by day.
    /// </remarks>
    MatchedOrders = 1,

    /// <summary>
    /// Order matching and negotiated block trades together — <em>khớp lệnh</em>
    /// plus <em>thỏa thuận</em>.
    /// </summary>
    MatchedAndNegotiated = 2,
}

/// <summary>
/// The constraints a source places on how it may be asked.
/// </summary>
public sealed record ProviderLimitations
{
    /// <summary>
    /// Gets the most periods one call may cover, or <see langword="null"/> when
    /// the source states none.
    /// </summary>
    /// <remarks>
    /// A bound the pipeline applies by truncating the range. The checkpoint
    /// already resumes, so a long backfill becomes several runs rather than one
    /// rejected call.
    /// </remarks>
    public int? MaxPeriodsPerCall { get; init; }

    /// <summary>
    /// Gets the shortest interval between calls the source tolerates, or
    /// <see langword="null"/> when it states none.
    /// </summary>
    public TimeSpan? MinimumCallSpacing { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source returns prices already
    /// adjusted for corporate actions.
    /// </summary>
    /// <remarks>
    /// Not a detail. This system stores raw prices and adjusts on read, so a
    /// pre-adjusted feed is a <em>different dataset</em> that happens to share
    /// a shape. Mixing the two produces numbers no quality rule can catch,
    /// which is why the fact is declared here rather than inferred later.
    /// </remarks>
    public bool AdjustsPricesAtSource { get; init; }
}
