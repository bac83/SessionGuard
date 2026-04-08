using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Infrastructure.Persistence;
using Server.Infrastructure.Services;
using Server.Infrastructure.Storage;

namespace Server.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddServerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionGuardStorageOptions>()
            .Bind(configuration.GetSection(SessionGuardStorageOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.SqlitePath), "A SQLite path is required.");

        services.AddDbContext<SessionGuardDbContext>((serviceProvider, optionsBuilder) =>
        {
            var storageOptions = serviceProvider.GetRequiredService<IOptions<SessionGuardStorageOptions>>().Value;
            var sqlitePath = Path.GetFullPath(storageOptions.SqlitePath);
            var directory = Path.GetDirectoryName(sqlitePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionsBuilder.UseSqlite($"Data Source={sqlitePath}");
        });

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISessionGuardRepository, SessionGuardRepository>();
        return services;
    }

    public static async Task InitializeServerInfrastructureAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SessionGuardDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
