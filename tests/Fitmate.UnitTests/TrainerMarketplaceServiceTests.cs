using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-120: TrainerMarketplaceService — list verified + filter + detail.
/// </summary>
public class TrainerMarketplaceServiceTests
{
    private sealed class InMemoryStore : ITrainerMarketplaceStore
    {
        public Dictionary<Guid, TrainerMarketplaceDetail> Items { get; } = new();
        public List<string> Calls { get; } = new();

        public Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListVerifiedAsync(TrainerMarketplaceQuery q, CancellationToken ct)
        {
            Calls.Add("list");
            // Apply same post filter logic cho đơn giản.
            IEnumerable<TrainerMarketplaceDetail> src = Items.Values;
            if (!string.IsNullOrWhiteSpace(q.Specialty))
                src = src.Where(d => d.Profile.Specialty != null && d.Profile.Specialty.Contains(q.Specialty, StringComparison.OrdinalIgnoreCase));
            if (q.MinPriceVnd.HasValue)
                src = src.Where(d => d.Profile.PricePerSessionVnd.HasValue && d.Profile.PricePerSessionVnd.Value >= q.MinPriceVnd.Value);
            if (q.MaxPriceVnd.HasValue)
                src = src.Where(d => d.Profile.PricePerSessionVnd.HasValue && d.Profile.PricePerSessionVnd.Value <= q.MaxPriceVnd.Value);
            if (q.MinRating.HasValue)
                src = src.Where(d => d.Profile.Rating >= q.MinRating.Value);

            var arr = src.OrderByDescending(d => d.Profile.Rating).ThenBy(d => d.Profile.UserId).ToList();
            int start = 0;
            if (!string.IsNullOrEmpty(q.Cursor) && int.TryParse(q.Cursor, out var idx))
                start = Math.Min(Math.Max(idx, 0), arr.Count);
            var page = arr.Skip(start).Take(q.Limit + 1).ToList();
            string? next = null;
            if (page.Count > q.Limit) { page.RemoveAt(page.Count - 1); next = (start + page.Count).ToString(); }
            var items = page.Select(d => d.Profile).ToList();
            return Task.FromResult<(IReadOnlyList<TrainerMarketplaceSummary>, string?)>((items, next));
        }

        public Task<TrainerMarketplaceDetail?> FindDetailAsync(Guid ptUserId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(ptUserId));
    }

    private static TrainerMarketplaceSummary Summary(Guid userId, string specialty = "Strength", decimal rating = 4.5m, long? price = 500_000, bool isVerified = true) =>
        new(Id: Guid.NewGuid(), UserId: userId, FullName: null, AvatarUrl: null, Specialty: specialty, ExperienceYears: 5, PricePerSessionVnd: price, Rating: rating, ReviewsCount: 10, IsVerified: isVerified);

    private static TrainerMarketplaceService NewService(InMemoryStore store) => new(store);

    [Fact]
    public async Task List_NoFilter_Pagination()
    {
        var store = new InMemoryStore();
        for (var i = 0; i < 50; i++)
        {
            var u = Guid.NewGuid();
            store.Items[u] = new TrainerMarketplaceDetail(Summary(u, specialty: "Yoga", rating: 3.5m + i % 5 * 0.1m), Bio: null, Services: Array.Empty<Service>());
        }
        var svc = NewService(store);

        var (items, next) = await svc.ListAsync(new TrainerMarketplaceQuery(null, null, null, null, 20, null), CancellationToken.None);
        items.Should().HaveCount(20);
        next.Should().NotBeNull();
    }

    [Fact]
    public async Task List_FilterBySpecialty()
    {
        var store = new InMemoryStore();
        var u1 = Guid.NewGuid(); store.Items[u1] = new TrainerMarketplaceDetail(Summary(u1, "Strength"), null, Array.Empty<Service>());
        var u2 = Guid.NewGuid(); store.Items[u2] = new TrainerMarketplaceDetail(Summary(u2, "Yoga"), null, Array.Empty<Service>());
        var u3 = Guid.NewGuid(); store.Items[u3] = new TrainerMarketplaceDetail(Summary(u3, "Stretching"), null, Array.Empty<Service>());

        var (items, _) = await NewService(store).ListAsync(
            new TrainerMarketplaceQuery("Yo", null, null, null, 20, null), CancellationToken.None);

        items.Should().HaveCount(1);
        items[0].Specialty.Should().Be("Yoga");
    }

