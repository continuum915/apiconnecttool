using System.Linq;
using System.Net.Http;
using System.Text.Json;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class OpenApiImporterTests
{
    private readonly OpenApiImporter _importer = new();

    private const string FixtureSpec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Pets API" },
          "servers": [ { "url": "https://api.example.test/v1" } ],
          "paths": {
            "/pets/{petId}": {
              "get": {
                "summary": "Get a pet",
                "parameters": [
                  { "name": "petId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "verbose", "in": "query", "schema": { "type": "boolean" } },
                  { "name": "X-Trace-Id", "in": "header", "schema": { "type": "string" } }
                ]
              }
            },
            "/pets": {
              "post": {
                "operationId": "createPet",
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/Pet" }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Pet": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "age": { "type": "integer" },
                  "tags": { "type": "array", "items": { "type": "string" } },
                  "owner": {
                    "type": "object",
                    "properties": { "email": { "type": "string" } }
                  }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Import_CollectionName_ComesFromInfoTitle()
    {
        var collection = _importer.Import(FixtureSpec);

        Assert.Equal("Pets API", collection.Name);
    }

    [Fact]
    public void Import_UntaggedOperations_StayAtCollectionRootWithNoFolders()
    {
        var collection = _importer.Import(FixtureSpec);

        Assert.Empty(collection.Folders);
        Assert.All(collection.Requests, r => Assert.Null(r.FolderId));
    }

    [Fact]
    public void Import_PathParameter_IsRewrittenToApiConnectToolVariableSyntaxInUrl()
    {
        var collection = _importer.Import(FixtureSpec);

        var getPet = collection.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Equal("{{baseUrl}}/pets/{{petId}}", getPet.Url);
    }

    [Fact]
    public void Import_OperationName_PrefersSummaryOverOperationId()
    {
        var collection = _importer.Import(FixtureSpec);

        var getPet = collection.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Equal("Get a pet", getPet.Name);
    }

    [Fact]
    public void Import_OperationWithNoSummary_FallsBackToOperationId()
    {
        var collection = _importer.Import(FixtureSpec);

        var createPet = collection.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("Create Pet", createPet.Name);
    }

    [Fact]
    public void Import_OperationId_IsHumanizedWhenPresent()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Pets API" },
              "paths": { "/pets": { "get": { "operationId": "listAllPets" } } }
            }
            """;

        Assert.Equal("List All Pets", _importer.Import(spec).Requests.Single().Name);
    }

    [Fact]
    public void Import_VerboseProseSummary_FallsBackToCompactPathName()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Sample API" },
              "paths": {
                "/v1/accountingDocuments/{invoiceId}/transferResponses": {
                  "post": {
                    "summary": "Marks invoice transfer as accepted / rejected by receiving system. Used with invoices having a specific processing mode and additional integration rules."
                  }
                }
              }
            }
            """;

        var request = _importer.Import(spec).Requests.Single();

        Assert.Equal("POST Accounting Documents Transfer Responses", request.Name);
        Assert.DoesNotContain("accepted / rejected", request.Name);
    }

    [Fact]
    public void Import_ShortSummary_RemainsTheDisplayName()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Pets API" },
              "paths": { "/pets": { "get": { "summary": "List pets" } } }
            }
            """;

        Assert.Equal("List pets", _importer.Import(spec).Requests.Single().Name);
    }

    [Fact]
    public void Import_QueryParameter_IsAddedToQueryParams()
    {
        var collection = _importer.Import(FixtureSpec);

        var getPet = collection.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Contains(getPet.QueryParams, p => p.Key == "verbose");
    }

    [Fact]
    public void Import_HeaderParameter_IsAddedToHeaders()
    {
        var collection = _importer.Import(FixtureSpec);

        var getPet = collection.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Contains(getPet.Headers, h => h.Key == "X-Trace-Id");
    }

    [Fact]
    public void Import_PathParameter_IsNotDuplicatedIntoQueryParamsOrHeaders()
    {
        var collection = _importer.Import(FixtureSpec);

        var getPet = collection.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.DoesNotContain(getPet.QueryParams, p => p.Key == "petId");
        Assert.DoesNotContain(getPet.Headers, h => h.Key == "petId");
    }

    [Fact]
    public void Import_RequestBodyWithRefSchema_GeneratesJsonBodySkeletonWithNestedFieldsAndArray()
    {
        var collection = _importer.Import(FixtureSpec);

        var createPet = collection.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal(ApiConnectTool.Models.RequestBodyMode.Raw, createPet.BodyMode);
        Assert.Equal("application/json", createPet.BodyContentType);

        using var body = JsonDocument.Parse(createPet.Body);
        var root = body.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("age").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("tags").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("owner").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("owner").GetProperty("email").ValueKind);
    }

    [Fact]
    public void Import_OneOfBodySchema_DegradesToEmptyPlaceholderRatherThanFailing()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "OneOf API" },
              "paths": {
                "/widgets": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "oneOf": [
                              { "$ref": "#/components/schemas/A" },
                              { "$ref": "#/components/schemas/B" }
                            ]
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "A": { "type": "object", "properties": { "a": { "type": "string" } } },
                  "B": { "type": "object", "properties": { "b": { "type": "string" } } }
                }
              }
            }
            """;

        var collection = _importer.Import(spec);

        var request = collection.Requests.Single();
        Assert.Equal(ApiConnectTool.Models.RequestBodyMode.Raw, request.BodyMode);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        Assert.Empty(body.RootElement.EnumerateObject());
    }

    [Fact]
    public void Import_RefCycleInSchema_DoesNotHangOrStackOverflow()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Cycle API" },
              "paths": {
                "/nodes": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": { "schema": { "$ref": "#/components/schemas/Node" } }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Node": {
                    "type": "object",
                    "properties": {
                      "name": { "type": "string" },
                      "child": { "$ref": "#/components/schemas/Node" }
                    }
                  }
                }
              }
            }
            """;

        var collection = _importer.Import(spec);

        // Just needs to terminate and produce something - the exact placeholder depth reached
        // once MaxSchemaDepth caps the recursion isn't the point of this test.
        var request = collection.Requests.Single();
        Assert.False(string.IsNullOrEmpty(request.Body));
    }

    [Fact]
    public void Import_NoServersDeclared_LeavesUrlAsPathOnly()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "No Server API" },
              "paths": { "/ping": { "get": {} } }
            }
            """;

        var collection = _importer.Import(spec);

        Assert.Equal("/ping", collection.Requests.Single().Url);
    }

    [Fact]
    public void ImportWithReport_MapsSupportedSecuritySchemesAndReportsAlternatives()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Secured API" },
              "security": [{ "basicAuth": [] }, { "oauth": [] }],
              "paths": { "/ping": { "get": {} } },
              "components": {
                "securitySchemes": {
                  "basicAuth": { "type": "http", "scheme": "basic" },
                  "oauth": {
                    "type": "oauth2",
                    "flows": {
                      "clientCredentials": {
                        "tokenUrl": "https://auth.example.test/token",
                        "scopes": { "read": "Read access" }
                      }
                    }
                  }
                }
              }
            }
            """;

        var result = _importer.ImportWithReport(spec);

        Assert.Equal(ApiConnectTool.Models.AuthType.Basic, result.Collection.Auth.Type);
        Assert.Equal(ApiConnectTool.Models.AuthType.Inherit, result.Collection.Requests.Single().Auth.Type);
        Assert.Equal(1, result.Report.SecuredRequestCount);
        Assert.Equal(1, result.Report.MappedRequestCount);
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("alternatives"));
    }

    [Fact]
    public void ImportWithReport_MapsApiKeyLocationsAndOAuthClientCredentials()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Secured API" },
              "paths": {
                "/header": { "get": { "security": [{ "key": [] }] } },
                "/query": { "get": { "security": [{ "queryKey": [] }] } },
                "/oauth": { "get": { "security": [{ "oauth": [] }] } }
              },
              "components": {
                "securitySchemes": {
                  "key": { "type": "apiKey", "in": "header", "name": "X-API-Key" },
                  "queryKey": { "type": "apiKey", "in": "query", "name": "api_key" },
                  "oauth": {
                    "type": "oauth2",
                    "flows": {
                      "clientCredentials": {
                        "tokenUrl": "https://auth.example.test/token",
                        "scopes": { "read": "Read access", "write": "Write access" }
                      }
                    }
                  }
                }
              }
            }
            """;

        var result = _importer.ImportWithReport(spec);
        var header = result.Collection.Requests.Single(request => request.Url == "/header");
        var query = result.Collection.Requests.Single(request => request.Url == "/query");
        var oauth = result.Collection.Requests.Single(request => request.Url == "/oauth");

        Assert.Equal(ApiConnectTool.Models.AuthType.ApiKey, header.Auth.Type);
        Assert.Equal("X-API-Key", header.Auth.ApiKeyHeaderName);
        Assert.Equal(ApiConnectTool.Models.AuthType.ApiKey, query.Auth.Type);
        Assert.Equal("api_key", query.Auth.ApiKeyQueryParam);
        Assert.Equal(ApiConnectTool.Models.AuthType.OAuth2, oauth.Auth.Type);
        Assert.Equal(ApiConnectTool.Models.OAuthGrantType.ClientCredentials, oauth.Auth.GrantType);
        Assert.Equal("https://auth.example.test/token", oauth.Auth.TokenUrl);
        Assert.Equal("read write", oauth.Auth.Scope);
        Assert.Equal(3, result.Report.MappedRequestCount);
        Assert.Empty(result.Report.Warnings);
    }

    [Fact]
    public void ImportWithReport_ExplicitEmptyOperationSecurityDisablesRootSecurity()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Secured API" },
              "security": [{ "bearer": [] }],
              "paths": { "/public": { "get": { "security": [] } } },
              "components": { "securitySchemes": { "bearer": { "type": "http", "scheme": "bearer" } } }
            }
            """;

        var result = _importer.ImportWithReport(spec);

        Assert.Equal(ApiConnectTool.Models.AuthType.None, result.Collection.Requests.Single().Auth.Type);
        Assert.Equal(0, result.Report.SecuredRequestCount);
    }

    [Fact]
    public void ImportWithReport_UnsupportedSecurityIsVisibleAndLeavesRequestUnauthenticated()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Secured API" },
              "paths": { "/ping": { "get": { "security": [{ "oidc": [] }] } } },
              "components": { "securitySchemes": { "oidc": { "type": "openIdConnect", "openIdConnectUrl": "https://issuer.example.test" } } }
            }
            """;

        var result = _importer.ImportWithReport(spec);

        Assert.Equal(ApiConnectTool.Models.AuthType.None, result.Collection.Requests.Single().Auth.Type);
        Assert.Equal(1, result.Report.SecuredRequestCount);
        Assert.Equal(0, result.Report.MappedRequestCount);
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("openIdConnect"));
    }

    [Fact]
    public void Import_MalformedJson_ThrowsOpenApiImportException()
    {
        Assert.Throws<OpenApiImportException>(() => _importer.Import("not json"));
    }

    [Fact]
    public void Import_MissingPathsProperty_ThrowsOpenApiImportException()
    {
        Assert.Throws<OpenApiImportException>(() => _importer.Import("""{ "openapi": "3.0.0" }"""));
    }

    [Fact]
    public void Import_OperationTagsCreateFoldersAndAssignRequests()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Tagged API" },
              "paths": {
                "/accounts": {
                  "get": { "tags": ["Accounts"], "summary": "List accounts" },
                  "post": { "tags": ["Accounts"], "summary": "Create account" }
                },
                "/invoices": {
                  "get": { "tags": ["Invoices", "Reporting"], "summary": "List invoices" }
                },
                "/health": {
                  "get": { "summary": "Health check" }
                }
              }
            }
            """;

        var collection = _importer.Import(spec);

        Assert.Equal(["Accounts", "Invoices"], collection.Folders.Select(folder => folder.Name));
        Assert.All(collection.Requests.Where(request => request.Name is "List accounts" or "Create account"),
            request => Assert.Equal(collection.Folders.Single(folder => folder.Name == "Accounts").Id, request.FolderId));
        Assert.Equal(collection.Folders.Single(folder => folder.Name == "Invoices").Id,
            collection.Requests.Single(request => request.Name == "List invoices").FolderId);
        Assert.Null(collection.Requests.Single(request => request.Name == "Health check").FolderId);
        Assert.DoesNotContain(collection.Folders, folder => folder.Name == "Reporting");
    }

    [Fact]
    public void Import_TaggedRequestsUseIndependentSortOrderPerFolder()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Tagged API" },
              "paths": {
                "/a": { "get": { "tags": ["A"] } },
                "/b": { "get": { "tags": ["B"] } },
                "/c": { "get": { "tags": ["A"] } },
                "/d": { "get": { "tags": ["B"] } }
              }
            }
            """;

        var collection = _importer.Import(spec);

        Assert.Equal([0, 1], collection.Requests.Where(request => request.FolderId == collection.Folders.Single(folder => folder.Name == "A").Id).Select(request => request.SortOrder));
        Assert.Equal([0, 1], collection.Requests.Where(request => request.FolderId == collection.Folders.Single(folder => folder.Name == "B").Id).Select(request => request.SortOrder));
    }

    [Fact]
    public void Import_AssignsFreshSortOrderAcrossAllOperations()
    {
        var collection = _importer.Import(FixtureSpec);

        Assert.Equal(collection.Requests.Select(r => r.SortOrder).OrderBy(x => x), collection.Requests.Select(r => r.SortOrder));
        Assert.Equal(collection.Requests.Count, collection.Requests.Select(r => r.SortOrder).Distinct().Count());
    }

    [Fact]
    public void Import_FallbackName_KeepsLiteralTextInAMixedParameterSegment()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Orders API" },
              "paths": { "/orders/{id}-summary": { "get": { } } }
            }
            """;

        Assert.Equal("GET Orders Summary", _importer.Import(spec).Requests.Single().Name);
    }

    [Fact]
    public void Import_FallbackName_DropsPureParameterSegmentEntirely()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Pets API" },
              "paths": { "/pets/{petId}": { "get": { } } }
            }
            """;

        Assert.Equal("GET Pets", _importer.Import(spec).Requests.Single().Name);
    }

    [Fact]
    public void Import_OperationId_TooLongToBeAName_FallsBackToPathName()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Orders API" },
              "paths": {
                "/orders": {
                  "get": { "operationId": "getAllPurchaseOrdersForSupplierWithSpecificDateRangeAndStatusAndPriorityLevelAndWarehouseLocationCode" }
                }
              }
            }
            """;

        Assert.Equal("GET Orders", _importer.Import(spec).Requests.Single().Name);
    }

    [Fact]
    public void Import_OperationId_TooLong_FallsBackToSummaryWhenPresent()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Orders API" },
              "paths": {
                "/orders": {
                  "get": {
                    "operationId": "getAllPurchaseOrdersForSupplierWithSpecificDateRangeAndStatusAndPriorityLevelAndWarehouseLocationCode",
                    "summary": "List orders"
                  }
                }
              }
            }
            """;

        Assert.Equal("List orders", _importer.Import(spec).Requests.Single().Name);
    }
}
