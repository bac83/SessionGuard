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
        await CreateLegacyAgentsTableAsync(sqlitePath);

        await Task.WhenAll(
            CreateServices(sqlitePath).InitializeServerInfrastructureAsync(),
            CreateServices(sqlitePath).InitializeServerInfrastructureAsync());

        var columns = await ReadAgentColumnsAsync(sqlitePath);
        Assert.Contains("AgentVersion", columns);
        Assert.Contains("LastPolicyVersion", columns);
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

    private static async Task CreateLegacyAgentsTableAsync(string sqlitePath)
    {
        await using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
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

    private static string CreateSqlitePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionguard-infrastructure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "sessionguard.db");
    }
}
