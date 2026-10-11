using System.Text.Json.Serialization;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Finance;

public sealed record SavePtPayoutAccountRequest(
    [property: JsonPropertyName("bank_bin")] string BankBin,
    [property: JsonPropertyName("bank_name")] string BankName,
    [property: JsonPropertyName("account_number")] string AccountNumber);

public sealed record PtPayoutAccountView(
    [property: JsonPropertyName("bank_code")] string BankCode,
    [property: JsonPropertyName("bank_name")] string BankName,
    [property: JsonPropertyName("account_number")] string AccountNumber,
    [property: JsonPropertyName("masked_account_number")] string MaskedAccountNumber,
    [property: JsonPropertyName("account_name")] string AccountName,
    [property: JsonPropertyName("verified_at_utc")] DateTime VerifiedAtUtc);

public sealed record BankAccountLookupResult(string BankCode, string BankName, string AccountNumber,
    string AccountName, string Provider, string Reference);

public interface IBankAccountLookup
{
    Task<BankAccountLookupResult> LookupAsync(string bankCode, string accountNumber, CancellationToken ct);
}

public interface IPtPayoutAccountStore
{
    Task<PtPayoutAccount?> FindAsync(Guid ptUserId, CancellationToken ct);
    Task<bool> HasVerifiedPhoneAsync(Guid ptUserId, CancellationToken ct);
    Task SaveAsync(PtPayoutAccount account, CancellationToken ct);
}

public sealed class PtPayoutAccountService(IPtPayoutAccountStore store, IBankAccountLookup lookup,
    IAuthorizationGuard authz)
{
    private static readonly IReadOnlyDictionary<string, string> SupportedBanks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["970422"] = "MB Bank",
            ["970436"] = "Vietcombank",
            ["970418"] = "BIDV",
            ["970416"] = "ACB"
        };

    public async Task<PtPayoutAccountView?> GetAsync(Guid ptUserId, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var account = await store.FindAsync(ptUserId, ct);
        return account is null ? null : View(account);
    }

    public async Task<PtPayoutAccountView> SaveAsync(Guid ptUserId, SavePtPayoutAccountRequest request,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(ptUserId, "PT", ct);
        if (!await store.HasVerifiedPhoneAsync(ptUserId, ct))
            throw new ConflictException("VERIFIED_PHONE_REQUIRED",
                "Verify your phone number before changing the payout account.");
        var bankCode = request.BankBin?.Trim() ?? "";
        var bankName = request.BankName?.Trim() ?? "";
        var accountNumber = request.AccountNumber?.Trim() ?? "";
        if (!SupportedBanks.TryGetValue(bankCode, out var canonicalBankName) ||
            !string.Equals(bankName, canonicalBankName, StringComparison.Ordinal) ||
            bankName.Length is < 2 or > 120 ||
            bankName.Any(char.IsControl) || accountNumber.Length is < 6 or > 32 ||
            !accountNumber.All(char.IsDigit))
            throw new DomainException("PAYOUT_ACCOUNT_INVALID", "Choose a bank and enter a valid account number.", 400);
        var verified = await lookup.LookupAsync(bankCode, accountNumber, ct);
        verified = verified with { BankName = canonicalBankName };
        var account = await store.FindAsync(ptUserId, ct);
        account = account is null
            ? PtPayoutAccount.CreateVerified(ptUserId, verified.BankCode, verified.BankName,
                verified.AccountNumber, verified.AccountName, verified.Provider, verified.Reference, DateTime.UtcNow)
            : account.ReplaceVerified(verified.BankCode, verified.BankName, verified.AccountNumber,
                verified.AccountName, verified.Provider, verified.Reference, DateTime.UtcNow);
        await store.SaveAsync(account, ct);
        return View(account);
    }

    private static PtPayoutAccountView View(PtPayoutAccount account) => new(account.BankCode,
        account.BankName, account.AccountNumber, Mask(account.AccountNumber), account.AccountName,
        account.VerifiedAtUtc);

    private static string Mask(string value) => value.Length <= 4 ? value : new string('•', value.Length - 4) + value[^4..];
}
