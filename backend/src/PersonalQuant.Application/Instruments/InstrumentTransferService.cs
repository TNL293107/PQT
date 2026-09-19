using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IInstrumentTransferService"/>.
/// </summary>
/// <param name="instruments">The instrument master.</param>
/// <param name="exchanges">The venue repository.</param>
/// <param name="unitOfWork">Commits the transfer.</param>
/// <param name="clock">Supplies the audit instant.</param>
internal sealed class InstrumentTransferService(
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IUnitOfWork unitOfWork,
    IClock clock) : IInstrumentTransferService
{
    /// <inheritdoc />
    public async Task<InstrumentTransfer> TransferAsync(
        InstrumentId instrumentId,
        ExchangeCode destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var instrument = await instruments
            .FindByIdAsync(instrumentId, cancellationToken)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return new InstrumentTransfer(InstrumentTransferOutcome.UnknownInstrument, null, destination);
        }

        var current = await exchanges
            .FindByIdAsync(instrument.ExchangeId, cancellationToken)
            .ConfigureAwait(false);

        var from = current?.Code;

        var target = await exchanges.FindByCodeAsync(destination, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return new InstrumentTransfer(InstrumentTransferOutcome.UnknownExchange, from, destination);
        }

        if (target.Id == instrument.ExchangeId)
        {
            return new InstrumentTransfer(InstrumentTransferOutcome.AlreadyThere, from, destination);
        }

        if (instrument.Status is InstrumentStatus.Delisted)
        {
            return new InstrumentTransfer(InstrumentTransferOutcome.Delisted, from, destination);
        }

        // Checked here rather than left to the unique index: a violation there
        // surfaces as a database exception on commit, and the operator needs to
        // be told which record is in the way, not that a constraint fired.
        if (await instruments
                .IsTickerTakenAsync(target.Id, instrument.Ticker, cancellationToken)
                .ConfigureAwait(false))
        {
            return new InstrumentTransfer(InstrumentTransferOutcome.TickerTaken, from, destination);
        }

        instrument.TransferToExchange(target.Id, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new InstrumentTransfer(InstrumentTransferOutcome.Transferred, from, destination);
    }
}
