using System.Text;
using System.Text.Json;
using Fitmate.Application.Idempotency;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public class CanonicalJsonHasherTests
{
    [Fact]
    public void ComputeHash_DifferentPropertyOrder_ProducesIdenticalHash()
    {
        var json1 = "{\"name\":\"Fitmate\",\"version\":1,\"enabled\":true}";
        var json2 = "{\"enabled\":true,\"name\":\"Fitmate\",\"version\":1}";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json1), "application/json");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json2), "application/json");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_WhitespaceAndIndentation_ProducesIdenticalHash()
    {
        var jsonCompact = "{\"user\":{\"id\":\"123\",\"role\":\"admin\"},\"tags\":[\"a\",\"b\"]}";
        var jsonFormatted = "{\n  \"user\": {\n    \"id\": \"123\",\n    \"role\": \"admin\"\n  },\n  \"tags\": [\n    \"a\",\n    \"b\"\n  ]\n}";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(jsonCompact), "application/json");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(jsonFormatted), "application/json");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_ArrayOrder_IsPreservedAndDifferentiatesHash()
    {
        var json1 = "{\"items\":[1,2,3]}";
        var json2 = "{\"items\":[3,2,1]}";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json1), "application/json");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json2), "application/json");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_RetainsNullValuesAndDoesNotCollideWithMissingProperties()
    {
        var jsonWithNull = "{\"key\":null}";
        var jsonEmpty = "{}";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(jsonWithNull), "application/json");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(jsonEmpty), "application/json");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_DoesNotOmitSensitiveFields()
    {
        var json1 = "{\"password\":\"Secret1\",\"user\":\"admin\"}";
        var json2 = "{\"password\":\"Secret2\",\"user\":\"admin\"}";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json1), "application/json");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(json2), "application/json");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_MalformedJson_ThrowsJsonException()
    {
        var malformed = "{ this is not valid json }";

        var action = () => CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(malformed), "application/json");

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void ComputeHash_NonJsonContentType_HashesRawBytesDeterministically()
    {
        var text1 = "Hello World";
        var text2 = "Hello World ";

        var hash1 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(text1), "text/plain");
        var hash2 = CanonicalJsonHasher.ComputeHash(Encoding.UTF8.GetBytes(text2), "text/plain");

        hash1.Should().NotBe(hash2);
    }
}
