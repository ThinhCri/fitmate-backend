using Fitmate.Api.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-080: ProblemDetailsFactory format chuẩn RFC 7807 + extensions code/traceId/errors.
/// </summary>
public class ProblemDetailsFactoryTests
{
    [Fact]
    public void Create_WithFullArgs_SetsAllRequiredFields()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["email"] = new[] { "Email is required", "Email must be valid" }
        };

        var problem = ProblemDetailsFactory.Create(
            status: 400,
            title: "Validation failed",
            code: ErrorCodes.ValidationFailed,
            typeSlug: "validation",
            detail: "One or more fields failed validation.",
            traceId: "abc-123",
            errors: errors);

        problem.Status.Should().Be(400);
        problem.Title.Should().Be("Validation failed");
        problem.Detail.Should().Be("One or more fields failed validation.");
        problem.Type.Should().Be("https://docs.fitmate.vn/errors/validation");
        problem.Instance.Should().Be("abc-123");
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be(ErrorCodes.ValidationFailed);
        problem.Extensions.Should().ContainKey("traceId").WhoseValue.Should().Be("abc-123");
        problem.Extensions.Should().ContainKey("errors");
    }

    [Fact]
    public void Create_WithoutOptionalArgs_OmitsNullExtensions()
    {
        var problem = ProblemDetailsFactory.Create(
            status: 404,
            title: "Not found",
            code: ErrorCodes.GeneralNotFound,
            typeSlug: "not-found");

        problem.Status.Should().Be(404);
        problem.Type.Should().Be("https://docs.fitmate.vn/errors/not-found");
        problem.Detail.Should().BeNull();
        problem.Instance.Should().BeNull();
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions.Should().NotContainKey("traceId");
        problem.Extensions.Should().NotContainKey("errors");
    }

    [Fact]
    public void Create_WithEmptyErrors_DoesNotAddErrorsExtension()
    {
        var problem = ProblemDetailsFactory.Create(
            status: 400,
            title: "Bad request",
            code: ErrorCodes.GeneralBadRequest,
            typeSlug: "bad-request",
            errors: new Dictionary<string, string[]>());

        problem.Extensions.Should().NotContainKey("errors");
    }

    [Fact]
    public void ResolveTraceId_PrefersHttpContextTraceIdentifier()
    {
        var ctx = new DefaultHttpContext { TraceIdentifier = "ctx-trace-id" };
        ProblemDetailsFactory.ResolveTraceId(ctx).Should().Be("ctx-trace-id");
    }

    [Fact]
    public void ResolveTraceId_NullContext_ReturnsUnknown()
    {
        ProblemDetailsFactory.ResolveTraceId(null).Should().Be("unknown");
    }

    [Fact]
    public void ErrorCodes_UseStablePrefix()
    {
        // Không được đổi prefix/format đã phát hành — test này chỉ pass khi code bắt đầu bằng FITMATE.
        ErrorCodes.GeneralInternal.Should().StartWith("FITMATE.");
        ErrorCodes.ValidationFailed.Should().StartWith("FITMATE.");
        ErrorCodes.BookingSlotTaken.Should().StartWith("FITMATE.");
        ErrorCodes.IdempotencyConflict.Should().StartWith("FITMATE.");
    }
}
