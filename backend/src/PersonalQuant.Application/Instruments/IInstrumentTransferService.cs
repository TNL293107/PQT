using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Records that a security now lists on a different venue.
/// </summary>
/// <remarks>
/// <para>
/// Routine in Vietnam, where issuers move from UPCOM to HNX to HOSE, and not
/// something the instrument import can do on its own. The only thing linking
/// a listing on one venue to a listing on another is the ticker, which is the
/// weakest identity the master holds; a row that inferred a transfer from it
/// would sooner or later move the wrong security. So a transfer is a
/// deliberate act somebody names, the way closing a quality finding is.
/// </para>
/// <para>
/// The master holds a venue, not a venue history. Everything read through the
/// venue after a transfer — the price band the inspector and the adjustment
/// engine judge by, which sources cover the instrument — is the new venue's,
/// including for bars that printed before the move.
/// </para>
/// </remarks>
public interface IInstrumentTransferService
{
    /// <summary>
    /// Moves an instrument to a venue, keeping its identity.
    /// </summary>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="destination">The venue it now lists on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened, including why nothing did.</returns>
    Task<InstrumentTransfer> TransferAsync(
        InstrumentId instrumentId,
        ExchangeCode destination,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of a transfer.
/// </summary>
public enum InstrumentTransferOutcome
{
    /// <summary>The instrument now lists on the named venue.</summary>
    Transferred = 0,

    /// <summary>It already did; nothing changed.</summary>
    AlreadyThere = 1,

    /// <summary>No instrument has that identifier.</summary>
    UnknownInstrument = 2,

    /// <summary>The system holds no venue with that code.</summary>
    UnknownExchange = 3,

    /// <summary>Another active security holds the ticker on the target venue.</summary>
    TickerTaken = 4,

    /// <summary>The instrument is delisted and can no longer move.</summary>
    Delisted = 5,
}

/// <summary>
/// What a transfer did.
/// </summary>
/// <param name="Outcome">The result.</param>
/// <param name="From">The venue it was on, when the instrument is known.</param>
/// <param name="To">The venue named.</param>
public sealed record InstrumentTransfer(
    InstrumentTransferOutcome Outcome,
    ExchangeCode? From,
    ExchangeCode To)
{
    /// <summary>Gets whether the instrument is on the named venue afterwards.</summary>
    public bool Succeeded =>
        Outcome is InstrumentTransferOutcome.Transferred or InstrumentTransferOutcome.AlreadyThere;
}
