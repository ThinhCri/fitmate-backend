namespace Fitmate.Application.Common;

/// <summary>
/// BE-825: Định nghĩa các hằng số và câu truy vấn kiểm chứng bất biến sau khôi phục thảm họa (Post-Restore Invariants).
/// </summary>
public static class PostRestoreInvariantChecker
{
    public const string DefaultCanaryPurpose = "fitmate.dr-canary-verification.v1";
    public const string DefaultCanaryExpectedPlaintext = "fitmate-dr-canary-token-alive";

    /// <summary>
    /// Định nghĩa các câu lệnh SQL kiểm chứng 5 bất biến tài chính sau khôi phục
    /// (Sử dụng đúng tên bảng và tên cột có trích dẫn quoted PascalCase theo schema thực tế).
    /// </summary>
    public static class SqlInvariantQueries
    {
        /// <summary>
        /// Bất biến 1: Không có AmountVnd hoặc RefundedSoFarVnd âm.
        /// Kết quả phải bằng 0.
        /// </summary>
        public const string NonNegativeAmounts = @"
            SELECT COUNT(*) 
            FROM payments 
            WHERE ""AmountVnd"" < 0 OR ""RefundedSoFarVnd"" < 0;";

        /// <summary>
        /// Bất biến 2: Số tiền đã hoàn (RefundedSoFarVnd) không được vượt quá số tiền thanh toán (AmountVnd).
        /// Kết quả phải bằng 0.
        /// </summary>
        public const string RefundCapIntegrity = @"
            SELECT COUNT(*) 
            FROM payments 
            WHERE ""RefundedSoFarVnd"" > ""AmountVnd"";";

        /// <summary>
        /// Bất biến 3: Tổng số tiền các lượt hoàn tiền thành công (Status = 1) liên kết với một payment
        /// không được vượt quá AmountVnd của payment đó.
        /// Kết quả phải bằng 0.
        /// </summary>
        public const string CumulativeRefundIntegrity = @"
            SELECT COUNT(*) FROM (
                SELECT p.id, p.""AmountVnd"", COALESCE(SUM(r.""AmountVnd""), 0) AS total_refunded
                FROM payments p
                LEFT JOIN refunds r ON r.""PaymentId"" = p.id AND r.""Status"" = 1
                GROUP BY p.id, p.""AmountVnd""
                HAVING COALESCE(SUM(r.""AmountVnd""), 0) > p.""AmountVnd""
            ) violations;";

        /// <summary>
        /// Bất biến 4: Mã tham chiếu giao dịch cổng thanh toán (ProviderTxnRef) không được trùng lặp giữa các payment khác nhau.
        /// Kết quả phải bằng 0.
        /// </summary>
        public const string UniqueProviderTxnRef = @"
            SELECT COUNT(*) FROM (
                SELECT ""ProviderTxnRef"", COUNT(*) 
                FROM payments 
                WHERE ""ProviderTxnRef"" IS NOT NULL AND ""ProviderTxnRef"" <> ''
                GROUP BY ""ProviderTxnRef"" 
                HAVING COUNT(*) > 1
            ) dups;";

        /// <summary>
        /// Bất biến 5: Mọi Appointment có PaymentId không null phải trỏ tới một bản ghi Payment tồn tại thực tế.
        /// Kết quả phải bằng 0.
        /// </summary>
        public const string AppointmentPaymentReferentialIntegrity = @"
            SELECT COUNT(*) 
            FROM appointments a 
            WHERE a.""PaymentId"" IS NOT NULL 
              AND NOT EXISTS (SELECT 1 FROM payments p WHERE p.id = a.""PaymentId"");";

        /// <summary>
        /// Bất biến Tombstone: Thống kê số lượng người dùng đã bị xóa (DeletedAt khác NULL).
        /// </summary>
        public const string DeletedUsersCount = @"
            SELECT COUNT(*) 
            FROM asp_net_users 
            WHERE ""DeletedAt"" IS NOT NULL;";

        /// <summary>
        /// Thống kê Outbox state và Lost-Work Boundary timestamp.
        /// </summary>
        public const string LatestOutboxEventTimestamp = @"
            SELECT MAX(""CreatedAt"") 
            FROM outbox_messages;";
    }
}
