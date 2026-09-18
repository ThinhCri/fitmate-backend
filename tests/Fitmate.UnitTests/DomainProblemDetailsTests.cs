using Fitmate.Api.Errors;
using ApiErrorCodes = Fitmate.Api.Errors.ErrorCodes;
using DomainErrorCodes = Fitmate.Domain.Common.ErrorCodes;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-040: DomainException → ProblemDetails mapping consistency.
/// Đảm bảo HttpStatus + Code + Errors map đúng format RFC 7807.
/// </summary>
public class DomainProblemDetailsTests
{
    [Theory]
    [InlineData(404, "FITMATE.GENERAL.NOT_FOUND")]
    [InlineData(409, "FITMATE.BOOKING.SLOT_TAKEN")]
    [InlineData(403, "FITMATE.IDENTITY.FORBIDDEN")]
    [InlineData(400, "FITMATE.AUTH.WEAK_PASSWORD")]
    public void DomainException_CarriesHttpStatusAndCode(int status, string code)
    {
        var ex = new DomainException(code, "test message", status);
        ex.HttpStatus.Should().Be(status);
        ex.Code.Should().Be(code);
        ex.HttpStatus.Should().Be(status);
    }

    [Fact]
    public void NotFoundException_DefaultsTo404()
    {
        var ex = new NotFoundException(DomainErrorCodes.General.NotFound, "missing");
        ex.HttpStatus.Should().Be(404);
        ex.Code.Should().Be("FITMATE.GENERAL.NOT_FOUND");
    }

    [Fact]
    public void ConflictException_DefaultsTo409()
    {
        var ex = new ConflictException(DomainErrorCodes.Booking.SlotTaken, "taken");
        ex.HttpStatus.Should().Be(409);
        ex.Code.Should().Be("FITMATE.BOOKING.SLOT_TAKEN");
    }

    [Fact]
    public void ValidationException_CarriesErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["reason"] = new[] { "min 5 chars" }
        };
        var ex = new ValidationException(DomainErrorCodes.Validation.FieldInvalid, "bad", errors);
        ex.HttpStatus.Should().Be(400);
        ex.Errors.Should().ContainKey("reason");
        ex.Errors!["reason"].Should().ContainSingle().Which.Should().Be("min 5 chars");
    }

    [Fact]
    public void ProblemDetails_Create_DomainException_PreservesCode()
    {
        var problem = FitmateProblemDetails.Create(
            status: 409,
            title: "Conflict",
            code: ApiErrorCodes.IdempotencyConflict,
            typeSlug: "idempotency/conflict",
            detail: "Key conflict.",
            traceId: "trace-xyz");
        problem.Status.Should().Be(409);
        problem.Title.Should().Be("Conflict");
        problem.Extensions["code"].Should().Be("FITMATE.IDEMPOTENCY.CONFLICT");
        problem.Extensions["traceId"].Should().Be("trace-xyz");
        problem.Type.Should().Be("https://docs.fitmate.vn/errors/idempotency/conflict");
    }
}
