using Fitmate.Api.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// CERT-02 (phần storage key): storage key do <c>UploadsController</c> phát ra có dạng
/// <c>{category}/{userIdN}_{safeName}_{random}{ext}</c>. <c>CertificatesController.Submit</c>
/// kiểm tra lại scope này trước khi ghi <c>ImageUploadedByUserId</c>.
///
/// Lý do cần cả hai lớp: nếu chỉ ghi uploader = current user mà không kiểm key, một PT có
/// thể lấy storage key của tài khoản khác (từ signed URL lúc upload) rồi submit — khi đó
/// uploader và owner cùng là PT đó và ownership check ở preview trở nên vô nghĩa.
/// </summary>
public class CertificateImageKeyOwnershipTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Other = Guid.Parse("99999999-8888-7777-6666-555555555555");

    [Theory]
    [InlineData("certificates/11111111222233334444555555555555_nasm_abcdef0123456789.png")]
    [InlineData("post-images/11111111222233334444555555555555_avatar_0123456789abcdef.jpg")]
    public void KeyScopedToUploader_IsAccepted(string key)
    {
        CertificatesController.ImageKeyBelongsTo(key, Owner).Should().BeTrue();
    }

    [Fact]
    public void KeyWithoutSeparatorAfterUserId_IsRejected()
    {
        // "{userId}.png" không phải format UploadsController phát ra (thiếu dấu '_').
        CertificatesController.ImageKeyBelongsTo("certificates/11111111222233334444555555555555.png", Owner)
            .Should().BeFalse();
    }

    [Fact]
    public void KeyScopedToAnotherUser_IsRejected()
    {
        // Signed URL của tài khoản khác bị dùng lại: phải chặn.
        var otherKey = $"certificates/{Other:N}_nasm_abcdef0123456789.png";
        CertificatesController.ImageKeyBelongsTo(otherKey, Owner).Should().BeFalse();
    }

    [Fact]
    public void LegacyKeyWithoutUserScope_IsRejected()
    {
        // Key phát ra trước khi thêm user scope: không chứng minh được chủ → chặn.
        CertificatesController.ImageKeyBelongsTo("certificates/nasm_abcdef0123456789.png", Owner)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("certificates")]
    [InlineData("certificates/")]
    [InlineData("/nasm.png")]
    [InlineData("11111111222233334444555555555555_file.png")]   // thiếu category
    [InlineData("certificates/nasm.png/extra")]                 // quá nhiều phần
    [InlineData("certificates/x11111111222233334444555555555555_file.png")] // scope chỉ là tiền tố
    public void MalformedKey_IsRejected(string key)
    {
        CertificatesController.ImageKeyBelongsTo(key, Owner).Should().BeFalse();
    }

    [Fact]
    public void KeyIsTrimmedBeforeValidation()
    {
        var key = $"  certificates/{Owner:N}_nasm_abc.png  ";
        CertificatesController.ImageKeyBelongsTo(key, Owner).Should().BeTrue();
    }

    [Fact]
    public void UserScopeComparisonIsCaseInsensitive()
    {
        // Guid.ToString("N") sinh chữ thường; client có thể gửi hoa/thường tuỳ ý.
        var key = $"certificates/{Owner.ToString("N").ToUpperInvariant()}_nasm_abc.png";
        CertificatesController.ImageKeyBelongsTo(key, Owner).Should().BeTrue();
    }

    [Fact]
    public void PathTraversalLikeKey_IsRejected()
    {
        // "certificates/../{other}/file.png" — phần sau '/' đầu tiên là "..", không phải scope.
        var traversal = $"certificates/../{Other:N}_file.png";
        CertificatesController.ImageKeyBelongsTo(traversal, Owner).Should().BeFalse();
    }
}
