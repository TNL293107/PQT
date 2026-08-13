using PersonalQuant.Application.Abstractions;

namespace PersonalQuant.Infrastructure.Time;

/// <summary>
/// The real clock, reading wall-clock time in UTC.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
