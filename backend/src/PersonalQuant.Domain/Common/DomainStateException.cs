namespace PersonalQuant.Domain.Common;

/// <summary>
/// Raised when an operation is not legal for an entity's current state.
/// </summary>
/// <remarks>
/// Represents a well-formed request against an entity that cannot accept it —
/// for example resuming trading in a delisted instrument. A future API layer
/// maps this to 409 Conflict.
/// </remarks>
public sealed class DomainStateException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="DomainStateException"/> class.</summary>
    public DomainStateException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainStateException"/> class.</summary>
    /// <param name="message">A description of why the transition is illegal.</param>
    public DomainStateException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainStateException"/> class.</summary>
    /// <param name="message">A description of why the transition is illegal.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DomainStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
