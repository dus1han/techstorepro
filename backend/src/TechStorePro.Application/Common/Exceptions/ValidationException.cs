using FluentValidation.Results;

namespace TechStorePro.Application.Common.Exceptions;

/// <summary>
/// Aggregates FluentValidation failures into the shape the API returns to clients
/// (RFC 7807 problem details with an <c>errors</c> dictionary).
/// </summary>
public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures) : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    public IDictionary<string, string[]> Errors { get; }
}
