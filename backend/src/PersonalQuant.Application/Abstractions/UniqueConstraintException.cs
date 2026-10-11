namespace PersonalQuant.Application.Abstractions;

/// <summary>
/// Raised by <see cref="IUnitOfWork.SaveChangesAsync"/> when a write lost a
/// race to a uniqueness rule the database enforces.
/// </summary>
/// <remarks>
/// A service checks a rule before writing and the database enforces it again
/// at commit; between the two, a concurrent request can take the name. This
/// lets the service answer that as the conflict it is, rather than as a server
/// failure, without the application layer knowing which database said so.
/// </remarks>
public sealed class UniqueConstraintException : Exception
{
    /// <summary>Initialises the exception.</summary>
    public UniqueConstraintException()
    {
    }

    /// <summary>Initialises the exception with a message.</summary>
    /// <param name="message">What happened.</param>
    public UniqueConstraintException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises the exception with the constraint and the cause.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">The database's own error.</param>
    public UniqueConstraintException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets or initialises the name of the constraint that refused the write, when known.</summary>
    public string? ConstraintName { get; init; }
}
