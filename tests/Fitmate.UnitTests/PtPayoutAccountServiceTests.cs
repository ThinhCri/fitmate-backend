using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class PtPayoutAccountServiceTests
{
    [Fact]
    public async Task Save_verifies_and_masks_supported_bank_account()
    {
        var pt = Guid.NewGuid();
        var store = new PayoutStore { HasVerifiedPhone = true };
        var lookup = new PayoutLookup();
        var service = new PtPayoutAccountService(store, lookup, AlwaysAllowAuthz.Instance);

        var result = await service.SaveAsync(pt,
            new SavePtPayoutAccountRequest("970422", "MB Bank", "0123456789"), default);

        result.AccountName.Should().Be("NGUYEN VAN A");
        result.MaskedAccountNumber.Should().Be("••••••6789");
        store.Account.Should().NotBeNull();
        lookup.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Save_rejects_unverified_phone_before_external_lookup()
    {
        var lookup = new PayoutLookup();
        var service = new PtPayoutAccountService(new PayoutStore(), lookup, AlwaysAllowAuthz.Instance);

        var act = () => service.SaveAsync(Guid.NewGuid(),
            new SavePtPayoutAccountRequest("970422", "MB Bank", "0123456789"), default);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*phone*");
        lookup.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Save_rejects_client_bank_name_that_does_not_match_bin()
    {
        var lookup = new PayoutLookup();
        var service = new PtPayoutAccountService(
            new PayoutStore { HasVerifiedPhone = true }, lookup, AlwaysAllowAuthz.Instance);

        var act = () => service.SaveAsync(Guid.NewGuid(),
            new SavePtPayoutAccountRequest("970422", "Vietcombank", "0123456789"), default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*valid account number*");
        lookup.Calls.Should().Be(0);
    }

    private sealed class PayoutStore : IPtPayoutAccountStore
    {
        public bool HasVerifiedPhone { get; init; }
        public PtPayoutAccount? Account { get; private set; }
        public Task<PtPayoutAccount?> FindAsync(Guid ptUserId, CancellationToken ct) =>
            Task.FromResult(Account);
        Task<bool> IPtPayoutAccountStore.HasVerifiedPhoneAsync(Guid ptUserId, CancellationToken ct) =>
            Task.FromResult(HasVerifiedPhone);
        public Task SaveAsync(PtPayoutAccount account, CancellationToken ct)
        {
            Account = account;
            return Task.CompletedTask;
        }
    }

    private sealed class PayoutLookup : IBankAccountLookup
    {
        public int Calls { get; private set; }
        public Task<BankAccountLookupResult> LookupAsync(string bankCode, string accountNumber,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new BankAccountLookupResult(bankCode, "ignored", accountNumber,
                "Nguyen Van A", "VietQr", "lookup:test"));
        }
    }
}
