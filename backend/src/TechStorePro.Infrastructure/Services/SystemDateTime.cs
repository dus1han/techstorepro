using TechStorePro.Application.Common.Interfaces;

namespace TechStorePro.Infrastructure.Services;

public class SystemDateTime : IDateTime
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
