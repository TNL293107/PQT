using System.Globalization;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Domain.CorporateActions;

/// <summary>The canonical internal identifier of a <see cref="CorporateAction"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct CorporateActionId(Guid Value)
{
    /// <summary>Gets a value indicating whether the identifier is unassigned.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Issues a new identifier.</summary>
    /// <returns>A new, unique identifier.</returns>
    public static CorporateActionId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Something an issuer did that changes what its historical prices mean.
/// </summary>
/// <remarks>
/// <para>
/// The record the whole phase turns on. A two-for-one split halves the price
/// overnight; without this row the series shows a 50% crash that never
/// happened, and every return, every indicator and every backtest computed
/// across it is wrong in a way nothing reports.
/// </para>
/// <para>
/// An action is a fact about the issuer, not a derived quantity. The factor it
/// implies is computed separately and stored beside it, so a correction to the
/// arithmetic never touches the record of what actually happened.
/// </para>
/// <para>
/// Amendment is versioned rather than silent. Providers restate ex-dates and
/// ratios, and an adjustment recomputed from a changed action must be
/// distinguishable from one that was always this way.
/// </para>
/// </remarks>
public sealed class CorporateAction : AuditableEntity
{
    /// <summary>Total digits stored for a ratio or a cash amount.</summary>
    public const int AmountPrecision = 18;

    /// <summary>Digits permitted after the decimal point.</summary>
    public const int AmountScale = 6;

    /// <summary>Longest permitted note.</summary>
    public const int MaxNoteLength = 500;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private CorporateAction() => Source = null!;

    private CorporateAction(
        CorporateActionId id,
        InstrumentId instrumentId,
        CorporateActionType type,
        DateOnly exDate,
        decimal? ratio,
        decimal? cashAmount,
        SourceCode source)
    {
        Id = id;
        InstrumentId = instrumentId;
        Type = type;
        ExDate = exDate;
        Ratio = ratio;
        CashAmount = cashAmount;
        Source = source;
        Version = 1;
    }

    /// <summary>Gets the canonical internal identifier.</summary>
    public CorporateActionId Id { get; private set; }

    /// <summary>Gets the instrument the action concerns.</summary>
    public InstrumentId InstrumentId { get; private set; }

    /// <summary>Gets what the issuer did.</summary>
    public CorporateActionType Type { get; private set; }

    /// <summary>
    /// Gets the first date the security trades without the entitlement.
    /// </summary>
    /// <remarks>
    /// The only date the adjustment uses. Prices <em>before</em> it are
    /// rescaled and prices on and after it are already ex, which is why an
    /// ex-date wrong by one session misprices exactly one bar and is almost
    /// impossible to spot afterwards.
    /// </remarks>
    public DateOnly ExDate { get; private set; }

    /// <summary>Gets the date the register is closed, when the source states one.</summary>
    public DateOnly? RecordDate { get; private set; }

    /// <summary>Gets the date cash or shares are delivered, when the source states one.</summary>
    public DateOnly? PaymentDate { get; private set; }

    /// <summary>
    /// Gets the date the action became public, when the source states one.
    /// </summary>
    /// <remarks>
    /// Not used by the adjustment, and stored anyway. It is what separates
    /// "the market knew" from "the market found out later", and a point-in-time
    /// backtest that applies an action before it was announced has looked
    /// ahead. Recording it now costs a column; discovering it is missing during
    /// Phase 9 costs a migration and a re-import.
    /// </remarks>
    public DateOnly? AnnouncedOn { get; private set; }

    /// <summary>
    /// Gets the ratio, whose meaning depends on <see cref="Type"/>.
    /// </summary>
    /// <remarks>
    /// Shares after per share before for a split; <em>additional</em> shares
    /// per existing share for a stock dividend or bonus issue; new shares
    /// offered per existing share for a rights issue. The three are different
    /// quantities and the difference is a factor of one, which is exactly large
    /// enough to be wrong and small enough to look plausible.
    /// </remarks>
    public decimal? Ratio { get; private set; }

    /// <summary>
    /// Gets the cash amount, whose meaning depends on <see cref="Type"/>.
    /// </summary>
    /// <remarks>
    /// Cash per share for a dividend; the subscription price per new share for
    /// a rights issue. In the instrument's quote currency.
    /// </remarks>
    public decimal? CashAmount { get; private set; }

    /// <summary>Gets where the record came from.</summary>
    public SourceCode Source { get; private set; }

    /// <summary>
    /// Gets how many times the record has been restated, starting at one.
    /// </summary>
    /// <remarks>
    /// The version an adjustment records alongside its factor. When they
    /// disagree the factor was computed from an action that has since changed,
    /// and the series needs recomputing — which is a query rather than a
    /// re-adjustment of everything.
    /// </remarks>
    public int Version { get; private set; }

    /// <summary>Gets a value indicating whether the action was called off.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>Gets why it was cancelled or last amended, when a reason was given.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the action should rescale historical
    /// prices.
    /// </summary>
    public bool AffectsPrice => !IsCancelled && Type.AffectsPrice();

    /// <summary>
    /// Records an action an issuer announced.
    /// </summary>
    /// <param name="instrumentId">The instrument it concerns.</param>
    /// <param name="type">What the issuer did.</param>
    /// <param name="exDate">The first date the security trades without the entitlement.</param>
    /// <param name="ratio">The ratio, where the type requires one.</param>
    /// <param name="cashAmount">The cash amount, where the type requires one.</param>
    /// <param name="source">Where the record came from.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>The new action, at version one.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static CorporateAction Record(
        InstrumentId instrumentId,
        CorporateActionType type,
        DateOnly exDate,
        decimal? ratio,
        decimal? cashAmount,
        SourceCode source,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (instrumentId.IsEmpty)
        {
            throw new DomainValidationException("A corporate action must concern an instrument.");
        }

        if (!type.IsDeclared())
        {
            throw new DomainValidationException(
                $"'{type}' is not a corporate action this system records.");
        }

        RequireConsistentAmounts(type, ratio, cashAmount);

        var action = new CorporateAction(
            CorporateActionId.New(), instrumentId, type, exDate, ratio, cashAmount, source);

        action.MarkCreated(occurredAtUtc);
        return action;
    }

    /// <summary>
    /// Records the dates around the ex-date that a source supplied.
    /// </summary>
    /// <remarks>
    /// Separate from the factory because most sources carry the ex-date and
    /// little else, and a record that refused to exist without a payment date
    /// would mean importing nothing. None of them affect the adjustment.
    /// </remarks>
    /// <param name="recordDate">When the register closes.</param>
    /// <param name="paymentDate">When cash or shares are delivered.</param>
    /// <param name="announcedOn">When the action became public.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainValidationException">A date contradicts the ex-date.</exception>
    public void Schedule(
        DateOnly? recordDate,
        DateOnly? paymentDate,
        DateOnly? announcedOn,
        DateTimeOffset occurredAtUtc)
    {
        // An announcement after the ex-date is not a late filing; it is two
        // fields transposed, and accepting it would make a point-in-time read
        // hide an action the market already knew about.
        if (announcedOn is { } announced && announced > ExDate)
        {
            throw new DomainValidationException(
                $"An action cannot be announced on {announced:yyyy-MM-dd}, after its ex-date of {ExDate:yyyy-MM-dd}.");
        }

        if (paymentDate is { } payment && recordDate is { } record && payment < record)
        {
            throw new DomainValidationException(
                $"Payment on {payment:yyyy-MM-dd} cannot precede the record date of {record:yyyy-MM-dd}.");
        }

        RecordDate = recordDate;
        PaymentDate = paymentDate;
        AnnouncedOn = announcedOn;
        MarkUpdated(occurredAtUtc);
    }

    /// <summary>
    /// Restates the action after the source corrected it.
    /// </summary>
    /// <remarks>
    /// Returns whether anything moved, so a re-import of an unchanged action is
    /// not counted as a restatement and does not invalidate a factor that is
    /// still correct.
    /// </remarks>
    /// <param name="exDate">The corrected ex-date.</param>
    /// <param name="ratio">The corrected ratio.</param>
    /// <param name="cashAmount">The corrected cash amount.</param>
    /// <param name="note">Why it changed.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <returns><see langword="true"/> when the action changed.</returns>
    /// <exception cref="DomainStateException">The action was cancelled.</exception>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public bool Amend(
        DateOnly exDate,
        decimal? ratio,
        decimal? cashAmount,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        if (IsCancelled)
        {
            throw new DomainStateException(
                $"Corporate action {Id} was cancelled and can no longer be amended.");
        }

        RequireConsistentAmounts(Type, ratio, cashAmount);

        if (ExDate == exDate && Ratio == ratio && CashAmount == cashAmount)
        {
            return false;
        }

        ExDate = exDate;
        Ratio = ratio;
        CashAmount = cashAmount;
        Note = Truncate(note);
        Version++;
        MarkUpdated(occurredAtUtc);

        return true;
    }

    /// <summary>
    /// Records that the action was called off.
    /// </summary>
    /// <remarks>
    /// Cancelled rather than deleted. An adjustment computed from it may
    /// already be in the series, and a row that vanishes leaves nothing to
    /// explain why the factors changed. A cancelled action produces no factor,
    /// so recomputing removes its effect.
    /// </remarks>
    /// <param name="reason">Why it was called off.</param>
    /// <param name="occurredAtUtc">The instant the change is recorded.</param>
    /// <exception cref="DomainStateException">The action was already cancelled.</exception>
    public void Cancel(string reason, DateTimeOffset occurredAtUtc)
    {
        if (IsCancelled)
        {
            throw new DomainStateException($"Corporate action {Id} is already cancelled.");
        }

        IsCancelled = true;
        Note = Truncate(reason);
        Version++;
        MarkUpdated(occurredAtUtc);
    }

    private static void RequireConsistentAmounts(
        CorporateActionType type,
        decimal? ratio,
        decimal? cashAmount)
    {
        if (type.RequiresRatio())
        {
            RequireRatio(type, ratio);
        }
        else if (ratio is not null)
        {
            // A ratio on a cash dividend is a field filled in by mistake, and
            // silently ignoring it would leave a record that reads as though
            // it means something it does not.
            throw new DomainValidationException(
                $"A {type} carries no ratio, but one was supplied.");
        }

        if (type.RequiresCashAmount())
        {
            RequireCashAmount(type, cashAmount);
        }
        else if (cashAmount is not null)
        {
            throw new DomainValidationException(
                $"A {type} carries no cash amount, but one was supplied.");
        }
    }

    private static void RequireRatio(CorporateActionType type, decimal? ratio)
    {
        if (ratio is not { } value)
        {
            throw new DomainValidationException($"A {type} requires a ratio.");
        }

        if (value <= 0m)
        {
            throw new DomainValidationException(
                $"A {type} ratio must be positive, but {Format(value)} was supplied.");
        }

        if (value.Scale > AmountScale)
        {
            throw new DomainValidationException(
                $"A ratio may not carry more than {AmountScale} decimal places.");
        }

        // A split that turns one share into one share is not a split. It
        // produces a factor of exactly one, and a row that adjusts nothing is
        // a transcription error rather than an event.
        if (type is CorporateActionType.StockSplit or CorporateActionType.ReverseSplit
            && value == 1m)
        {
            throw new DomainValidationException(
                $"A {type} with a ratio of 1 changes nothing and is not an action.");
        }
    }

    private static void RequireCashAmount(CorporateActionType type, decimal? cashAmount)
    {
        if (cashAmount is not { } value)
        {
            throw new DomainValidationException($"A {type} requires a cash amount.");
        }

        if (value <= 0m)
        {
            throw new DomainValidationException(
                $"A {type} cash amount must be positive, but {Format(value)} was supplied.");
        }

        if (value.Scale > AmountScale)
        {
            throw new DomainValidationException(
                $"A cash amount may not carry more than {AmountScale} decimal places.");
        }
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Truncate(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();

        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
