namespace SaasEngine.Domain.Shared;

/// <summary>
/// Base class for all domain-specific exceptions.
/// All exceptions that cross slice boundaries must inherit from this class.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Initializes a new instance of <see cref="DomainException"/>.</summary>
    /// <param name="message">The error message.</param>
    protected DomainException(string message) : base(message) { }

    /// <summary>Initializes a new instance with an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
