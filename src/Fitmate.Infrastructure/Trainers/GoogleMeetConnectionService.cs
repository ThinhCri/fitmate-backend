using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Trainers;

/// <summary>PT-owned Google OAuth connection; refresh tokens never leave the backend.</summary>
public sealed class GoogleMeetConnectionService(
    FitmateDbContext db,
    IAuthorizationGuard authorization,
    HttpClient http,
    IDataProtectionProvider dataProtection,
    IOptions<GoogleMeetOptions> options) : IGoogleMeetConnectionService
{
    public const string Provider = "fitmate-google-meet-v1";
    private const string StateName = "oauth-state";
    public const string ConnectionName = "refresh-token";
    private const string MeetingPrefix = "appointment-meeting-";
    private const string Scope = "https://www.googleapis.com/auth/meetings.space.created";
    private readonly IDataProtector _stateProtector = dataProtection.CreateProtector("google-meet-oauth-state-v1");
    private readonly IDataProtector _tokenProtector = dataProtection.CreateProtector("google-meet-refresh-token-v1");
    private sealed record OAuthState(Guid PtUserId, string Nonce, DateTime ExpiresAtUtc);

    public async Task<GoogleMeetConnectionStatus> GetStatusAsync(Guid ptUserId, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(ptUserId, "PT", ct);
        return new GoogleMeetConnectionStatus(options.Value.IsConfigured() &&
            await db.UserTokens.AnyAsync(t => t.UserId == ptUserId && t.LoginProvider == Provider &&
                t.Name == ConnectionName, ct));
    }

    public async Task<string> CreateAuthorizationUrlAsync(Guid ptUserId, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(ptUserId, "PT", ct);
        var config = RequireConfig();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var state = new OAuthState(ptUserId, nonce, DateTime.UtcNow.AddMinutes(10));
        var row = await FindTokenAsync(ptUserId, StateName, ct);
        if (row is null)
        {
            row = new IdentityUserToken<Guid> { UserId = ptUserId, LoginProvider = Provider, Name = StateName };
            db.UserTokens.Add(row);
        }
        row.Value = Hash(nonce);
        await db.SaveChangesAsync(ct);
        var protectedState = _stateProtector.Protect(JsonSerializer.Serialize(state));
        return "https://accounts.google.com/o/oauth2/v2/auth?response_type=code" +
            "&client_id=" + Uri.EscapeDataString(config.ClientId) +
            "&redirect_uri=" + Uri.EscapeDataString(config.RedirectUri) +
            "&scope=" + Uri.EscapeDataString(Scope) +
            "&access_type=offline&prompt=consent&state=" + Uri.EscapeDataString(protectedState);
    }

    public async Task CompleteAuthorizationAsync(string code, string state, CancellationToken ct)
    {
        var config = RequireConfig();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            code.Length > 4096 || state.Length > 4096)
            throw InvalidState();
        OAuthState parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state)) ?? throw InvalidState();
        }
        catch (CryptographicException)
        {
            throw InvalidState();
        }
        catch (JsonException)
        {
            throw InvalidState();
        }
        if (parsed.PtUserId == Guid.Empty || parsed.ExpiresAtUtc <= DateTime.UtcNow)
            throw InvalidState();

        // Consume one-time state before contacting Google. A failed exchange requires a fresh start.
        var expectedHash = Hash(parsed.Nonce);
        var consumed = await db.UserTokens.Where(t => t.UserId == parsed.PtUserId &&
            t.LoginProvider == Provider && t.Name == StateName && t.Value == expectedHash)
            .ExecuteDeleteAsync(ct);
        if (consumed != 1) throw InvalidState();

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri,
                ["grant_type"] = "authorization_code"
            })
        };
        using var response = await http.SendAsync(tokenRequest, ct);
        if (!response.IsSuccessStatusCode)
            throw new DomainException("GOOGLE_MEET_OAUTH_FAILED", "Không thể kết nối Google Meet. Vui lòng thử lại.", 503);
        var refresh = await ReadTokenFieldAsync(response, "refresh_token", ct);
        if (string.IsNullOrWhiteSpace(refresh))
            throw new DomainException("GOOGLE_MEET_REFRESH_TOKEN_MISSING", "Google chưa cấp quyền dài hạn. Vui lòng kết nối lại.", 503);

        var row = await FindTokenAsync(parsed.PtUserId, ConnectionName, ct);
        if (row is null)
        {
            row = new IdentityUserToken<Guid> { UserId = parsed.PtUserId, LoginProvider = Provider, Name = ConnectionName };
            db.UserTokens.Add(row);
        }
        row.Value = _tokenProtector.Protect(refresh);
        await db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(Guid ptUserId, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(ptUserId, "PT", ct);
        await db.UserTokens.Where(t => t.UserId == ptUserId && t.LoginProvider == Provider &&
            (t.Name == ConnectionName || t.Name == StateName)).ExecuteDeleteAsync(ct);
    }

    public async Task<GoogleMeetSpace> CreateSpaceAsync(Guid ptUserId, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(ptUserId, "PT", ct);
        var config = RequireConfig();
        var row = await FindTokenAsync(ptUserId, ConnectionName, ct)
            ?? throw new DomainException("GOOGLE_MEET_NOT_CONNECTED", "PT chưa kết nối Google Meet.", 503);
        string refresh;
        try { refresh = _tokenProtector.Unprotect(row.Value!); }
        catch (CryptographicException)
        {
            throw new DomainException("GOOGLE_MEET_CONNECTION_INVALID", "PT cần kết nối lại Google Meet.", 503);
        }

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["refresh_token"] = refresh,
                ["grant_type"] = "refresh_token"
            })
        };
        using var refreshResponse = await http.SendAsync(refreshRequest, ct);
        if (!refreshResponse.IsSuccessStatusCode)
            throw new DomainException("GOOGLE_MEET_CONNECTION_EXPIRED", "PT cần kết nối lại Google Meet.", 503);
        var accessToken = await ReadTokenFieldAsync(refreshResponse, "access_token", ct);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new DomainException("GOOGLE_MEET_TOKEN_INVALID", "Không thể tạo liên kết buổi tập online.", 503);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "https://meet.googleapis.com/v2/spaces")
        {
            Content = JsonContent.Create(new { })
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var createResponse = await http.SendAsync(createRequest, ct);
        if (!createResponse.IsSuccessStatusCode)
            throw new DomainException("GOOGLE_MEET_CREATE_FAILED", "Không thể tạo liên kết buổi tập online.", 503);
        await using var stream = await createResponse.Content.ReadAsStreamAsync(ct);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var name = result.RootElement.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
        var meetingUri = result.RootElement.TryGetProperty("meetingUri", out var uriProperty) ? uriProperty.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || !Uri.TryCreate(meetingUri, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "meet.google.com")
            throw new DomainException("GOOGLE_MEET_RESPONSE_INVALID", "Google Meet trả về liên kết không hợp lệ.", 503);
        return new GoogleMeetSpace(name, meetingUri!);
    }

    public async Task<GoogleMeetSpace> CreateForAppointmentAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        var tokenName = MeetingPrefix + appointmentId.ToString("N");
        var existing = await FindTokenAsync(ptUserId, tokenName, ct);
        if (existing is not null)
            return JsonSerializer.Deserialize<GoogleMeetSpace>(existing.Value!) ??
                throw new DomainException("GOOGLE_MEET_RECORD_INVALID", "Liên kết buổi tập không hợp lệ.", 503);
        var space = await CreateSpaceAsync(ptUserId, ct);
        db.UserTokens.Add(new IdentityUserToken<Guid>
        {
            UserId = ptUserId, LoginProvider = Provider, Name = tokenName,
            Value = JsonSerializer.Serialize(space)
        });
        await db.SaveChangesAsync(ct);
        return space;
    }

    public async Task RemoveForAppointmentAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        var tokenName = MeetingPrefix + appointmentId.ToString("N");
        var tracked = db.UserTokens.Local.Where(t => t.UserId == ptUserId &&
            t.LoginProvider == Provider && t.Name == tokenName).ToArray();
        foreach (var token in tracked) db.Entry(token).State = EntityState.Detached;
        await db.UserTokens.Where(t => t.UserId == ptUserId && t.LoginProvider == Provider &&
            t.Name == tokenName).ExecuteDeleteAsync(ct);
    }

    public async Task<string?> GetMeetingUriAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        var token = await FindTokenAsync(ptUserId, MeetingPrefix + appointmentId.ToString("N"), ct);
        return token?.Value is null ? null : JsonSerializer.Deserialize<GoogleMeetSpace>(token.Value)?.MeetingUri;
    }

    public async Task<string?> GetMeetingUriForParticipantAsync(Guid actorUserId, Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null || (appointment.MemberId != actorUserId && appointment.PtUserId != actorUserId))
            throw new NotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
        if (appointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.InProgress or
            AppointmentStatus.AwaitingMemberConfirmation or AppointmentStatus.Completed) ||
            !appointment.PaymentId.HasValue ||
            !await db.Payments.AsNoTracking().AnyAsync(p => p.Id == appointment.PaymentId &&
                (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartialRefunded), ct))
            return null;
        return await GetMeetingUriAsync(appointment.PtUserId, appointmentId, ct);
    }

    private async Task<IdentityUserToken<Guid>?> FindTokenAsync(Guid userId, string name, CancellationToken ct) =>
        await db.UserTokens.SingleOrDefaultAsync(t => t.UserId == userId && t.LoginProvider == Provider && t.Name == name, ct);

    private GoogleMeetOptions RequireConfig() => options.Value.IsConfigured() ? options.Value :
        throw new DomainException("GOOGLE_MEET_NOT_CONFIGURED", "Google Meet chưa được cấu hình.", 503);

    private static async Task<string?> ReadTokenFieldAsync(HttpResponseMessage response, string field, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    }

    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    private static DomainException InvalidState() =>
        new("GOOGLE_MEET_OAUTH_STATE_INVALID", "Liên kết Google đã hết hạn hoặc không hợp lệ.", 400);
}
