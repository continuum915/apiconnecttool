using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>
/// OpenAPI 3.x import (export is out of scope). Operations
/// are read with raw <see cref="JsonElement"/> navigation rather than a full strongly-typed OpenAPI
/// model, since the schema shape is irregular (arbitrary path-string keys, a "schema" object that
/// can be a $ref or any of several inline shapes) and this importer's contract is explicitly
/// best-effort - degrading gracefully fits looser, less rigid parsing better than a DTO type that
/// would have to model every OpenAPI schema variant to deserialize at all.
/// </summary>
public sealed class OpenApiImporter : IOpenApiImporter
{
    private static readonly string[] SupportedMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];
    private static readonly Regex PathParamPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex IdentifierBoundaryPattern = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex ParamTokenPattern = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);
    private const int MaxSchemaDepth = 6;

    public RequestCollection Import(string json) => ImportWithReport(json).Collection;

    public OpenApiImportResult ImportWithReport(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OpenApiImportException("The file is not a valid OpenAPI document.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("paths", out var pathsElement) ||
                pathsElement.ValueKind != JsonValueKind.Object)
            {
                throw new OpenApiImportException("The file is not a valid OpenAPI document (missing \"paths\").");
            }

            var collection = new RequestCollection { Name = GetCollectionName(root) };
            var baseUrl = GetBaseUrl(root);
            if (!string.IsNullOrEmpty(baseUrl)) collection.Variables.Add(new() { Key = "baseUrl", InitialValue = baseUrl });
            var requestBaseUrl = string.IsNullOrEmpty(baseUrl) ? baseUrl : "{{baseUrl}}";
            var mappedSchemes = new List<string?>();
            var report = new OpenApiImportReport();
            var schemas = root.TryGetProperty("components", out var componentsElement) &&
                          componentsElement.TryGetProperty("schemas", out var schemasElement)
                ? schemasElement
                : default;
            var securitySchemes = componentsElement.ValueKind == JsonValueKind.Object &&
                                  componentsElement.TryGetProperty("securitySchemes", out var securitySchemesElement)
                ? securitySchemesElement
                : default;
            var rootSecurity = root.TryGetProperty("security", out var rootSecurityElement)
                ? rootSecurityElement
                : default;

            var nextFolderSortOrder = 0;
            var nextRootRequestSortOrder = 0;
            var nextFolderRequestSortOrder = new Dictionary<Guid, int>();
            var foldersByTag = new Dictionary<string, CollectionFolder>(StringComparer.Ordinal);
            foreach (var pathProperty in pathsElement.EnumerateObject())
            {
                if (pathProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var pathTemplate = RewritePathParams(pathProperty.Name);
                foreach (var methodProperty in pathProperty.Value.EnumerateObject())
                {
                    var method = methodProperty.Name.ToLowerInvariant();
                    if (Array.IndexOf(SupportedMethods, method) < 0 || methodProperty.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var primaryTag = GetPrimaryTag(methodProperty.Value);
                    Guid? folderId = null;
                    if (primaryTag is not null)
                    {
                        if (!foldersByTag.TryGetValue(primaryTag, out var folder))
                        {
                            folder = new CollectionFolder
                            {
                                Name = primaryTag,
                                SortOrder = nextFolderSortOrder++
                            };
                            foldersByTag.Add(primaryTag, folder);
                            collection.Folders.Add(folder);
                        }

                        folderId = folder.Id;
                    }

                    var request = ImportOperation(method, pathTemplate, requestBaseUrl, methodProperty.Value, schemas);
                    request.Auth.Type = AuthType.None;
                    var security = methodProperty.Value.TryGetProperty("security", out var operationSecurity)
                        ? operationSecurity
                        : rootSecurity;
                    mappedSchemes.Add(ApplySecurity(request, security, securitySchemes, report));
                    request.FolderId = folderId;
                    if (folderId is { } assignedFolderId)
                    {
                        request.SortOrder = nextFolderRequestSortOrder.TryGetValue(assignedFolderId, out var currentSortOrder)
                            ? currentSortOrder
                            : 0;
                        nextFolderRequestSortOrder[assignedFolderId] = request.SortOrder + 1;
                    }
                    else
                    {
                        request.SortOrder = nextRootRequestSortOrder++;
                    }
                    collection.Requests.Add(request);
                }
            }

            if (mappedSchemes.Count > 0 && mappedSchemes[0] is not null && mappedSchemes.All(scheme => scheme == mappedSchemes[0]))
            {
                collection.Auth = collection.Requests[0].Auth.Clone();
                foreach (var request in collection.Requests) request.Auth = new() { Type = AuthType.Inherit };
            }
            return new OpenApiImportResult(collection, report);
        }
    }

    private static string GetCollectionName(JsonElement root) =>
        root.TryGetProperty("info", out var info) &&
        info.TryGetProperty("title", out var title) &&
        title.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(title.GetString())
            ? title.GetString()!
            : "Imported OpenAPI Collection";

    private static string GetBaseUrl(JsonElement root)
    {
        if (root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in servers.EnumerateArray())
            {
                if (server.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                {
                    return (urlElement.GetString() ?? string.Empty).TrimEnd('/');
                }
            }
        }

        return string.Empty;
    }

    private static string RewritePathParams(string path) => PathParamPattern.Replace(path, "{{$1}}");

    /// <summary>Returns the first non-empty operation tag. ApiConnectTool currently supports one folder
    /// assignment per request, so additional OpenAPI tags are intentionally not duplicated.</summary>
    private static string? GetPrimaryTag(JsonElement operation)
    {
        if (!operation.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()))
            {
                return tag.GetString()!.Trim();
            }
        }

        return null;
    }

    private static string? ApplySecurity(
        RequestItem request,
        JsonElement security,
        JsonElement securitySchemes,
        OpenApiImportReport report)
    {
        if (security.ValueKind != JsonValueKind.Array || security.GetArrayLength() == 0)
        {
            return null;
        }

        report.SecuredRequestCount++;
        var mapped = false;
        string? mappedScheme = null;
        var alternativeCount = security.GetArrayLength();
        if (alternativeCount > 1)
        {
            report.AddWarning($"OpenAPI security defines {alternativeCount} alternatives; ApiConnectTool keeps one auth configuration per request and selected the first supported alternative.");
        }

        foreach (var requirement in security.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object)
            {
                report.AddWarning("An OpenAPI security requirement was not an object and could not be mapped.");
                continue;
            }

            var entries = requirement.EnumerateObject().ToArray();
            if (entries.Length == 0)
            {
                continue;
            }

            if (entries.Length > 1)
            {
                report.AddWarning("An OpenAPI security requirement combines multiple schemes; ApiConnectTool keeps only the first mapped scheme.");
            }

            foreach (var entry in entries)
            {
                if (securitySchemes.ValueKind != JsonValueKind.Object ||
                    !securitySchemes.TryGetProperty(entry.Name, out var scheme))
                {
                    report.AddWarning($"OpenAPI security scheme '{entry.Name}' was not defined and was not mapped.");
                    continue;
                }

                if (TryMapSecurityScheme(scheme, request.Auth, out var warning))
                {
                    mapped = true;
                    mappedScheme = entry.Name + ":" + JsonSerializer.Serialize(entry.Value);
                    if (request.Auth.Type == AuthType.OAuth2 && entry.Value.ValueKind == JsonValueKind.Array && entry.Value.GetArrayLength() > 0)
                        request.Auth.Scope = string.Join(" ", entry.Value.EnumerateArray().Where(scope => scope.ValueKind == JsonValueKind.String).Select(scope => scope.GetString()));
                    break;
                }

                report.AddWarning(warning ?? $"OpenAPI security scheme '{entry.Name}' is not supported and was not mapped.");
            }

            if (mapped)
            {
                break;
            }
        }

        if (mapped)
        {
            report.MappedRequestCount++;
        }
        else
        {
            report.AddWarning("One or more secured OpenAPI requests could not be mapped and will import without authentication.");
        }
        return mappedScheme;
    }

    private static bool TryMapSecurityScheme(JsonElement scheme, RequestAuth auth, out string? warning)
    {
        warning = null;
        if (!scheme.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            warning = "An OpenAPI security scheme has no supported type and was not mapped.";
            return false;
        }

        var type = typeElement.GetString()?.Trim();
        if (string.Equals(type, "http", StringComparison.OrdinalIgnoreCase))
        {
            var schemeName = scheme.TryGetProperty("scheme", out var schemeElement) && schemeElement.ValueKind == JsonValueKind.String
                ? schemeElement.GetString()
                : null;
            if (string.Equals(schemeName, "basic", StringComparison.OrdinalIgnoreCase))
            {
                auth.Type = AuthType.Basic;
                return true;
            }

            if (string.Equals(schemeName, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                auth.Type = AuthType.Bearer;
                return true;
            }

            warning = $"OpenAPI HTTP authentication scheme '{schemeName ?? "(missing)"}' is not supported and was not mapped.";
            return false;
        }

        if (string.Equals(type, "apiKey", StringComparison.OrdinalIgnoreCase))
        {
            var name = scheme.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()?.Trim()
                : null;
            var location = scheme.TryGetProperty("in", out var inElement) && inElement.ValueKind == JsonValueKind.String
                ? inElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                warning = "OpenAPI API key authentication has no key name and was not mapped.";
                return false;
            }

            if (string.Equals(location, "header", StringComparison.OrdinalIgnoreCase))
            {
                auth.Type = AuthType.ApiKey;
                auth.ApiKeyHeaderName = name;
                return true;
            }

            if (string.Equals(location, "query", StringComparison.OrdinalIgnoreCase))
            {
                auth.Type = AuthType.ApiKey;
                auth.ApiKeyQueryParam = name;
                return true;
            }

            warning = $"OpenAPI API key location '{location ?? "(missing)"}' is not supported and was not mapped.";
            return false;
        }

        if (string.Equals(type, "oauth2", StringComparison.OrdinalIgnoreCase))
        {
            if (!scheme.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Object)
            {
                warning = "OpenAPI OAuth2 authentication has no flows and was not mapped.";
                return false;
            }

            JsonElement flow;
            OAuthGrantType grantType;
            if (flows.TryGetProperty("clientCredentials", out flow))
            {
                grantType = OAuthGrantType.ClientCredentials;
            }
            else if (flows.TryGetProperty("authorizationCode", out flow))
            {
                grantType = OAuthGrantType.AuthorizationCodePkce;
            }
            else
            {
                warning = "OpenAPI OAuth2 flow is not supported; only clientCredentials and authorizationCode can be mapped.";
                return false;
            }

            auth.Type = AuthType.OAuth2;
            auth.GrantType = grantType;
            auth.TokenUrl = GetStringProperty(flow, "tokenUrl");
            auth.AuthorizationUrl = GetStringProperty(flow, "authorizationUrl");
            auth.Scope = GetObjectPropertyNames(flow, "scopes");
            return true;
        }

        warning = $"OpenAPI security scheme type '{type ?? "(missing)"}' is not supported and was not mapped.";
        return false;
    }

    private static string GetStringProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string GetObjectPropertyNames(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? string.Join(' ', value.EnumerateObject().Select(property => property.Name))
            : string.Empty;

    private static RequestItem ImportOperation(string method, string pathTemplate, string baseUrl, JsonElement operation, JsonElement schemas)
    {
        var request = new RequestItem
        {
            Name = GetOperationName(method, pathTemplate, operation),
            Method = new HttpMethod(method.ToUpperInvariant()),
            Url = baseUrl + pathTemplate
        };

        if (operation.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                ApplyParameter(request, parameter);
            }
        }

        if (operation.TryGetProperty("requestBody", out var requestBody))
        {
            ApplyRequestBody(request, requestBody, schemas);
        }

        return request;
    }

    private static string GetOperationName(string method, string pathTemplate, JsonElement operation)
    {
        if (operation.TryGetProperty("operationId", out var operationId) &&
            operationId.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(operationId.GetString()))
        {
            var humanizedId = HumanizeIdentifier(operationId.GetString()!);
            if (IsCompactSummary(humanizedId))
            {
                return humanizedId;
            }
        }

        if (operation.TryGetProperty("summary", out var summary) &&
            summary.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(summary.GetString()))
        {
            var compactSummary = CollapseWhitespace(summary.GetString()!);
            if (IsCompactSummary(compactSummary))
            {
                return compactSummary;
            }
        }

        return BuildPathName(method, pathTemplate);
    }

    private static bool IsCompactSummary(string summary) =>
        summary.Length <= 72 &&
        !(summary.Length > 24 && ".!?".Contains(summary[^1]));

    private static string BuildPathName(string method, string pathTemplate)
    {
        var segments = pathTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => ParamTokenPattern.Replace(segment, string.Empty))
            .Where(segment => segment.Length > 0 &&
                              !Regex.IsMatch(segment, "^v\\d+$", RegexOptions.IgnoreCase))
            .Select(HumanizeIdentifier)
            .ToArray();

        return segments.Length == 0
            ? method.ToUpperInvariant()
            : $"{method.ToUpperInvariant()} {string.Join(" ", segments)}";
    }

    private static string HumanizeIdentifier(string value)
    {
        var humanized = IdentifierBoundaryPattern.Replace(value.Trim(), " ")
            .Replace('-', ' ')
            .Replace('_', ' ');
        humanized = CollapseWhitespace(humanized);
        return humanized.Length == 0
            ? value.Trim()
            : char.ToUpperInvariant(humanized[0]) + humanized[1..];
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// "path" parameters are already represented in the URL template itself (rewritten to
    /// {{param}}); "cookie" parameters have no dedicated per-request field in this app's model
    /// (cookies are jar-based, 4e) so are skipped rather than misrepresented as a header.
    /// </summary>
    private static void ApplyParameter(RequestItem request, JsonElement parameter)
    {
        if (parameter.ValueKind != JsonValueKind.Object ||
            !parameter.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var location = parameter.TryGetProperty("in", out var inElement) && inElement.ValueKind == JsonValueKind.String
            ? inElement.GetString()
            : null;
        var row = new KeyValueItem { IsEnabled = true, Key = nameElement.GetString()!, Value = string.Empty };

        switch (location)
        {
            case "query":
                request.QueryParams.Add(row);
                break;
            case "header":
                request.Headers.Add(row);
                break;
        }
    }

    private static void ApplyRequestBody(RequestItem request, JsonElement requestBody, JsonElement schemas)
    {
        if (requestBody.ValueKind != JsonValueKind.Object ||
            !requestBody.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("application/json", out var jsonContent) ||
            jsonContent.ValueKind != JsonValueKind.Object)
        {
            // Only a JSON request body is generated; anything else (multipart, xml, no content at
            // all) is left as BodyMode.None rather than guessing a representation for it.
            return;
        }

        request.BodyMode = RequestBodyMode.Raw;
        request.BodyContentType = "application/json";
        request.Body = jsonContent.TryGetProperty("schema", out var schema)
            ? BuildSkeleton(schema, schemas, depth: 0).ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            : "{}";
    }

    /// <summary>
    /// Best-effort JSON skeleton from an OpenAPI schema: one level of $ref resolution per step
    /// (bounded by <see cref="MaxSchemaDepth"/> against cycles), placeholder values per primitive
    /// type, recursion into object properties and array items. oneOf/anyOf degrade to an empty
    /// object rather than guessing a branch; allOf attempts a shallow property merge since that's
    /// usually just schema composition, not a real choice between alternatives.
    /// </summary>
    private static JsonNode BuildSkeleton(JsonElement schema, JsonElement schemas, int depth)
    {
        if (depth > MaxSchemaDepth || schema.ValueKind != JsonValueKind.Object)
        {
            return new JsonObject();
        }

        if (schema.TryGetProperty("$ref", out var refElement) && refElement.ValueKind == JsonValueKind.String)
        {
            var resolved = ResolveRef(refElement.GetString()!, schemas);
            return resolved is null ? new JsonObject() : BuildSkeleton(resolved.Value, schemas, depth + 1);
        }

        if (schema.TryGetProperty("oneOf", out _) || schema.TryGetProperty("anyOf", out _))
        {
            return new JsonObject();
        }

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            var merged = new JsonObject();
            foreach (var subSchema in allOf.EnumerateArray())
            {
                if (BuildSkeleton(subSchema, schemas, depth + 1) is not JsonObject subObject)
                {
                    continue;
                }

                foreach (var property in subObject.ToArray())
                {
                    subObject.Remove(property.Key);
                    merged[property.Key] = property.Value;
                }
            }

            return merged;
        }

        var type = schema.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;
        var hasProperties = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;

        if (type == "object" || (type is null && hasProperties))
        {
            var obj = new JsonObject();
            if (hasProperties)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    obj[property.Name] = BuildSkeleton(property.Value, schemas, depth + 1);
                }
            }

            return obj;
        }

        if (type == "array")
        {
            var array = new JsonArray();
            if (schema.TryGetProperty("items", out var items))
            {
                array.Add(BuildSkeleton(items, schemas, depth + 1));
            }

            return array;
        }

        return type switch
        {
            "string" => JsonValue.Create(string.Empty),
            "integer" or "number" => JsonValue.Create(0),
            "boolean" => JsonValue.Create(false),
            _ => new JsonObject()
        };
    }

    private static JsonElement? ResolveRef(string reference, JsonElement schemas)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal) || schemas.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = reference[prefix.Length..];
        return schemas.TryGetProperty(name, out var resolved) ? resolved : null;
    }
}
