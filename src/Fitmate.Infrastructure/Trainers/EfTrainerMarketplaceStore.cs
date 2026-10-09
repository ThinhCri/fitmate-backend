using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;

namespace Fitmate.Infrastructure.Trainers;

/// <summary>
/// EF Core impl cho marketplace read. Verified = Certificate.Status == Approved.
/// Dùng cursor pagination theo (Rating desc, UserId asc) để ổn định giữa các lần truy vấn.
/// </summary>
public class EfTrainerMarketplaceStore : ITrainerMarketplaceStore
{
    private readonly FitmateDbContext _db;
    private readonly bool _onlineEnabled;
    public EfTrainerMarketplaceStore(FitmateDbContext db, IOptions<GoogleMeetOptions>? meetOptions = null)
    {
        _db = db;
        _onlineEnabled = meetOptions?.Value.IsConfigured() ?? false;
    }

    public async Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListVerifiedAsync(TrainerMarketplaceQuery q, CancellationToken ct)
    {
        var onlineEnabled = _onlineEnabled;
        // Subquery: distinct PT userIds có ít nhất 1 Certificate Approved.
        var verifiedPtIds = _db.Certificates
            .Where(c => c.Status == CertificateStatus.Approved)
            .Select(c => c.PtUserId)
            .Distinct();

        var profiles = await _db.PtProfiles
            .Where(p => p.VerificationStatus == PtVerificationStatus.Approved &&
                verifiedPtIds.Contains(p.UserId) &&
                _db.Users.Any(u => u.Id == p.UserId && u.Status == UserStatus.Active &&
                    u.EmailConfirmed && u.PhoneNumberConfirmed) &&
                _db.Services.Any(s => s.PtUserId == p.UserId && s.IsActive &&
                    s.TargetAudience != null && s.RequiredEquipment != null && s.Deliverable != null &&
                    ((s.DeliveryMode == ServiceDeliveryMode.InPerson && s.LocationName != null && s.LocationAddress != null) ||
                     (onlineEnabled && s.DeliveryMode == ServiceDeliveryMode.Online &&
                      _db.UserTokens.Any(t => t.UserId == p.UserId &&
                          t.LoginProvider == GoogleMeetConnectionService.Provider &&
                          t.Name == GoogleMeetConnectionService.ConnectionName)))))
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.UserId)
            .ToListAsync(ct);

        var profileIds = profiles.Select(p => p.UserId).ToArray();
        var activePrices = (await _db.Services
            .Where(s => profileIds.Contains(s.PtUserId) && s.IsActive &&
                s.TargetAudience != null && s.RequiredEquipment != null && s.Deliverable != null &&
                ((s.DeliveryMode == ServiceDeliveryMode.InPerson && s.LocationName != null && s.LocationAddress != null) ||
                 (onlineEnabled && s.DeliveryMode == ServiceDeliveryMode.Online &&
                  _db.UserTokens.Any(t => t.UserId == s.PtUserId &&
                      t.LoginProvider == GoogleMeetConnectionService.Provider &&
                      t.Name == GoogleMeetConnectionService.ConnectionName))))
            .Select(s => new { s.PtUserId, s.PriceVnd })
            .ToListAsync(ct))
            .GroupBy(s => s.PtUserId)
            .ToDictionary(g => g.Key, g => g.Min(s => s.PriceVnd));

        // Apply filter post-EF (chuyên ngành + price + rating).
        IEnumerable<PtProfile> filtered = profiles;
        if (!string.IsNullOrWhiteSpace(q.Specialty))
            filtered = filtered.Where(p => p.Specialty != null && p.Specialty.Contains(q.Specialty, StringComparison.OrdinalIgnoreCase));
        if (q.MinPriceVnd.HasValue)
            filtered = filtered.Where(p => activePrices.TryGetValue(p.UserId, out var price) && price >= q.MinPriceVnd.Value);
        if (q.MaxPriceVnd.HasValue)
            filtered = filtered.Where(p => activePrices.TryGetValue(p.UserId, out var price) && price <= q.MaxPriceVnd.Value);
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
        var userDetails = await _db.Users
            .Where(u => ptIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
            .ToDictionaryAsync(u => u.Id, ct);

        var items = page
            .Where(p => verifiedSet.Contains(p.UserId))
            .Select(p => new TrainerMarketplaceSummary(
                Id: p.Id,
                UserId: p.UserId,
                FullName: userDetails[p.UserId].FullName,
                AvatarUrl: userDetails[p.UserId].AvatarUrl,
                Specialty: p.Specialty,
                ExperienceYears: p.ExperienceYears,
                PricePerSessionVnd: activePrices.GetValueOrDefault(p.UserId),
                Rating: p.Rating,
                ReviewsCount: p.ReviewsCount,
                IsVerified: true))
            .ToList();

        return (items, nextCursor);
    }

    public async Task<TrainerMarketplaceDetail?> FindDetailAsync(Guid ptUserId, CancellationToken ct)
    {
        // Certificate alone is insufficient: an approved, active PT account is also required.
        var hasApproved = await _db.Certificates
            .AnyAsync(c => c.PtUserId == ptUserId && c.Status == CertificateStatus.Approved, ct);
        if (!hasApproved) return null;

        var profile = await _db.PtProfiles.FirstOrDefaultAsync(p => p.UserId == ptUserId, ct);
        if (profile is null || profile.VerificationStatus != PtVerificationStatus.Approved ||
            !await _db.Users.AnyAsync(u => u.Id == ptUserId && u.Status == UserStatus.Active &&
                u.EmailConfirmed && u.PhoneNumberConfirmed, ct)) return null;

        var onlineEnabled = _onlineEnabled;
        var services = await _db.Services
            .Where(s => s.PtUserId == ptUserId && s.IsActive &&
                s.TargetAudience != null && s.RequiredEquipment != null && s.Deliverable != null &&
                ((s.DeliveryMode == ServiceDeliveryMode.InPerson && s.LocationName != null && s.LocationAddress != null) ||
                 (onlineEnabled && s.DeliveryMode == ServiceDeliveryMode.Online &&
                  _db.UserTokens.Any(t => t.UserId == ptUserId &&
                      t.LoginProvider == GoogleMeetConnectionService.Provider &&
                      t.Name == GoogleMeetConnectionService.ConnectionName))))
            .OrderBy(s => s.PriceVnd)
            .ToListAsync(ct);
        if (services.Count == 0) return null;

        var user = await _db.Users.Where(u => u.Id == ptUserId)
            .Select(u => new { u.FullName, u.AvatarUrl }).SingleAsync(ct);
        var summary = new TrainerMarketplaceSummary(
            Id: profile.Id,
            UserId: profile.UserId,
            FullName: user.FullName,
            AvatarUrl: user.AvatarUrl,
            Specialty: profile.Specialty,
            ExperienceYears: profile.ExperienceYears,
            PricePerSessionVnd: services.Min(s => s.PriceVnd),
            Rating: profile.Rating,
            ReviewsCount: profile.ReviewsCount,
            IsVerified: true);

        return new TrainerMarketplaceDetail(summary, Bio: profile.Bio, Services: services);
    }
}
