using System.Reflection;
using TechStorePro.Application.Common.Behaviours;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace TechStorePro.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters. Permission is checked before validation so that a caller who may not
            // touch a feature learns nothing about its input rules — validation errors are a
            // description of the request shape, and describing it to someone denied the feature is a
            // small leak that costs nothing to close.
            cfg.AddOpenBehavior(typeof(PermissionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });

        return services;
    }
}
