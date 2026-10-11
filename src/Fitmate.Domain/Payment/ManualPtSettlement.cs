using Fitmate.Domain.Common;

namespace Fitmate.Domain.Payment;

public sealed class ManualPtSettlement
{
    public Guid AppointmentId { get; private set; }
    public Guid PtUserId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string Status { get; private set; } = "on_hold";
    public long AmountVnd { get; private set; }
    public Guid ReviewedBy { get; private set; }
    public DateTime? CustomerConfirmedAt { get; private set; }
    public DateTime? TrainerConfirmedAt { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public string? SessionEvidenceReference { get; private set; }
    public string? PayeeEvidenceReference { get; private set; }
    public string? HoldReason { get; private set; }
    public Guid? TransferredBy { get; private set; }
    public DateTime? TransferredAt { get; private set; }
    public string? BankReference { get; private set; }
    public string? TransferEvidenceReference { get; private set; }
    public string? PayeeBankCode { get; private set; }
    public string? PayeeBankName { get; private set; }
    public string? PayeeAccountNumber { get; private set; }
    public string? PayeeAccountName { get; private set; }
    public DateTime? PayeeVerifiedAtUtc { get; private set; }
    private ManualPtSettlement() { }
    public static ManualPtSettlement Create(Guid appointmentId, Guid ptId, Guid paymentId) => new()
    { AppointmentId = appointmentId, PtUserId = ptId, PaymentId = paymentId };

    public void Approve(Guid adminId, long amount, DateTime customerConfirmedAt, DateTime trainerConfirmedAt,
        DateTime sessionEnd, string sessionEvidence, string payeeEvidence, bool noOpenDispute, DateTime now,
        string payeeBankCode, string payeeBankName, string payeeAccountNumber, string payeeAccountName,
        DateTime payeeVerifiedAtUtc)
    {
        EnsureNotPaid();
        if (adminId == Guid.Empty || amount <= 0 || !noOpenDispute || sessionEnd > now ||
            customerConfirmedAt.Kind != DateTimeKind.Utc || trainerConfirmedAt.Kind != DateTimeKind.Utc ||
            customerConfirmedAt < sessionEnd || trainerConfirmedAt < sessionEnd ||
            customerConfirmedAt > now || trainerConfirmedAt > now)
            throw new DomainException("SETTLEMENT_NOT_VERIFIED", "Confirm both participants after the session, resolve disputes and verify the payee before approval.", 400);
        ValidateReference(sessionEvidence);
        ValidateReference(payeeEvidence);
        if (string.IsNullOrWhiteSpace(payeeBankCode) || string.IsNullOrWhiteSpace(payeeBankName) ||
            string.IsNullOrWhiteSpace(payeeAccountNumber) || string.IsNullOrWhiteSpace(payeeAccountName) ||
            payeeVerifiedAtUtc.Kind != DateTimeKind.Utc || payeeVerifiedAtUtc > now)
            throw new ConflictException("PAYOUT_ACCOUNT_REQUIRED", "The PT needs a verified payout account before approval.");
        AmountVnd = amount;
        CustomerConfirmedAt = customerConfirmedAt;
        TrainerConfirmedAt = trainerConfirmedAt;
        SessionEvidenceReference = sessionEvidence.Trim();
        PayeeEvidenceReference = payeeEvidence.Trim();
        PayeeBankCode = payeeBankCode.Trim();
        PayeeBankName = payeeBankName.Trim();
        PayeeAccountNumber = payeeAccountNumber.Trim();
        PayeeAccountName = payeeAccountName.Trim();
        PayeeVerifiedAtUtc = payeeVerifiedAtUtc;
        ReviewedBy = adminId;
        ApprovedAt = now;
        HoldReason = null;
        Status = "approved";
    }

    public void Hold(string reason)
    {
        EnsureNotPaid();
        ValidateReference(reason);
        Status = "on_hold";
        HoldReason = reason.Trim();
    }

    public void RecordTransfer(Guid adminId, long eligibleAmount, string bankReference, string evidence, DateTime transferredAt, DateTime now)
    {
        // PostgreSQL timestamps retain microseconds; normalize before duplicate comparison.
        transferredAt = new DateTime(transferredAt.Ticks - transferredAt.Ticks % 10, transferredAt.Kind);
        ValidateReference(bankReference);
        if (bankReference.Length > 128) throw new DomainException("TRANSFER_REFERENCE_INVALID", "Bank reference cannot exceed 128 characters.", 400);
        ValidateReference(evidence);
        if (Status == "paid")
        {
            if (BankReference == bankReference.Trim() && TransferEvidenceReference == evidence.Trim() && TransferredAt == transferredAt) return;
            throw new ConflictException("SETTLEMENT_ALREADY_PAID", "This appointment already has a recorded transfer.");
        }
        if (Status != "approved" || AmountVnd != eligibleAmount || adminId == Guid.Empty ||
            ApprovedAt is not DateTime approvedAt || transferredAt.Kind != DateTimeKind.Utc ||
            transferredAt.Ticks / 10 < approvedAt.Ticks / 10 || transferredAt > now)
            throw new ConflictException("SETTLEMENT_NOT_APPROVED", "Review the current amount and approval before recording a transfer.");
        BankReference = bankReference.Trim();
        TransferEvidenceReference = evidence.Trim();
        TransferredAt = transferredAt;
        TransferredBy = adminId;
        Status = "paid";
    }

    private void EnsureNotPaid()
    {
        if (Status == "paid") throw new ConflictException("SETTLEMENT_ALREADY_PAID", "Paid settlement history is immutable.");
    }
    private static void ValidateReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw new DomainException("SETTLEMENT_EVIDENCE_REQUIRED", "Provide a private evidence/case reference of at most 200 characters.", 400);
    }
}
