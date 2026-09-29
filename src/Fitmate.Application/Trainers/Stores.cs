using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Application abstraction cho PtProfile persistence. EF Core impl ở Infrastructure.
/// </summary>
public interface IPtProfileStore
{
    Task<PtProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(PtProfile profile, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Application abstraction cho Certificate persistence + User lookup (để check role).
/// </summary>
public interface ICertificateStore
{
    Task<Certificate?> FindByIdentityAsync(Guid ptUserId, string title, string issuedBy, string code, CancellationToken ct);
    Task<Certificate?> FindByIdAsync(Guid certId, CancellationToken ct);
    /// <summary>
    /// Lists every certificate belonging to a PT. Bounded to <paramref name="pageSize"/>.
    /// Used by admin preview/list screens where the count can grow unbounded.
    /// </summary>
    Task<(IReadOnlyList<Certificate> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct);
    Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingPagedAsync(int page, int pageSize, CancellationToken ct);
    Task AddAsync(Certificate cert, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Lookup user + role check. Application abstraction; Identity UserManager impl ở Infrastructure.
/// </summary>
public interface IUserRoleLookup
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct);
    Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct);
    /// <summary>Trả về primary role của user, hoặc null nếu user không có role nào (member mặc định).</summary>
    Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct);
}
