using Fitmate.Application.Storage;
using Fitmate.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-420: LocalStorageService writes files atomically under BasePath with
/// path-traversal protection and MIME validation caller responsibility.
/// </summary>
public class LocalStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalStorageService _svc;

    public LocalStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fitmate-test-{Guid.NewGuid():N}");
        var options = Options.Create(new StorageOptions { BasePath = _tempRoot, MaxFileSizeBytes = 5 * 1024 * 1024 });
        _svc = new LocalStorageService(options, new LoggerFactory().CreateLogger<LocalStorageService>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task UploadAsync_WritesFile_AndReturnsUri()
    {
        var data = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var uri = await _svc.UploadAsync("avatars/user1.jpg", data, "image/jpeg");

        var fullPath = Path.Combine(_tempRoot, "avatars", "user1.jpg");
        File.Exists(fullPath).Should().BeTrue();
        File.ReadAllBytes(fullPath).Should().Equal(1, 2, 3, 4, 5);
        uri.AbsolutePath.Should().EndWith("avatars/user1.jpg");
    }

    [Fact]
    public async Task UploadAsync_CreatesSubdirectories_OnDemand()
    {
        var data = new MemoryStream(new byte[] { 9 });
        await _svc.UploadAsync("posts/2026/09/img.png", data, "image/png");

        File.Exists(Path.Combine(_tempRoot, "posts", "2026", "09", "img.png")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_PreservesAtomics_NoTempFilesLeft()
    {
        var data = new MemoryStream(new byte[] { 42, 42 });
        await _svc.UploadAsync("certificates/x.jpg", data, "image/jpeg");

        var dir = Path.Combine(_tempRoot, "certificates");
        Directory.GetFiles(dir).Should().HaveCount(1, "atomic rename removes temp file");
    }

    [Fact]
    public async Task UploadAsync_RejectsPathTraversal()
    {
        var data = new MemoryStream(new byte[] { 1 });
        var act = async () => await _svc.UploadAsync("../escape.jpg", data, "image/jpeg");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*traversal*");
    }

    [Fact]
    public async Task ListOwnedKeysAsync_IncludesUnattachedUploadsOnlyForOwner()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ownKey = $"avatars/{owner:N}_portrait_1.png";
        await _svc.UploadAsync(ownKey, new MemoryStream([1]), "image/png");
        await _svc.UploadAsync($"avatars/{other:N}_portrait_1.png", new MemoryStream([2]), "image/png");
        var keys = await _svc.ListOwnedKeysAsync(owner);
        keys.Should().ContainSingle().Which.Should().Be(ownKey);
        await _svc.DeleteAsync(ownKey);
        (await _svc.ListOwnedKeysAsync(owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RejectsSiblingDirectoryWithSamePrefix()
    {
        var sibling = _tempRoot + "-other";
        var action = () => _svc.DeleteAsync("../" + Path.GetFileName(sibling) + "/private.png");
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
}
