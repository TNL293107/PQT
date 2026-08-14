namespace PersonalQuant.Domain.Common;

/// <summary>
/// Raised when a value cannot form a valid domain type.
/// </summary>
/// <remarks>
/// Represents malformed input. A future API layer maps this to
/// 400 Bad Request.
/// </remarks>
public sealed class DomainValidationException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    public DomainValidationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    /// <param name="message">A description of why the value is invalid.</param>
    public DomainValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    /// <param name="message">A description of why the value is invalid.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
