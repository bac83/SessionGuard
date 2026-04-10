using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
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
        await EnsureColumnAsync(dbContext, "Agents", "AgentVersion", "TEXT", cancellationToken);
        await EnsureColumnAsync(dbContext, "Agents", "LastPolicyVersion", "TEXT", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SessionGuardDbContext dbContext,
        string tableName,
        string columnName,
        string columnType,
        CancellationToken cancellationToken)
    {
        ValidateSqliteIdentifier(tableName);
        ValidateSqliteIdentifier(columnName);
        ValidateSqliteType(columnType);

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
            {
                return;
            }

            await AddColumnAsync(connection, tableName, columnName, columnType, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task AddColumnAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName,
        string columnType,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
                await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                return;
            }
            catch (SqliteException ex) when (IsDuplicateColumn(ex))
            {
                if (await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
                {
                    return;
                }

                throw;
            }
            catch (SqliteException ex) when (IsTransientLock(ex) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsDuplicateColumn(SqliteException exception) =>
        exception.SqliteErrorCode == 1
        && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientLock(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6;

    private static void ValidateSqliteIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
        {
            throw new InvalidOperationException($"Invalid SQLite identifier '{value}'.");
        }
    }

    private static void ValidateSqliteType(string value)
    {
        if (!string.Equals(value, "TEXT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported SQLite column type '{value}'.");
        }
    }
}
