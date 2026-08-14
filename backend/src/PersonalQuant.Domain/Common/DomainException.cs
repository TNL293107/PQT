namespace PersonalQuant.Domain.Common;

/// <summary>
/// Base type for every failure that represents a violated domain rule.
/// </summary>
/// <remarks>
/// Domain failures are distinguished from infrastructure failures so the API
/// layer can map them to meaningful status codes rather than a blanket 500.
/// </remarks>
public class DomainException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    public DomainException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">A description of the violated rule.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">A description of the violated rule.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
