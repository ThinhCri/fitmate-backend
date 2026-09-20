using System.Text;
using Microsoft.Extensions.Configuration;

namespace Fitmate.Application.Common;

/// <summary>
/// BE-380: load + validate <see cref="JwtOptions"/> tại một nơi duy nhất.
/// </summary>
/// <remarks>
/// <para>
/// Hỗ trợ alias để tương thích ngược với tên env cũ:
/// <list type="bullet">
///   <item><c>JWT_SIGNING_KEY</c> (env) — tên canonical, đã dùng trong <c>.env.example</c> và <c>compose.yml</c>.</item>
///   <item><c>Jwt__SigningKey</c> (env) — alias theo convention ASP.NET Core configuration mapping
///         (khớp <c>Jwt:SigningKey</c> section key).</item>
/// </list>
/// </para>
/// <para>
/// Quy tắc:
/// <list type="bullet">
///   <item>Một trong hai được set → dùng giá trị đó.</item>
///   <item>Cả hai set và giống nhau → chấp nhận.</item>
///   <item>Cả hai set và khác nhau → throw với thông báo rõ ràng (fail-fast).</item>
///   <item>Không set cái nào hoặc key không đáp ứng yêu cầu thuật toán → throw (fail-fast, không fallback).</item>
///   <item>Không log giá trị key.</item>
/// </list>
/// </para>
/// </remarks>
public static class JwtOptionsLoader
{
    /// <summary>
    /// Env var canonical (uppercase + underscore, dotnet convention ngoài <c>IConfiguration</c>).
    /// </summary>
    public const string CanonicalEnvKey = "JWT_SIGNING_KEY";

    /// <summary>
    /// Env var alias theo ASP.NET Core (section__key).
    /// </summary>
    public const string AliasEnvKey = "Jwt__SigningKey";

    /// <summary>
    /// Đọc và validate <see cref="JwtOptions"/> từ <paramref name="configuration"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Throw fail-fast khi thiếu key, key không đủ mạnh, hoặc hai env alias có giá trị khác nhau.
    /// </exception>
    public static JwtOptions Load(IConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var jwtSection = configuration.GetSection("Jwt");
        var fromSection = jwtSection["SigningKey"];
        var fromCanonicalEnv = configuration[CanonicalEnvKey];
        var fromAliasEnv = configuration[AliasEnvKey];

        // Gộp tất cả giá trị không rỗng; fail-fast nếu >1 giá trị khác nhau.
        var candidates = new List<string>(capacity: 3);
        if (!string.IsNullOrWhiteSpace(fromSection)) candidates.Add(fromSection);
        if (!string.IsNullOrWhiteSpace(fromCanonicalEnv)) candidates.Add(fromCanonicalEnv);
        if (!string.IsNullOrWhiteSpace(fromAliasEnv)) candidates.Add(fromAliasEnv);

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"JwtOptions.SigningKey must be configured. " +
                $"Set env {CanonicalEnvKey} (preferred) or {AliasEnvKey} (ASP.NET Core alias) or appsettings 'Jwt:SigningKey'.");
        }

        var distinct = candidates.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1)
        {
            // Fail-fast — KHÔNG chọn ngầm một giá trị nào.
            throw new InvalidOperationException(
                $"JwtOptions.SigningKey has conflicting values across " +
                $"env {CanonicalEnvKey}, env {AliasEnvKey}, or Jwt:SigningKey section. " +
                $"Resolve to a single value before starting the API.");
        }

        var signingKey = distinct[0];

        // Validate độ dài theo thuật toán hiện dùng (HS256): >= 32 bytes.
        // ASP.NET Core yêu cầu >= 16 bytes cho HmacSha256, nhưng ta muốn an toàn cho HS512 (>= 64 bytes).
        var byteLen = Encoding.UTF8.GetByteCount(signingKey);
        if (byteLen < 32)
        {
            throw new InvalidOperationException(
                $"JwtOptions.SigningKey must be at least 32 bytes for HS256 " +
                $"(got {byteLen} bytes). Generate with `openssl rand -base64 64`.");
        }

        var options = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
        options.SigningKey = signingKey;

        // Defaults cho Issuer/Audience/TTLs vẫn tới từ appsettings hoặc hardcoded trong JwtOptions.
        return options;
    }
}
