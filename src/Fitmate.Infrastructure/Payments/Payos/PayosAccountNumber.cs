namespace Fitmate.Infrastructure.Payments.Payos;

public static class PayosAccountNumber
{
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static bool Matches(string? incoming, string? configured)
    {
        var a = Normalize(incoming);
        var b = Normalize(configured);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        // PayOS issues Virtual Account numbers with an alphanumeric prefix (e.g. V3CAS)
        // prepended to the merchant bank account number during payment link creation.
        if (a.Length > b.Length && b.Length >= 6 && a.EndsWith(b, StringComparison.Ordinal))
        {
            var prefix = a[..^b.Length];
            if (!char.IsDigit(prefix[^1]) || prefix.Any(char.IsLetter))
                return true;
        }
        return false;
    }
}
