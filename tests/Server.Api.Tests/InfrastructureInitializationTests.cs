using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;

namespace Server.Api.Tests;

public sealed class InfrastructureInitializationTests
{
    [Fact]
    public async Task InitializeServerInfrastructureAsync_AddsMissingAgentColumnsDuringConcurrentStartup()
    {
        var sqlitePath = CreateSqlitePath();
        await CreateLegacyDatabaseSchemaAsync(sqlitePath);
        await using var firstServices = CreateServices(sqlitePath);
        await using var secondServices = CreateServices(sqlitePath);

        await Task.WhenAll(
            firstServices.InitializeServerInfrastructureAsync(),
            secondServices.InitializeServerInfrastructureAsync());

        var columns = await ReadAgentColumnsAsync(sqlitePath);
        Assert.Contains("AgentVersion", columns);
        Assert.Contains("LastPolicyVersion", columns);
    }

    [Fact]
    public async Task InitializeServerInfrastructureAsync_OnFreshDatabase_AppliesInitialMigration()
    {
        var sqlitePath = CreateSqlitePath();
        await using var services = CreateServices(sqlitePath);

        await services.InitializeServerInfrastructureAsync();

        var historyEntries = await ReadMigrationHistoryAsync(sqlitePath);
        Assert.NotEmpty(historyEntries);
        Assert.Contains(historyEntries, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));

        var columns = await ReadAgentColumnsAsync(sqlitePath);
        Assert.Contains("AgentVersion", columns);
    }

    [Fact]
    public async Task InitializeServerInfrastructureAsync_IsIdempotentWhenRunRepeatedlyOnStampedLegacyDatabase()
    {
        var sqlitePath = CreateSqlitePath();
        await CreateLegacyDatabaseSchemaAsync(sqlitePath);

        await using (var first = CreateServices(sqlitePath))
        {
            await first.InitializeServerInfrastructureAsync();
        }

        await using (var second = CreateServices(sqlitePath))
        {
            await second.InitializeServerInfrastructureAsync();
        }

        var historyEntries = await ReadMigrationHistoryAsync(sqlitePath);
        Assert.Equal(historyEntries.Count, historyEntries.Distinct().Count());
        Assert.Contains(historyEntries, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeServerInfrastructureAsync_OnLegacyDatabase_StampsHistoryWithoutLosingData()
    {
        var sqlitePath = CreateSqlitePath();
        await CreateLegacyDatabaseSchemaAsync(sqlitePath);
        await SeedAgentRowAsync(sqlitePath, "agent-legacy", "host-1");

        await using var services = CreateServices(sqlitePath);
        await services.InitializeServerInfrastructureAsync();

        var historyEntries = await ReadMigrationHistoryAsync(sqlitePath);
        Assert.Contains(historyEntries, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));

        var preservedAgentId = await ReadFirstAgentIdAsync(sqlitePath);
        Assert.Equal("agent-legacy", preservedAgentId);
    }

    [Fact]
    public async Task InitializeServerInfrastructureAsync_OnPartialLegacySchema_FailsLoudlyWithoutStamping()
    {
        var sqlitePath = CreateSqlitePath();
        await CreatePartialLegacyTablesAsync(sqlitePath);
        await using var services = CreateServices(sqlitePath);

        await Assert.ThrowsAnyAsync<Exception>(() => services.InitializeServerInfrastructureAsync());

        // EF may create __EFMigrationsHistory before the failing CREATE TABLE; the contract is
        // that no migration is stamped, leaving the DB in a state that re-run will surface again.
        if (await TableExistsAsync(sqlitePath, "__EFMigrationsHistory"))
        {
            var history = await ReadMigrationHistoryAsync(sqlitePath);
            Assert.Empty(history);
        }
    }

    private static async Task CreatePartialLegacyTablesAsync(string sqlitePath)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Children (
                ChildId TEXT NOT NULL CONSTRAINT PK_Children PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                DailyLimitMinutes INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE Agents (
                AgentId TEXT NOT NULL CONSTRAINT PK_Agents PRIMARY KEY,
                Hostname TEXT NOT NULL,
                LocalUser TEXT NULL,
                ChildId TEXT NULL,
                LastSeenAtUtc TEXT NOT NULL,
                LastUsageReportAtUtc TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string sqlitePath, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static ServiceProvider CreateServices(string sqlitePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionGuard:Storage:SqlitePath"] = sqlitePath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddServerInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task CreateLegacyDatabaseSchemaAsync(string sqlitePath)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Children (
                ChildId TEXT NOT NULL CONSTRAINT PK_Children PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                DailyLimitMinutes INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE Agents (
                AgentId TEXT NOT NULL CONSTRAINT PK_Agents PRIMARY KEY,
                Hostname TEXT NOT NULL,
                LocalUser TEXT NULL,
                ChildId TEXT NULL,
                LastSeenAtUtc TEXT NOT NULL,
                LastUsageReportAtUtc TEXT NULL,
                CONSTRAINT FK_Agents_Children_ChildId FOREIGN KEY (ChildId) REFERENCES Children (ChildId) ON DELETE SET NULL
            );
            CREATE INDEX IX_Agents_ChildId ON Agents (ChildId);
            CREATE TABLE UsageReports (
                Id INTEGER NOT NULL CONSTRAINT PK_UsageReports PRIMARY KEY AUTOINCREMENT,
                AgentId TEXT NOT NULL,
                ChildId TEXT NOT NULL,
                LocalUser TEXT NOT NULL,
                UsageDateUtc TEXT NOT NULL,
                UsedMinutes INTEGER NOT NULL,
                ReportedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_UsageReports_Agents_AgentId FOREIGN KEY (AgentId) REFERENCES Agents (AgentId) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_UsageReports_AgentId_UsageDateUtc ON UsageReports (AgentId, UsageDateUtc);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAgentRowAsync(string sqlitePath, string agentId, string hostname)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Agents (AgentId, Hostname, LastSeenAtUtc) VALUES ($id, $host, $seen)";
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "$id";
        idParameter.Value = agentId;
        command.Parameters.Add(idParameter);
        var hostParameter = command.CreateParameter();
        hostParameter.ParameterName = "$host";
        hostParameter.Value = hostname;
        command.Parameters.Add(hostParameter);
        var seenParameter = command.CreateParameter();
        seenParameter.ParameterName = "$seen";
        seenParameter.Value = DateTimeOffset.UtcNow.ToString("O");
        command.Parameters.Add(seenParameter);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> ReadAgentColumnsAsync(string sqlitePath)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Agents)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<List<string>> ReadMigrationHistoryAsync(string sqlitePath)
    {
        var ids = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task<string?> ReadFirstAgentIdAsync(string sqlitePath)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AgentId FROM Agents LIMIT 1";
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    private static string CreateSqlitePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionguard-infrastructure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "sessionguard.db");
    }
}
