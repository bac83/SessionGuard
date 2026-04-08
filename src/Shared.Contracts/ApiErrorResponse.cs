namespace Shared.Contracts;

public sealed record ApiErrorResponse(
    string Error,
    string Message);
