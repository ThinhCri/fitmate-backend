using Fitmate.Domain.Common;

namespace Fitmate.Domain.Trainers;

public sealed class PtPayoutAccount : AuditableEntity
{
    public Guid PtUserId { get; private set; }
    public string BankCode { get; private set; } = null!;
    public string BankName { get; private set; } = null!;
    public string AccountNumber { get; private set; } = null!;
    public string AccountName { get; private set; } = null!;
    public string VerificationProvider { get; private set; } = null!;
    public string VerificationReference { get; private set; } = null!;
    public DateTime VerifiedAtUtc { get; private set; }

    private PtPayoutAccount() { }

    public static PtPayoutAccount CreateVerified(Guid ptUserId, string bankCode, string bankName,
        string accountNumber, string accountName, string provider, string reference, DateTime nowUtc)
    {
        if (ptUserId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("PAYOUT_ACCOUNT_INVALID", "A PT and UTC verification time are required.", 400);
        return new PtPayoutAccount { PtUserId = ptUserId }.ReplaceVerified(bankCode, bankName,
            accountNumber, accountName, provider, reference, nowUtc);
    }

    public PtPayoutAccount ReplaceVerified(string bankCode, string bankName, string accountNumber,
        string accountName, string provider, string reference, DateTime nowUtc)
    {
        BankCode = Required(bankCode, 20, "bank code").ToUpperInvariant();
        BankName = Required(bankName, 120, "bank name");
        AccountNumber = Required(accountNumber, 32, "account number");
        if (!AccountNumber.All(char.IsDigit))
            throw new DomainException("PAYOUT_ACCOUNT_INVALID", "Account number must contain digits only.", 400);
        AccountName = Required(accountName, 160, "account name").ToUpperInvariant();
        VerificationProvider = Required(provider, 40, "verification provider");
        VerificationReference = Required(reference, 128, "verification reference");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("PAYOUT_ACCOUNT_INVALID", "A UTC verification time is required.", 400);
        VerifiedAtUtc = nowUtc;
        Touch();
        return this;
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > max || normalized.Any(char.IsControl))
            throw new DomainException("PAYOUT_ACCOUNT_INVALID", $"A valid {field} is required.", 400);
        return normalized;
    }
}
