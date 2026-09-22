namespace Fitmate.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryView> GetSummaryAsync(Guid userId, DateOnly? date = null, CancellationToken ct = default);
}

