namespace TechStorePro.Domain.Exceptions;

/// <summary>
/// Thrown when an operation would violate a business invariant. Surfaced to callers as
/// HTTP 400 by the API exception handler.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
