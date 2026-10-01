using System.Reflection;
using Fitmate.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Fitmate.UnitTests.Contract;

/// <summary>
/// Phase 12 — Contract security and authorization boundary coverage tests.
/// Verifies:
/// 1. Admin controllers enforce Admin role/policy
/// 2. User personal endpoints under /me derive user identity from token/accessor
/// 3. Financial VND properties use integer types (int/long), never floating point
/// 4. No sensitive mutations allow anonymous access
/// </summary>
public class ContractSecurityCoverageTests
{
    private static readonly Assembly ApiAssembly = typeof(Fitmate.Api.Auth.AuthController).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Fitmate.Application.Auth.IAuthorizationGuard).Assembly;

    [Fact]
    public void Admin_Controllers_Must_Enforce_Admin_Role_Or_Policy()
    {
        var adminControllers = ApiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.Namespace?.Contains(".Admin", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        adminControllers.Should().NotBeEmpty("admin controllers must exist in Fitmate.Api.Admin");

        var unshielded = new List<string>();

        foreach (var controller in adminControllers)
        {
            var authAttributes = controller.GetCustomAttributes(true).OfType<AuthorizeAttribute>().ToList();
            var hasAdminRoleOrPolicy = authAttributes.Any(a =>
                (!string.IsNullOrEmpty(a.Roles) && a.Roles.Contains("Admin", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(a.Policy) && a.Policy.Contains("Admin", StringComparison.OrdinalIgnoreCase)));

            if (!hasAdminRoleOrPolicy)
            {
                // Check if all actions have admin authorize
                var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes(true).OfType<HttpMethodAttribute>().Any());

                var allActionsProtected = actions.All(m =>
                    m.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
                        .Any(a => (!string.IsNullOrEmpty(a.Roles) && a.Roles.Contains("Admin", StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(a.Policy) && a.Policy.Contains("Admin", StringComparison.OrdinalIgnoreCase))));

                if (!allActionsProtected)
                    unshielded.Add(controller.Name);
            }
        }

        unshielded.Should().BeEmpty(
            "all admin controllers must strictly enforce Admin role or policy.\n" +
            "Unshielded controllers:\n  " + string.Join("\n  ", unshielded));
    }

    [Fact]
    public void Sensitive_Mutations_Must_Not_Allow_Anonymous()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        var allowedAnonymousMutations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "POST /api/v1/auth/login",
            "POST /api/v1/auth/register",
            "POST /api/v1/auth/refresh",
            "POST /api/v1/auth/revoke",
            "POST /api/v1/auth/password/forgot",
            "POST /api/v1/auth/password/reset",
            "POST /api/v1/public/account-deletion-request",
            "POST /api/v1/public/account-deletion-confirm",
            "POST /api/v1/admin/auth/login/verify",
            "POST /api/v1/payments/payos/webhook",
            "POST /api/v1/payments/vnpay/ipn"
        };

        var exposedMutations = new List<string>();

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttributes<RouteAttribute>(true).FirstOrDefault()?.Template ?? "";
            var controllerHasAllowAnonymous = controller.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any();
            var controllerHasAuthorize = controller.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();

            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var httpAttrs = method.GetCustomAttributes(true).OfType<HttpMethodAttribute>().ToList();
                if (httpAttrs.Count == 0) continue;

                var isMutation = httpAttrs.Any(a => a.HttpMethods.Any(m =>
                    m.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("DELETE", StringComparison.OrdinalIgnoreCase)));

                if (!isMutation) continue;

                var methodHasAllowAnonymous = method.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any();
                var methodHasAuthorize = method.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();

                bool isAnonymous;
                if (methodHasAllowAnonymous) isAnonymous = true;
                else if (controllerHasAllowAnonymous && !methodHasAuthorize) isAnonymous = true;
                else isAnonymous = !methodHasAuthorize && !controllerHasAuthorize;

                if (isAnonymous)
                {
                    foreach (var attr in httpAttrs)
                    {
                        foreach (var httpMethod in attr.HttpMethods)
                        {
                            var template = attr.Template ?? "";
                            var fullPath = CombineRoutes(controllerRoute, template);
                            if (!fullPath.StartsWith('/')) fullPath = "/" + fullPath;
                            var key = $"{httpMethod.ToUpperInvariant()} {fullPath}";

                            if (!allowedAnonymousMutations.Contains(key))
                                exposedMutations.Add($"{key} on {controller.Name}.{method.Name}");
                        }
                    }
                }
            }
        }

        exposedMutations.Should().BeEmpty(
            "no unlisted mutation endpoint may allow anonymous access.\n" +
            "Exposed mutations:\n  " + string.Join("\n  ", exposedMutations));
    }

    [Fact]
    public void Financial_Vnd_Properties_Must_Use_Integer_Types()
    {
        var allApplicationTypes = ApplicationAssembly.GetTypes()
            .Where(t => !t.Name.Contains("<>") && !t.IsSpecialName && t.IsVisible);
        var violations = new List<string>();

        foreach (var type in allApplicationTypes)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (prop.Name.EndsWith("Vnd", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("AmountVnd", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("GrossVnd", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("NetVnd", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("FeeVnd", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("PlatformFeeVnd", StringComparison.OrdinalIgnoreCase))
                {
                    var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (propType != typeof(int) && propType != typeof(long))
                    {
                        violations.Add($"{type.Name}.{prop.Name} is {prop.PropertyType.Name} (must be int or long)");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "VND financial amounts must be integer types (int or long) to prevent floating-point rounding errors.\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Me_Endpoints_Must_Not_Accept_Client_Supplied_UserId_In_Route()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        var idorRisks = new List<string>();

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttributes<RouteAttribute>(true).FirstOrDefault()?.Template ?? "";
            if (!controllerRoute.Contains("me", StringComparison.OrdinalIgnoreCase))
                continue;

            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var httpAttrs = method.GetCustomAttributes(true).OfType<HttpMethodAttribute>().ToList();
                foreach (var attr in httpAttrs)
                {
                    var template = attr.Template ?? "";
                    if (template.Contains("{userId", StringComparison.OrdinalIgnoreCase))
                    {
                        idorRisks.Add($"{controller.Name}.{method.Name} has route template '{template}' under /me");
                    }
                }
            }
        }

        idorRisks.Should().BeEmpty(
            "endpoints under /me must not accept client-supplied userId route parameters (IDOR protection).\n" +
            string.Join("\n", idorRisks));
    }

    private static string CombineRoutes(string controllerRoute, string actionRoute)
    {
        if (actionRoute.StartsWith('/') || actionRoute.StartsWith("~/"))
            return actionRoute.TrimStart('~');

        if (string.IsNullOrEmpty(controllerRoute)) return actionRoute;
        if (string.IsNullOrEmpty(actionRoute)) return controllerRoute;

        return $"{controllerRoute.TrimEnd('/')}/{actionRoute.TrimStart('/')}";
    }
}
