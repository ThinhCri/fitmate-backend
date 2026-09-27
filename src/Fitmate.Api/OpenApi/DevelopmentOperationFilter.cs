using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Fitmate.Api.OpenApi;

/// <summary>Documents authentication and middleware headers for local interactive requests.</summary>
public sealed class DevelopmentOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new()
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    }] = Array.Empty<string>()
                }
            };
        }

        if (context.ApiDescription.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            AddHeader(operation, "Idempotency-Key",
                "Use a new UUID for a new operation; reuse it only when retrying the same payload.");
            if (context.ApiDescription.RelativePath?.Contains("admin/finance/", StringComparison.OrdinalIgnoreCase) == true)
                AddHeader(operation, "X-Finance-Totp", "Current six-digit authenticator code for finance operations requiring step-up.");
        }
    }

    private static void AddHeader(OpenApiOperation operation, string name, string description)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        if (operation.Parameters.Any(p => p.In == ParameterLocation.Header &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name, In = ParameterLocation.Header, Required = false,
            Description = description, Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
