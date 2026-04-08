using System.Text.Json.Serialization;

namespace Shared.Contracts;

[JsonSerializable(typeof(PolicyFetchResponse))]
[JsonSerializable(typeof(ChildPolicy))]
[JsonSerializable(typeof(CachedPolicyState))]
[JsonSerializable(typeof(AgentStatusSnapshot))]
[JsonSerializable(typeof(DashboardResponse))]
[JsonSerializable(typeof(ChildSummary))]
[JsonSerializable(typeof(AgentStatusSummary))]
[JsonSerializable(typeof(UpsertChildRequest))]
[JsonSerializable(typeof(UsageReportRequest))]
[JsonSerializable(typeof(UsageReportResponse))]
[JsonSerializable(typeof(List<UserChildMapping>))]
public partial class SessionGuardJsonContext : JsonSerializerContext;
