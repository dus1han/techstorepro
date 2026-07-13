using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Configuration;

public record SettingDto(
    string Key,
    string Module,
    string Name,
    string? Description,
    SettingDataType DataType,
    SettingScope Scope,
    string DefaultValue,
    string EffectiveValue,
    bool IsOverridden,
    DateTimeOffset? ValidFrom);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Settings, PermissionAction.View)]
public record GetSettingsQuery : IRequest<IReadOnlyCollection<SettingDto>>;

public class GetSettingsQueryHandler : IRequestHandler<GetSettingsQuery, IReadOnlyCollection<SettingDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetSettingsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyCollection<SettingDto>> Handle(
        GetSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // The versions in force right now, company-wide.
        var inForce = await _db.SettingValues
            .AsNoTracking()
            .Where(v => v.BranchId == null
                        && v.IsActive
                        && v.ValidFrom <= now
                        && (v.ValidTo == null || v.ValidTo > now))
            .ToListAsync(cancellationToken);

        var current = inForce
            .GroupBy(v => v.Key)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ValidFrom).First());

        return SettingCatalog.All
            .OrderBy(d => d.Module)
            .ThenBy(d => d.Key)
            .Select(d =>
            {
                current.TryGetValue(d.Key, out var value);

                return new SettingDto(
                    d.Key,
                    d.Module,
                    d.Name,
                    d.Description,
                    d.DataType,
                    d.Scope,
                    d.DefaultValue,
                    // A setting the company has never touched reads its default, so the screen always
                    // shows the value actually in force rather than an empty box.
                    value?.Value ?? d.DefaultValue,
                    IsOverridden: value is not null,
                    value?.ValidFrom);
            })
            .ToList();
    }
}

// --- Update -------------------------------------------------------------------------------------

/// <summary>
/// Changes a setting from <paramref name="ValidFrom"/> onwards (default: now).
///
/// This writes a new <em>version</em>. The value in force yesterday is untouched, so a document
/// raised yesterday still resolves yesterday's value — General Rule 3, and the reason this is not
/// simply an UPDATE.
/// </summary>
[RequiresPermission(FeatureCatalog.Settings, PermissionAction.Edit)]
public record UpdateSettingCommand(
    string Key,
    string Value,
    Guid? BranchId = null,
    DateTimeOffset? ValidFrom = null) : IRequest;

public class UpdateSettingCommandValidator : AbstractValidator<UpdateSettingCommand>
{
    public UpdateSettingCommandValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .Must(key => SettingCatalog.Find(key) is not null)
            .WithMessage(x => $"'{x.Key}' is not a known setting.");

        RuleFor(x => x.Value).NotEmpty().MaximumLength(4000);

        RuleFor(x => x)
            .Must(BeParsableAsItsDeclaredType)
            .WithMessage(x => $"'{x.Value}' is not a valid {SettingCatalog.Find(x.Key)?.DataType} value.")
            .When(x => SettingCatalog.Find(x.Key) is not null);

        // A branch override on a company-scoped setting would be silently ignored on read, which is
        // worse than refusing it: the admin would believe they had changed something.
        RuleFor(x => x)
            .Must(x => x.BranchId is null || SettingCatalog.Find(x.Key)?.Scope == SettingScope.Branch)
            .WithMessage(x => $"Setting '{x.Key}' is company-scoped and cannot be overridden per branch.")
            .When(x => SettingCatalog.Find(x.Key) is not null);
    }

    private static bool BeParsableAsItsDeclaredType(UpdateSettingCommand command)
    {
        var definition = SettingCatalog.Find(command.Key);

        return definition?.DataType switch
        {
            SettingDataType.Integer => int.TryParse(command.Value, out _),
            SettingDataType.Decimal => decimal.TryParse(command.Value, out _),
            SettingDataType.Boolean => bool.TryParse(command.Value, out _),
            _ => true
        };
    }
}

public class UpdateSettingCommandHandler : IRequestHandler<UpdateSettingCommand>
{
    private readonly ISettingsProvider _settings;

    public UpdateSettingCommandHandler(ISettingsProvider settings)
    {
        _settings = settings;
    }

    public Task Handle(UpdateSettingCommand request, CancellationToken cancellationToken) =>
        _settings.SetAsync(
            request.Key,
            request.Value,
            request.BranchId,
            request.ValidFrom,
            cancellationToken);
}
