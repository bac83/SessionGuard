using System.Text.RegularExpressions;

namespace Agent.Linux;

internal static partial class LinuxIdentifiers
{
    public static bool IsValidLocalUser(string? value) =>
        !string.IsNullOrWhiteSpace(value) && LocalUserRegex().IsMatch(value);

    public static bool IsNumericId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && NumericIdRegex().IsMatch(value);

    public static bool IsXDisplay(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DisplayRegex().IsMatch(value);

    [GeneratedRegex(@"^[a-z_][a-z0-9_-]*[$]?$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalUserRegex();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumericIdRegex();

    [GeneratedRegex(@"^:\d+(\.\d+)?$")]
    private static partial Regex DisplayRegex();
}
