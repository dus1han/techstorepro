namespace TechStorePro.Application.Common.Interfaces;

/// <summary>Injectable clock, so time-dependent business rules stay testable.</summary>
public interface IDateTime
{
    DateTimeOffset UtcNow { get; }
}
