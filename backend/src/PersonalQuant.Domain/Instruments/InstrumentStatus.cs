namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// Where an instrument sits in its listing lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// Permitted transitions:
/// </para>
/// <code>
/// Pending ──► Listed ──► Suspended
///                ▲           │
///                └───────────┘
///                │           │
///                ▼           ▼
///             Delisted (terminal)
/// </code>
/// <para>
/// <see cref="Delisted"/> is terminal. The row is retained: delisting is a
/// state change, never a delete, because historical prices and positions
/// continue to reference the instrument.
/// </para>
/// </remarks>
public enum InstrumentStatus
{
    /// <summary>Known to the system but not yet trading.</summary>
    Pending = 0,

    /// <summary>Listed and trading.</summary>
    Listed = 1,

    /// <summary>Listed but temporarily halted.</summary>
    Suspended = 2,

    /// <summary>Permanently removed from the exchange. Terminal.</summary>
    Delisted = 3,
}
