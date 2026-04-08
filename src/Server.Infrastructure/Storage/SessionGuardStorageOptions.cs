namespace Server.Infrastructure.Storage;

public sealed class SessionGuardStorageOptions
{
    public const string SectionName = "SessionGuard:Storage";
    public string SqlitePath { get; set; } = "/data/sessionguard.db";
}
