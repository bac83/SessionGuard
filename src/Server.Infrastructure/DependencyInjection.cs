using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State == ConnectionState.Closed;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (await IsLegacyDatabaseAsync(connection, cancellationToken))
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                // Re-check inside the transaction: another startup may have stamped history between
                // the outer probe and acquiring the write lock. Cheap query; avoids redundant work.
                if (await IsLegacyDatabaseAsync(connection, cancellationToken))
                {
                    await BringLegacyAgentsTableUpToDateAsync(connection, cancellationToken);
                    await StampAllMigrationsAsAppliedAsync(dbContext, connection, cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BringLegacyAgentsTableUpToDateAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await EnsureLegacyColumnAsync(connection, "Agents", "AgentVersion", cancellationToken);
        await EnsureLegacyColumnAsync(connection, "Agents", "LastPolicyVersion", cancellationToken);
    }

    private static async Task EnsureLegacyColumnAsync(DbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        if (!IsSafeIdentifier(tableName) || !IsSafeIdentifier(columnName))
        {
            throw new InvalidOperationException($"Refused unsafe identifier for legacy upgrade: {tableName}.{columnName}");
        }

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({tableName})";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} TEXT";
        try
        {
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1
            && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Another startup added the column between the PRAGMA scan and our ALTER.
            // Message-based gating is intentional: SqliteErrorCode 1 is the generic SQL error,
            // and SQLite ships error messages in English regardless of the host locale, so
            // matching on "duplicate column" is stable across deployments.
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    private static async Task<bool> IsLegacyDatabaseAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken))
        {
            return false;
        }

        // Treat as legacy only when the full pre-migration schema is present. A partially
        // initialized DB (some tables missing) falls through to MigrateAsync, which will
        // fail loudly rather than stamp an incomplete schema as already migrated.
        return await TableExistsAsync(connection, "Children", cancellationToken)
            && await TableExistsAsync(connection, "Agents", cancellationToken)
            && await TableExistsAsync(connection, "UsageReports", cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task StampAllMigrationsAsAppliedAsync(
        SessionGuardDbContext dbContext,
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var historyRepository = dbContext.GetService<IHistoryRepository>();
        var migrationsAssembly = dbContext.GetService<IMigrationsAssembly>();
        // ProductVersion is informational only — written to __EFMigrationsHistory.ProductVersion and
        // not used by EF for migration matching. Fallback "10.0.0" only fires under trimmed/AOT builds
        // where the EF assembly version metadata is stripped.
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0";

        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = historyRepository.GetCreateIfNotExistsScript();
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migrationId in migrationsAssembly.Migrations.Keys)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = historyRepository.GetInsertScript(new HistoryRow(migrationId, productVersion));
            try
            {
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 19 or 1555)
            {
                // 19 = SQLITE_CONSTRAINT, 1555 = SQLITE_CONSTRAINT_PRIMARYKEY.
                // Another startup already stamped this migration; safe to ignore.
            }
        }
    }
}
