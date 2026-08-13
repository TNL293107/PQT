namespace PersonalQuant.Application.Abstractions;

/// <summary>
/// Supplies the current time.
/// </summary>
/// <remarks>
/// Every timestamp in a trading system is either an observation time or an
/// event time, and both must be reproducible in tests and backtests. Reading
/// <see cref="DateTimeOffset.UtcNow"/> directly anywhere outside the
/// implementation of this abstraction makes that impossible, so application
/// and domain code takes an <see cref="IClock"/> instead.
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