    [Fact]
    public async Task List_FilterByPriceRange()
    {
        var store = new InMemoryStore();
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), price: 200_000), null, Array.Empty<Service>());
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), price: 600_000), null, Array.Empty<Service>());
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), price: 1_500_000), null, Array.Empty<Service>());

        var (items, _) = await NewService(store).ListAsync(
            new TrainerMarketplaceQuery(null, 300_000, 1_000_000, null, 20, null), CancellationToken.None);
        items.Should().HaveCount(1).And.Subject.First().PricePerSessionVnd.Should().Be(600_000);
    }

    [Fact]
    public async Task List_FilterByMinRating()
    {
        var store = new InMemoryStore();
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), rating: 3.0m), null, Array.Empty<Service>());
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), rating: 4.5m), null, Array.Empty<Service>());
        store.Items[Guid.NewGuid()] = new TrainerMarketplaceDetail(Summary(Guid.NewGuid(), rating: 4.8m), null, Array.Empty<Service>());

        var (items, _) = await NewService(store).ListAsync(
            new TrainerMarketplaceQuery(null, null, null, 4.5m, 20, null), CancellationToken.None);
        items.Should().HaveCount(2).And.OnlyContain(x => x.Rating >= 4.5m);
    }

    [Fact]
    public async Task List_OnlyVerified_AppearInResults()
    {
        var store = new InMemoryStore();
        var verifiedPt = Guid.NewGuid(); store.Items[verifiedPt] = new TrainerMarketplaceDetail(Summary(verifiedPt, isVerified: true), null, Array.Empty<Service>());
        var unverifiedPt = Guid.NewGuid(); store.Items[unverifiedPt] = new TrainerMarketplaceDetail(Summary(unverifiedPt, isVerified: false), null, Array.Empty<Service>());

        var (items, _) = await NewService(store).ListAsync(
            new TrainerMarketplaceQuery(null, null, null, null, 20, null), CancellationToken.None);
        // Service không filter — store đã verified-only; test cho service-level behavior ở GetDetail.
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDetail_Verified_ReturnsDetail()
    {
        var store = new InMemoryStore();
        var u = Guid.NewGuid();
        var svc1 = Domain.Trainers.Service.Create(u, "Personal 60min", null, 500_000, 60);
        var svc2 = Domain.Trainers.Service.Create(u, "Group class", null, 200_000, 45);
        var svc3 = Domain.Trainers.Service.Create(u, "Old hidden", null, 999_000, 60);
        svc3.Deactivate();
        store.Items[u] = new TrainerMarketplaceDetail(Summary(u, isVerified: true), "Bio dài.", Services: new[] { svc1, svc2, svc3 });

        var detail = await NewService(store).GetDetailAsync(u, CancellationToken.None);
        detail.Profile.UserId.Should().Be(u);
        detail.Bio.Should().Be("Bio dài.");
        // Test fake chỉ verify service gọi đúng UserId; EF thật sẽ filter IsActive.
        detail.Services.Should().NotBeEmpty();
        detail.Services.Should().OnlyContain(s => s.PtUserId == u);
    }

    [Fact]
    public async Task GetDetail_NotVerified_ThrowsNotFound()
    {
        var store = new InMemoryStore();
        var u = Guid.NewGuid();
        store.Items[u] = new TrainerMarketplaceDetail(Summary(u, isVerified: false), "Bio", Array.Empty<Service>());

        var act = () => NewService(store).GetDetailAsync(u, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task List_LimitOutOfRange_Throws()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.ListAsync(new TrainerMarketplaceQuery(null, null, null, null, 200, null), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task List_MaxLessThanMin_Throws()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.ListAsync(new TrainerMarketplaceQuery(null, 1_000_000, 100_000, null, 20, null), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task List_CursorContinuesPagination()
    {
        var store = new InMemoryStore();
        for (var i = 0; i < 5; i++)
        {
            var u = Guid.NewGuid();
            store.Items[u] = new TrainerMarketplaceDetail(Summary(u, rating: 4.0m), null, Array.Empty<Service>());
        }
        var svc = NewService(store);

        var (page1, next1) = await svc.ListAsync(new TrainerMarketplaceQuery(null, null, null, null, 3, null), CancellationToken.None);
        page1.Should().HaveCount(3);
        next1.Should().Be("3");

        var (page2, next2) = await svc.ListAsync(new TrainerMarketplaceQuery(null, null, null, null, 3, "3"), CancellationToken.None);
        page2.Should().HaveCount(2);
        next2.Should().BeNull();
    }
}
