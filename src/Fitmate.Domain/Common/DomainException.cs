namespace Fitmate.Domain.Common;

/// <summary>
/// Domain exception. Application layer ném, API layer map sang ProblemDetails.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }
    public int HttpStatus { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public DomainException(string code, string message, int httpStatus = 400, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Code = code;
        HttpStatus = httpStatus;
        Errors = errors;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string code, string message)
        : base(code, message, 404) { }
}

public class ConflictException : DomainException
{
    public ConflictException(string code, string message)
        : base(code, message, 409) { }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string code, string message)
        : base(code, message, 403) { }
}

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string code, string message)
        : base(code, message, 401) { }
}

public class ValidationException : DomainException
{
    public ValidationException(string code, string message, IReadOnlyDictionary<string, string[]> errors)
        : base(code, message, 400, errors) { }
}
