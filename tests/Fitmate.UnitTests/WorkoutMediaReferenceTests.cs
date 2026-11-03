using Fitmate.Application.Storage;
using Fitmate.Application.Workout;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class WorkoutMediaReferenceTests
{
    [Theory]
    [InlineData("https://account.r2.cloudflarestorage.com/bucket/exercise-catalog/Pushups/0.jpg?X-Amz-Expires=1&X-Amz-Signature=expired")]
    [InlineData("http://localhost:5116/uploads/exercise-catalog/Pushups/0.jpg?expires=1&sig=expired")]
    [InlineData("storage://exercise-catalog/Pushups/0.jpg")]
    public void Shared_catalog_url_is_stored_as_identity_and_signed_fresh_on_every_read(string input)
    {
        WorkoutMediaReference.NormalizeForStorage(input).Should().Be("storage://exercise-catalog/Pushups/0.jpg");
        var signer = new Mock<ISignedUrlService>(); var revision = 0;
        signer.Setup(x => x.CreatePresignedReadUrl("exercise-catalog/Pushups/0.jpg", It.IsAny<TimeSpan>()))
            .Returns(() => new Uri($"https://storage.example/exercise-catalog/Pushups/0.jpg?fresh={++revision}"));
        WorkoutMediaReference.ForRead(input, signer.Object).Should().EndWith("fresh=1");
        WorkoutMediaReference.ForRead(input, signer.Object).Should().EndWith("fresh=2");
    }

    [Theory]
    [InlineData("https://account.r2.cloudflarestorage.com/bucket/certificates/private.jpg?X-Amz-Signature=expired")]
    [InlineData("storage://posts/private.jpg")]
    [InlineData("storage://exercise-catalog/../private.jpg")]
    [InlineData("https://example.test/exercise-catalog/Pushups/0.jpg/extra")]
    [InlineData("https://example.test/exercise-catalog/Pushups/%2e%2e/private.jpg")]
    public void Never_turns_a_private_or_malformed_reference_into_a_new_signed_url(string input)
    {
        var signer = new Mock<ISignedUrlService>(MockBehavior.Strict);
        WorkoutMediaReference.NormalizeForStorage(input).Should().Be(input);
        WorkoutMediaReference.ForRead(input, signer.Object).Should().Be(input);
        signer.VerifyNoOtherCalls();
    }
}
