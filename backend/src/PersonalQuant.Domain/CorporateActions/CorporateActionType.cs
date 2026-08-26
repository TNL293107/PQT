namespace PersonalQuant.Domain.CorporateActions;

/// <summary>
/// What an issuer did that changes what a historical price means.
/// </summary>
/// <remarks>
/// <para>
/// A closed enumeration, and every value carries behaviour: which fields it
/// requires, and how a price before its ex-date has to be rescaled. A reference
/// table could not hold either.
/// </para>
/// <para>
/// Rights issues and bonus shares sit alongside splits and dividends rather
/// than below them. In Vietnam they are ordinary — an issuer paying a stock
/// dividend and running a rights issue in the same year is unremarkable — and
/// treating them as edge cases is how a Vietnamese series ends up adjusted
/// almost right.
/// </para>
/// </remarks>
public enum CorporateActionType
{
    /// <summary>Not specified. Never valid on a stored action.</summary>
    Unspecified = 0,

    /// <summary>
    /// Cash paid per share. Requires a cash amount and no ratio.
    /// </summary>
    /// <remarks>
    /// The share count does not change, so the price drops by roughly the
    /// dividend and the volume series is untouched.
    /// </remarks>
    CashDividend = 1,

    /// <summary>
    /// Shares paid per share held, such as a 10% stock dividend. Requires a
    /// ratio of <em>additional</em> shares per existing share.
    /// </summary>
    StockDividend = 2,

    /// <summary>
    /// Each existing share becomes several. Requires a ratio of shares
    /// <em>after</em> per share before, so a two-for-one split is
    /// <c>2</c>.
    /// </summary>
    StockSplit = 3,

    /// <summary>
    /// Several existing shares become one. Requires a ratio of shares
    /// <em>after</em> per share before, so a one-for-ten consolidation is
    /// <c>0.1</c>.
    /// </summary>
    ReverseSplit = 4,

    /// <summary>
    /// Shares offered to existing holders below market. Requires a ratio of
    /// new shares offered per existing share, and the subscription price as
    /// the cash amount.
    /// </summary>
    /// <remarks>
    /// The one action whose adjustment needs two numbers. A rights issue at a
    /// deep discount moves the price further than a split of the same ratio
    /// would, and using the ratio alone understates it.
    /// </remarks>
    RightsIssue = 5,

    /// <summary>
    /// Free shares distributed from reserves. Requires a ratio of additional
    /// shares per existing share.
    /// </summary>
    /// <remarks>
    /// Arithmetically identical to a stock dividend and kept separate because
    /// the two are distinct events with distinct tax and accounting treatment,
    /// and a record that collapsed them could not be reconciled against the
    /// issuer's own announcement.
    /// </remarks>
    BonusShares = 6,

    /// <summary>
    /// New shares sold to someone, such as a private placement.
    /// </summary>
    /// <remarks>
    /// Recorded, and deliberately not adjusted for. Existing holders keep the
    /// same shares at the same price; the dilution is economic rather than
    /// mechanical, and the market prices it on the day. Rescaling history for
    /// it would invent a move that never happened.
    /// </remarks>
    ShareIssuance = 7,

    /// <summary>
    /// The security now trades under a different ticker.
    /// </summary>
    /// <remarks>
    /// No price effect. It is recorded here because a symbol change is a
    /// corporate action an issuer announces, and because a series that jumps
    /// for no visible reason is usually one that changed symbol. Identity is
    /// unaffected — the instrument keeps its canonical identifier.
    /// </remarks>
    SymbolChange = 8,
}

/// <summary>
/// Facts about a <see cref="CorporateActionType"/> that the model switches on.
/// </summary>
public static class CorporateActionTypes
{
    /// <summary>
    /// Reports whether a type is one of the declared values.
    /// </summary>
    /// <param name="type">The value to check.</param>
    /// <returns><see langword="true"/> when the type is usable.</returns>
    public static bool IsDeclared(this CorporateActionType type) =>
        type is >= CorporateActionType.CashDividend and <= CorporateActionType.SymbolChange;

    /// <summary>
    /// Reports whether a type rescales the prices recorded before its ex-date.
    /// </summary>
    /// <remarks>
    /// The distinction the adjustment engine turns on. A share issuance and a
    /// symbol change are real events worth recording and produce no factor;
    /// asking for one would either fabricate a number or force every caller to
    /// special-case them.
    /// </remarks>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> when historical prices must be rescaled.</returns>
    public static bool AffectsPrice(this CorporateActionType type) =>
        type is CorporateActionType.CashDividend
            or CorporateActionType.StockDividend
            or CorporateActionType.StockSplit
            or CorporateActionType.ReverseSplit
            or CorporateActionType.RightsIssue
            or CorporateActionType.BonusShares;

    /// <summary>
    /// Reports whether a type requires a ratio.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> when a ratio is mandatory.</returns>
    public static bool RequiresRatio(this CorporateActionType type) =>
        type is CorporateActionType.StockDividend
            or CorporateActionType.StockSplit
            or CorporateActionType.ReverseSplit
            or CorporateActionType.RightsIssue
            or CorporateActionType.BonusShares;

    /// <summary>
    /// Reports whether a type requires a cash amount.
    /// </summary>
    /// <remarks>
    /// A cash dividend's is the payment per share; a rights issue's is the
    /// subscription price per new share. They are different quantities in the
    /// same column, which is why each type's meaning is documented on the
    /// value itself.
    /// </remarks>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> when a cash amount is mandatory.</returns>
    public static bool RequiresCashAmount(this CorporateActionType type) =>
        type is CorporateActionType.CashDividend or CorporateActionType.RightsIssue;
}
