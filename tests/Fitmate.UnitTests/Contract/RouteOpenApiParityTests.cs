using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

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
/// - Duplicate YAML mapping keys (such as duplicate content: in responses)
/// - Unresolved $ref schema and response references
/// - Missing response descriptions
/// - OpenApiReader diagnostic errors
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
    /// STRICT RULE: ONLY AI operations pending implementation are permitted.
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
    };

    // ═══════════════════════════════════════════════════════════════════════
    // 0. OpenAPI Document Strict Validation (Diagnostic, Duplicate Keys, Refs)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OpenApi_Document_Must_Be_Valid_Without_Diagnostic_Errors()
    {
        var (_, diagnostic) = LoadOpenApiDocument();
        diagnostic.Errors.Should().BeEmpty(
            "OpenAPI document must not have reader diagnostic errors:\n" +
            string.Join("\n", diagnostic.Errors.Select(e => $"{e.Pointer}: {e.Message}")));
    }

    [Fact]
    public void OpenApi_Must_Not_Have_Duplicate_Yaml_Mapping_Keys()
    {
        var (_, diagnostic) = LoadOpenApiDocument();
        var duplicateKeyErrors = diagnostic.Errors
            .Where(e => e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            .ToList();

        duplicateKeyErrors.Should().BeEmpty(
            "OpenAPI YAML must not have duplicate mapping keys (e.g. duplicate content: blocks):\n" +
            string.Join("\n", duplicateKeyErrors.Select(e => $"{e.Pointer}: {e.Message}")));
    }

    [Fact]
    public void OpenApi_Responses_Must_Have_Descriptions()
    {
        var (doc, _) = LoadOpenApiDocument();
        doc.Should().NotBeNull();
        if (doc.Paths == null) return;
        var missingDescriptions = new List<string>();

        foreach (var (path, pathItem) in doc.Paths)
        {
            foreach (var (method, op) in pathItem.Operations)
            {
                foreach (var (statusCode, resp) in op.Responses)
                {
                    if (string.IsNullOrWhiteSpace(resp.Description))
                    {
                        missingDescriptions.Add($"{method} {path} [{statusCode}]: response missing description");
                    }
                }
            }
        }

        missingDescriptions.Should().BeEmpty(
            "all responses must have non-empty description:\n" +
            string.Join("\n", missingDescriptions));
    }

    [Fact]
    public void OpenApi_Schema_References_Must_Resolve()
    {
        var (doc, _) = LoadOpenApiDocument();
        doc.Should().NotBeNull();
        if (doc.Components == null) return;
        var declaredSchemas = doc.Components.Schemas.Keys.ToHashSet(StringComparer.Ordinal);
        var declaredResponses = doc.Components.Responses.Keys.ToHashSet(StringComparer.Ordinal);

        var specPath = FindOpenApiSpecPath();
        var lines = File.ReadAllLines(specPath);
        var unresolved = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"\$ref:\s*['""]?#/components/(schemas|responses)/([a-zA-Z0-9_\-]+)['""]?");
            if (match.Success)
            {
                var type = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                if (type == "schemas" && !declaredSchemas.Contains(name))
                    unresolved.Add($"Line {i + 1}: unresolved schema reference '#/components/schemas/{name}'");
                else if (type == "responses" && !declaredResponses.Contains(name))
                    unresolved.Add($"Line {i + 1}: unresolved response reference '#/components/responses/{name}'");
            }
        }

        unresolved.Should().BeEmpty(
            "all schema and response $refs must resolve:\n" +
            string.Join("\n", unresolved));
    }

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
        // Strip route constraints only: {id:guid} -> {id}, {requestId:guid} -> {requestId}
        return Regex.Replace(path, @"\{(\w+):\w+\}", "{$1}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OpenAPI Document Loading & Validation (OpenAPI.NET Parser)
    // ═══════════════════════════════════════════════════════════════════════

    private static (OpenApiDocument Document, OpenApiDiagnostic Diagnostic) LoadOpenApiDocument()
    {
        var specPath = FindOpenApiSpecPath();
        using var stream = File.OpenRead(specPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        return (doc, diagnostic);
    }

    private static HashSet<string> GetExplicitAnonymousOperations()
    {
        var specPath = FindOpenApiSpecPath();
        var lines = File.ReadAllLines(specPath);
        var anon = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? currentPath = null;
        string? currentMethod = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            var indent = line.Length - line.TrimStart().Length;

            var pathMatch = Regex.Match(trimmed, @"^(/[a-zA-Z0-9_\-{}/]*):$");
            if (indent == 2 && pathMatch.Success)
            {
                currentPath = pathMatch.Groups[1].Value;
                currentMethod = null;
                continue;
            }

            var methodMatch = Regex.Match(trimmed, @"^(get|post|put|patch|delete|head|options):$", RegexOptions.IgnoreCase);
            if (indent == 4 && currentPath != null && methodMatch.Success)
            {
                currentMethod = methodMatch.Groups[1].Value.ToUpperInvariant();
                continue;
            }

            if (currentMethod != null && indent > 4)
            {
                if (trimmed.StartsWith("security:") && trimmed.Contains("[]"))
                {
                    anon.Add($"{currentMethod} {currentPath}");
                }
            }
        }

        return anon;
    }

    private static List<OpenApiOperation> ParseOpenApiOperations()
    {
        var (doc, _) = LoadOpenApiDocument();
        var operations = new List<OpenApiOperation>();
        if (doc?.Paths == null) return operations;

        var anonymousOps = GetExplicitAnonymousOperations();

        foreach (var (path, pathItem) in doc.Paths)
        {
            foreach (var (methodType, op) in pathItem.Operations)
            {
                var method = methodType.ToString().ToUpperInvariant();
                var pathParams = new List<string>();

                if (op.Parameters != null)
                {
                    pathParams.AddRange(op.Parameters
                        .Where(p => p.In == ParameterLocation.Path)
                        .Select(p => p.Name));
                }

                if (pathItem.Parameters != null)
                {
                    pathParams.AddRange(pathItem.Parameters
                        .Where(p => p.In == ParameterLocation.Path)
                        .Select(p => p.Name));
                }

                bool requiresAuth = !anonymousOps.Contains($"{method} {path}");

                operations.Add(new OpenApiOperation(
                    path,
                    method,
                    op.OperationId ?? "",
                    pathParams.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    requiresAuth));
            }
        }

        return operations;
    }

    internal static List<string> FindDuplicateYamlKeys(string[] lines)
    {
        var duplicates = new List<string>();
        var scopes = new Stack<YamlScope>();
        bool inMultiline = false;
        int multilineIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            int indent = rawLine.Length - rawLine.TrimStart().Length;

            if (inMultiline)
            {
                if (indent > multilineIndent)
                    continue;
                inMultiline = false;
            }

            if (trimmed.EndsWith('|') || trimmed.EndsWith('>') ||
                trimmed.EndsWith("|-") || trimmed.EndsWith(">-") ||
                trimmed.EndsWith("|+") || trimmed.EndsWith(">+"))
            {
                inMultiline = true;
                multilineIndent = indent;
            }

            bool isListItem = trimmed.StartsWith("- ");
            string lineToParse = isListItem ? trimmed[2..].Trim() : trimmed;

            var keyMatch = Regex.Match(lineToParse, @"^([a-zA-Z0-9_\-/'""$#@.+]+|""[^""]+""|'[^']+'):(\s.*)?$");
            if (!keyMatch.Success)
                continue;

            var key = keyMatch.Groups[1].Value.Trim('\'', '"');

            while (scopes.Count > 0 && scopes.Peek().Indent > indent)
            {
                scopes.Pop();
            }

            if (scopes.Count > 0 && scopes.Peek().Indent == indent)
            {
                if (isListItem)
                {
                    scopes.Pop();
                    var newScope = new YamlScope(indent);
                    newScope.Keys.Add(key);
                    scopes.Push(newScope);
                }
                else
                {
                    if (scopes.Peek().Keys.Contains(key))
                    {
                        duplicates.Add($"Line {i + 1}: duplicate mapping key '{key}' at indent {indent}");
                    }
                    else
                    {
                        scopes.Peek().Keys.Add(key);
                    }
                }
            }
            else
            {
                var newScope = new YamlScope(indent);
                newScope.Keys.Add(key);
                scopes.Push(newScope);
            }
        }

        return duplicates;
    }

    private sealed class YamlScope
    {
        public int Indent { get; }
        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
        public YamlScope(int indent) => Indent = indent;
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
