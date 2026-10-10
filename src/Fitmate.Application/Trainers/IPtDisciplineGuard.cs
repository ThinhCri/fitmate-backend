namespace Fitmate.Application.Trainers;

public interface IPtDisciplineGuard
{
    Task EnsureMayPublishAsync(Guid ptUserId, CancellationToken ct);
}
