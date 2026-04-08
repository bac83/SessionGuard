namespace Server.Ui.Services;

public sealed class SessionGuardApiOptions
{
    public const string SectionName = "SessionGuard";
    public string ApiBaseUrl { get; set; } = "http://localhost:8080";
}
