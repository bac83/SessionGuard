namespace Agent.Linux;

internal sealed record UsageStateFile(
    Dictionary<string, UsageStateEntry> Users);

internal sealed record UsageStateEntry(
    DateOnly Day,
    DateTimeOffset LastSeen,
    int UsedMinutes);
