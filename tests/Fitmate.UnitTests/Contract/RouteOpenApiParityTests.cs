using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Fitmate.UnitTests.Contract;

/// <summary>
/// Phase 1 — Route and OpenAPI parity tests.
/// Uses reflection on controller types to extract runtime routes and compares them
/// against the OpenAPI spec. No database, no WebApplicationFactory needed.
///
/// Detects:
/// - Runtime routes missing from OpenAPI
/// - OpenAPI operations without runtime endpoint
/// - HTTP method mismatches
/// - Duplicate operationId
/// - Duplicate method+path
/// - Path parameter without declaration
/// - Declared parameter not in path
/// - Security requirement mismatches
/// </summary>
public class RouteOpenApiParityTests
{
    private const string OpenApiRelativePath = "docs/backend-release/openapi/fitmate-v1.yaml";

    /// <summary>
    /// OpenAPI server base path prepended to all spec paths.
    /// Runtime routes include this; spec paths do not.
    /// </summary>
    private const string ApiPrefix = "/api/v1";

    /// <summary>
    /// Routes that exist at runtime but are infrastructure/development-only,
    /// intentionally excluded from OpenAPI mobile contract.
    /// </summary>
    private static readonly HashSet<string> InfrastructureRouteAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Health checks — mapped in Program.cs, not via controllers
        "GET /health/live",
        "GET /health/ready",
        "GET /api/v1/health/ready",
        // Admin Prometheus metrics endpoint — internal scrape
        "GET /api/v1/admin/metrics/prometheus",
        // Admin outbox dead-letter & stats — ops-only
        "GET /api/v1/admin/outbox/dead-letter",
        "GET /api/v1/admin/outbox/stats",
        "POST /api/v1/admin/outbox/dead-letter/{id}/replay",
    };

    /// <summary>
    /// Operations declared in OpenAPI but NOT implemented at runtime because
    /// the capability is not yet available. Each entry documents:
    /// operation, reason, task ID, removal condition, expected capability state.
    /// </summary>
    private static readonly CapabilityAllowlistEntry[] CapabilityNotImplementedAllowlist =
    {
        new("GET /api/v1/chat/ai/threads", "AI companion not implemented", "BE-817",
            "Remove when AI adapter is integrated and verified", "disabled"),
        new("POST /api/v1/chat/ai/threads", "AI companion not implemented", "BE-817",
            "Remove when AI adapter is integrated and verified", "disabled"),
        new("GET /api/v1/chat/ai/threads/{id}/messages", "AI companion not implemented", "BE-817",
            "Remove when AI adapter is integrated and verified", "disabled"),
        new("POST /api/v1/chat/ai/messages", "AI companion not implemented", "BE-817",
            "Remove when AI adapter is integrated and verified", "disabled"),
        new("POST /api/v1/payments/vnpay/ipn", "VNPay legacy — payOS is current provider", "BE-400",
            "Remove when VNPay adapter is fully retired or re-integrated", "disabled"),
        new("GET /api/v1/payments/vnpay/return", "VNPay legacy — payOS is current provider", "BE-400",
            "Remove when VNPay adapter is fully retired or re-integrated", "disabled"),
        new("GET /api/v1/admin/payments", "Consolidated under /admin/finance/ledger", "BE-150",
            "Remove when standalone endpoint is deprecated", "disabled"),
    };

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Runtime routes missing from OpenAPI
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Runtime_Routes_Must_Exist_In_OpenApi()
    {
        var runtimeRoutes = GetRuntimeRoutes();
        var openApiOps = ParseOpenApiOperations();

        var openApiMethodPaths = openApiOps
            .Select(o => $"{o.Method} {ApiPrefix}{o.Path}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = runtimeRoutes
            .Select(r => $"{r.Method} {r.NormalizedPath}")
            .Where(key => !InfrastructureRouteAllowlist.Contains(key))
            .Where(key => !CapabilityNotImplementedAllowlist
                .Any(a => a.MethodPath.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(key => !openApiMethodPaths.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .ToList();

        missing.Should().BeEmpty(
            "all non-infrastructure runtime routes must be declared in OpenAPI.\n" +
            "Missing routes:\n" + string.Join("\n  ", missing));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. OpenAPI operations without runtime endpoint
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenApi_Operations_Must_Have_Runtime_Endpoint()
    {
        var runtimeRoutes = GetRuntimeRoutes();
        var openApiOps = ParseOpenApiOperations();

        var runtimeMethodPaths = runtimeRoutes
            .Select(r => $"{r.Method} {r.NormalizedPath}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = openApiOps
            .Select(op => (key: $"{op.Method} {ApiPrefix}{op.Path}", op.OperationId))
            .Where(x => !CapabilityNotImplementedAllowlist
                .Any(a => a.MethodPath.Equals(x.key, StringComparison.OrdinalIgnoreCase)))
            .Where(x => !runtimeMethodPaths.Contains(x.key))
            .Select(x => $"{x.key} (operationId: {x.OperationId})")
            .OrderBy(x => x)
            .ToList();

        missing.Should().BeEmpty(
            "all OpenAPI operations must have a corresponding runtime endpoint.\n" +
            "Phantom operations:\n  " + string.Join("\n  ", missing));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. OperationId uniqueness
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenApi_OperationIds_Must_Be_Unique()
    {
        var openApiOps = ParseOpenApiOperations();
        var duplicates = openApiOps
            .Where(o => !string.IsNullOrEmpty(o.OperationId))
            .GroupBy(o => o.OperationId)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(o => $"{o.Method} {o.Path}"))}")
            .ToList();

        duplicates.Should().BeEmpty("each operationId must be unique across the OpenAPI spec");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Duplicate method+path in OpenAPI
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenApi_Must_Not_Have_Duplicate_Method_Path()
    {
        var openApiOps = ParseOpenApiOperations();
        var duplicates = openApiOps
            .GroupBy(o => $"{o.Method} {o.Path}")
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty("each method+path combination must be unique in OpenAPI");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Path parameters declared but not in path template (and vice versa)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenApi_Path_Parameters_Must_Match_Path_Template()
    {
        var openApiOps = ParseOpenApiOperations();
        var issues = new List<string>();

        foreach (var op in openApiOps)
        {
            var templateParams = Regex.Matches(op.Path, @"\{(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var declaredPathParams = op.PathParameters
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var tp in templateParams)
            {
                if (!declaredPathParams.Contains(tp))
                    issues.Add($"{op.Method} {op.Path}: path template has '{{{tp}}}' but no path parameter declaration");
            }

            foreach (var dp in declaredPathParams)
            {
                if (!templateParams.Contains(dp))
                    issues.Add($"{op.Method} {op.Path}: declares path parameter '{dp}' but it does not appear in path template");
            }
        }

        issues.Should().BeEmpty(
            "path parameter declarations must match path template placeholders.\n" +
            string.Join("\n", issues));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Security requirement parity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Runtime_Auth_Must_Match_OpenApi_Security()
    {
        var runtimeRoutes = GetRuntimeRoutes();
        var openApiOps = ParseOpenApiOperations();

        var openApiSecurityMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in openApiOps)
        {
            var key = $"{op.Method} {ApiPrefix}{op.Path}";
            openApiSecurityMap.TryAdd(key, op.RequiresAuth);
        }

        var mismatches = new List<string>();
        foreach (var route in runtimeRoutes)
        {
            var key = $"{route.Method} {route.NormalizedPath}";
            if (InfrastructureRouteAllowlist.Contains(key)) continue;
            if (CapabilityNotImplementedAllowlist.Any(a =>
                    a.MethodPath.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            if (!openApiSecurityMap.TryGetValue(key, out var specRequiresAuth)) continue;

            if (route.RequiresAuth && !specRequiresAuth)
                mismatches.Add($"{key}: runtime requires auth but OpenAPI has security: []");
            else if (!route.RequiresAuth && specRequiresAuth)
                mismatches.Add($"{key}: runtime is anonymous but OpenAPI requires auth");
        }

        mismatches.Should().BeEmpty(
            "runtime authorization must match OpenAPI security declarations.\n" +
            string.Join("\n", mismatches));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Capability allowlist entries must be complete
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Capability_Allowlist_Entries_Must_Be_Complete()
    {
        foreach (var entry in CapabilityNotImplementedAllowlist)
        {
            entry.MethodPath.Should().NotBeNullOrWhiteSpace("must have method+path");
            entry.Reason.Should().NotBeNullOrWhiteSpace("must have reason");
            entry.TaskId.Should().MatchRegex(@"^BE-\d{3}$", "must reference valid task ID");
            entry.RemovalCondition.Should().NotBeNullOrWhiteSpace("must state removal condition");
            entry.ExpectedCapabilityState.Should().NotBeNullOrWhiteSpace("must declare capability state");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Runtime route extraction via reflection
    // ═══════════════════════════════════════════════════════════════════════

    private static List<RuntimeRoute> GetRuntimeRoutes()
    {
        var apiAssembly = typeof(Fitmate.Api.Auth.AuthController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        var routes = new List<RuntimeRoute>();

        foreach (var controllerType in controllerTypes)
        {
            // Get controller-level route prefix
            var controllerRoute = controllerType.GetCustomAttributes<RouteAttribute>(true)
                .FirstOrDefault()?.Template ?? "";

            // Get controller-level [Authorize]
            var controllerHasAuthorize = controllerType.GetCustomAttributes(true)
                .OfType<AuthorizeAttribute>().Any();
            var controllerHasAllowAnonymous = controllerType.GetCustomAttributes(true)
                .OfType<AllowAnonymousAttribute>().Any();

            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var httpMethodAttrs = method.GetCustomAttributes(true)
                    .OfType<HttpMethodAttribute>()
                    .ToList();

                if (httpMethodAttrs.Count == 0) continue;

                // Check method-level auth
                var methodHasAllowAnonymous = method.GetCustomAttributes(true)
                    .OfType<AllowAnonymousAttribute>().Any();
                var methodHasAuthorize = method.GetCustomAttributes(true)
                    .OfType<AuthorizeAttribute>().Any();

                bool requiresAuth;
                if (methodHasAllowAnonymous || controllerHasAllowAnonymous)
                    requiresAuth = false;
                else
                    requiresAuth = methodHasAuthorize || controllerHasAuthorize;

                foreach (var attr in httpMethodAttrs)
                {
                    foreach (var httpMethod in attr.HttpMethods)
                    {
                        var template = attr.Template ?? "";
                        var fullPath = CombineRoutes(controllerRoute, template);

                        // Normalize: ensure leading slash
                        if (!fullPath.StartsWith('/'))
                            fullPath = "/" + fullPath;

                        // Strip route constraints: {id:guid} -> {id}
                        fullPath = NormalizeConstraints(fullPath);

                        routes.Add(new RuntimeRoute(
                            httpMethod.ToUpperInvariant(),
                            fullPath,
                            requiresAuth,
                            controllerType.Name,
                            method.Name));
                    }
                }
            }
        }

        // Minimal API / middleware endpoints mapped in Program.cs
        routes.Add(new RuntimeRoute("GET", "/health/live", false, "Program", "MapHealthChecks"));
        routes.Add(new RuntimeRoute("GET", "/api/v1/health/live", false, "Program", "MapHealthChecks"));
        routes.Add(new RuntimeRoute("GET", "/health/ready", false, "Program", "MapHealthChecks"));
        routes.Add(new RuntimeRoute("GET", "/api/v1/health/ready", false, "Program", "MapHealthChecks"));

        return routes;
    }

    private static string CombineRoutes(string controllerRoute, string actionRoute)
    {
        if (actionRoute.StartsWith('/') || actionRoute.StartsWith("~/"))
            return actionRoute.TrimStart('~');

        if (string.IsNullOrEmpty(controllerRoute))
            return actionRoute;
        if (string.IsNullOrEmpty(actionRoute))
            return controllerRoute;

        controllerRoute = controllerRoute.TrimEnd('/');
        actionRoute = actionRoute.TrimStart('/');
        return $"{controllerRoute}/{actionRoute}";
    }

    private static string NormalizeConstraints(string path)
    {
        // {id:guid} -> {id}, {requestId:guid} -> {requestId}
        var normalized = Regex.Replace(path, @"\{(\w+):\w+\}", "{$1}");
        // Canonicalize trainer path param variants so user dirty controllers don't drift from OpenAPI {id}
        normalized = Regex.Replace(normalized, @"\{ptUserId\}", "{id}");
        normalized = Regex.Replace(normalized, @"\{ptId\}", "{id}");
        return normalized;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OpenAPI YAML parsing (minimal, no external dependency)
    // ═══════════════════════════════════════════════════════════════════════

    private static List<OpenApiOperation> ParseOpenApiOperations()
    {
        var specPath = FindOpenApiSpecPath();
        var lines = File.ReadAllLines(specPath);
        return ParseOperationsFromLines(lines);
    }

    internal static List<OpenApiOperation> ParseOperationsFromLines(string[] lines)
    {
        var operations = new List<OpenApiOperation>();

        string? currentPath = null;
        string? currentMethod = null;
        string? currentOperationId = null;
        var currentPathParams = new List<string>();
        bool hasEmptySecurity = false;
        bool inParameters = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var content = line.Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;

            var indent = line.Length - line.TrimStart().Length;

            // Path entry at indent 2: "  /auth/register:"
            var pathMatch = Regex.Match(content, @"^(/[a-zA-Z0-9_\-{}/]*):$");
            if (indent == 2 && pathMatch.Success)
            {
                FlushOperation(operations, currentPath, currentMethod, currentOperationId,
                    currentPathParams, hasEmptySecurity);

                currentPath = pathMatch.Groups[1].Value;
                currentMethod = null;
                currentOperationId = null;
                currentPathParams = new List<string>();
                hasEmptySecurity = false;
                inParameters = false;
                continue;
            }

            // Method entry at indent 4: "    post:"
            var methodMatch = Regex.Match(content,
                @"^(get|post|put|patch|delete|head|options):$", RegexOptions.IgnoreCase);
            if (indent == 4 && currentPath != null && methodMatch.Success)
            {
                FlushOperation(operations, currentPath, currentMethod, currentOperationId,
                    currentPathParams, hasEmptySecurity);

                currentMethod = methodMatch.Groups[1].Value.ToUpperInvariant();
                currentOperationId = null;
                currentPathParams = new List<string>();
                hasEmptySecurity = false;
                inParameters = false;
                continue;
            }

            // Inside a method block (indent > 4)
            if (currentMethod != null && indent > 4)
            {
                var opIdMatch = Regex.Match(content, @"^operationId:\s*(.+)$");
                if (opIdMatch.Success)
                {
                    currentOperationId = opIdMatch.Groups[1].Value.Trim();
                    continue;
                }

                if (content.StartsWith("security:"))
                {
                    if (content.Contains("[]"))
                        hasEmptySecurity = true;
                    continue;
                }

                if (content == "parameters:")
                {
                    inParameters = true;
                    continue;
                }

                if (inParameters)
                {
                    if (content.Contains("IdPath"))
                    {
                        currentPathParams.Add("id");
                    }
                    else if (Regex.IsMatch(content, @"in:\s*path") && Regex.IsMatch(content, @"name:\s*(\w+)"))
                    {
                        var m = Regex.Match(content, @"name:\s*(\w+)");
                        currentPathParams.Add(m.Groups[1].Value);
                    }
                    else if (content.StartsWith("- name:"))
                    {
                        var paramName = Regex.Match(content, @"name:\s*(\w+)").Groups[1].Value;
                        if (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("in: path"))
                        {
                            currentPathParams.Add(paramName);
                        }
                    }
                    else if (content.StartsWith("- in: path"))
                    {
                        if (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("name:"))
                        {
                            var paramName = Regex.Match(lines[i + 1].Trim(), @"name:\s*(\w+)").Groups[1].Value;
                            currentPathParams.Add(paramName);
                        }
                    }
                }
            }

            // Reset when leaving path/method scope
            if (indent <= 2 && currentPath != null && !pathMatch.Success && !content.StartsWith("#"))
            {
                FlushOperation(operations, currentPath, currentMethod, currentOperationId,
                    currentPathParams, hasEmptySecurity);
                currentMethod = null;
                if (!content.EndsWith(":") || indent < 2)
                    currentPath = null;
            }
        }

        // Final operation
        FlushOperation(operations, currentPath, currentMethod, currentOperationId,
            currentPathParams, hasEmptySecurity);

        return operations;
    }

    private static void FlushOperation(
        List<OpenApiOperation> operations,
        string? path, string? method, string? operationId,
        List<string> pathParams, bool hasEmptySecurity)
    {
        if (path != null && method != null)
        {
            operations.Add(new OpenApiOperation(
                path, method, operationId ?? "",
                new List<string>(pathParams),
                !hasEmptySecurity));
        }
    }

    private static string FindOpenApiSpecPath()
    {
        // Walk up from assembly dir
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, OpenApiRelativePath);
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // Fallback: CWD
        var fromCwd = Path.Combine(Environment.CurrentDirectory, OpenApiRelativePath);
        if (File.Exists(fromCwd)) return fromCwd;

        throw new FileNotFoundException(
            $"Cannot locate OpenAPI spec at '{OpenApiRelativePath}'. " +
            "Ensure the test is run from the repository root.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Records
    // ═══════════════════════════════════════════════════════════════════════

    private sealed record RuntimeRoute(
        string Method,
        string NormalizedPath,
        bool RequiresAuth,
        string Controller,
        string Action);

    internal sealed record OpenApiOperation(
        string Path,
        string Method,
        string OperationId,
        List<string> PathParameters,
        bool RequiresAuth);

    private sealed record CapabilityAllowlistEntry(
        string MethodPath,
        string Reason,
        string TaskId,
        string RemovalCondition,
        string ExpectedCapabilityState);
}
