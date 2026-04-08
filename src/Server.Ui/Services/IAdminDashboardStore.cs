using Server.Ui.Models;

namespace Server.Ui.Services;

public interface IAdminDashboardStore
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<ChildProfile> AddChildAsync(ChildProfileDraft draft, CancellationToken cancellationToken = default);

    Task<ChildProfile> UpdateChildBudgetAsync(string childId, int dailyBudgetMinutes, CancellationToken cancellationToken = default);

    Task<ChildProfile> SetChildActiveAsync(string childId, bool isActive, CancellationToken cancellationToken = default);
}
