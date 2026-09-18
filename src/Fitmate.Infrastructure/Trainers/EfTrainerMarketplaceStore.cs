using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Fitmate.Infrastructure.Trainers;

/// <summary>
/// EF Core impl cho marketplace read. Verified = Certificate.Status == Approved.
/// Dùng cursor pagination theo (Rating desc, UserId asc) để ổn định giữa các lần truy vấn.
/// </summary>
public class EfTrainerMarketplaceStore : ITrainerMarketplaceStore
{
    private readonly FitmateDbContext _db;
    public EfTrainerMarketplaceStore(FitmateDbContext db) { _db = db; }

    public async Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListVerifiedAsync(TrainerMarketplaceQuery q, CancellationToken ct)
    {
        // Subquery: distinct PT userIds có ít nhất 1 Certificate Approved.
        var verifiedPtIds = _db.Certificates
            .Where(c => c.Status == CertificateStatus.Approved)
            .Select(c => c.PtUserId)
            .Distinct();

        var profiles = await _db.PtProfiles
            .Where(p => verifiedPtIds.Contains(p.UserId))
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.UserId)
            .ToListAsync(ct);

        // Apply filter post-EF (chuyên ngành + price + rating).
        IEnumerable<PtProfile> filtered = profiles;
        if (!string.IsNullOrWhiteSpace(q.Specialty))
            filtered = filtered.Where(p => p.Specialty != null && p.Specialty.Contains(q.Specialty, StringComparison.OrdinalIgnoreCase));
        if (q.MinPriceVnd.HasValue)
            filtered = filtered.Where(p => p.PricePerSessionVnd.HasValue && p.PricePerSessionVnd.Value >= q.MinPriceVnd.Value);
        if (q.MaxPriceVnd.HasValue)
            filtered = filtered.Where(p => p.PricePerSessionVnd.HasValue && p.PricePerSessionVnd.Value <= q.MaxPriceVnd.Value);
        if (q.MinRating.HasValue)
            filtered = filtered.Where(p => p.Rating >= q.MinRating.Value);

        var arr = filtered.ToList();
        // Cursor đơn giản: index của start.
        int startIndex = 0;
        if (!string.IsNullOrEmpty(q.Cursor) && int.TryParse(q.Cursor, out var idx))
            startIndex = Math.Min(Math.Max(idx, 0), arr.Count);
        var page = arr.Skip(startIndex).Take(q.Limit + 1).ToList();
        var hasMore = page.Count > q.Limit;
        if (hasMore) page.RemoveAt(page.Count - 1);
        var nextCursor = hasMore ? (startIndex + page.Count).ToString() : null;

        // Project certificate approved cho mỗi PT (1 query riêng).
        var ptIds = page.Select(p => p.UserId).ToList();
        var approvedCerts = await _db.Certificates
            .Where(c => c.Status == CertificateStatus.Approved && ptIds.Contains(c.PtUserId))
            .Select(c => c.PtUserId)
            .ToListAsync(ct);
        var verifiedSet = approvedCerts.ToHashSet();

        var items = page
            .Where(p => verifiedSet.Contains(p.UserId))
            .Select(p => new TrainerMarketplaceSummary(
                Id: p.Id,
                UserId: p.UserId,
                FullName: null,
                AvatarUrl: null,
                Specialty: p.Specialty,
                ExperienceYears: p.ExperienceYears,
                PricePerSessionVnd: p.PricePerSessionVnd,
                Rating: p.Rating,
                ReviewsCount: p.ReviewsCount,
                IsVerified: true))
            .ToList();

        return (items, nextCursor);
    }

    public async Task<TrainerMarketplaceDetail?> FindDetailAsync(Guid ptUserId, CancellationToken ct)
    {
        // 1+ Approved certificate là điều kiện xuất hiện trên marketplace.
        var hasApproved = await _db.Certificates
            .AnyAsync(c => c.PtUserId == ptUserId && c.Status == CertificateStatus.Approved, ct);
        if (!hasApproved) return null;

        var profile = await _db.PtProfiles.FirstOrDefaultAsync(p => p.UserId == ptUserId, ct);
        if (profile is null) return null;

        var services = await _db.Services
            .Where(s => s.PtUserId == ptUserId && s.IsActive)
            .OrderBy(s => s.PriceVnd)
            .ToListAsync(ct);

        var summary = new TrainerMarketplaceSummary(
            Id: profile.Id,
            UserId: profile.UserId,
            FullName: null,
            AvatarUrl: null,
            Specialty: profile.Specialty,
            ExperienceYears: profile.ExperienceYears,
            PricePerSessionVnd: profile.PricePerSessionVnd,
            Rating: profile.Rating,
            ReviewsCount: profile.ReviewsCount,
            IsVerified: true);

        return new TrainerMarketplaceDetail(summary, Bio: profile.Bio, Services: services);
    }
}
