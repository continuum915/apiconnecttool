using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;

namespace ApiConnectTool.Services;

public sealed class PostmanCollectionConverter : IPostmanCollectionConverter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Export(RequestCollection collection, bool includeSecrets)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var dto = new PostmanCollectionDto
        {
            Info = new PostmanInfoDto { Name = collection.Name },
            Auth = PostmanAuthMapper.Export(collection.Auth, includeSecrets),
            Variable = collection.Variables.Select(variable => new PostmanVariableDto { Key = variable.Key,
                Value = variable.IsSecret && !includeSecrets ? string.Empty :
                    !variable.HasCurrentValue && string.IsNullOrEmpty(variable.CurrentValue.Value) ? variable.InitialValue.Value : variable.CurrentValue.Value,
                Disabled = !variable.IsEnabled, Type = variable.IsSecret ? "secret" : "string" }).ToList(),
            Event = [],
            Item = ExportItems(collection, null, includeSecrets)
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    public RequestCollection Import(string json)
        => ImportWithReport(json).Collection;

    public PostmanImportResult ImportWithReport(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        PostmanCollectionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PostmanCollectionDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PostmanImportException("The file is not a valid Postman collection.", ex);
        }

        if (dto?.Item is null)
        {
            throw new PostmanImportException("The file is not a valid Postman collection.");
        }

        var collection = new RequestCollection
        {
            Name = string.IsNullOrWhiteSpace(dto.Info?.Name) ? "Imported Collection" : dto.Info.Name
        };
        var report = new PostmanImportReport();
        collection.Auth = PostmanAuthMapper.Import(dto.Auth, collection.Name, report, collection: true);
        foreach (var variable in dto.Variable ?? []) collection.Variables.Add(new() { Key = variable.Key,
            InitialValue = variable.Value, IsEnabled = !variable.Disabled, IsSecret = variable.Type == "secret" });
        ReportEvents(dto.Event, collection.Name, report);
        ImportItems(dto.Item, collection, parentFolderId: null, report);
        return new(collection, report);
    }

    // ── Import ──────────────────────────────────────────────────────────────

    private static void ImportItems(List<PostmanItemDto> items, RequestCollection collection, Guid? parentFolderId, PostmanImportReport report)
    {
        var folderIndex = 0;
        var requestIndex = 0;
        foreach (var item in items)
        {
            ReportEvents(item.Event, item.Name, report);
            if (item.Item is not null)
            {
                var folder = new CollectionFolder { Name = item.Name, ParentFolderId = parentFolderId, SortOrder = folderIndex++ };
                folder.Auth = PostmanAuthMapper.Import(item.Auth, item.Name, report);
                collection.Folders.Add(folder);
                ImportItems(item.Item, collection, folder.Id, report);
            }
            else if (item.Request is not null)
            {
                var request = ImportRequest(item.Name, item.Request, report);
                request.FolderId = parentFolderId;
                request.SortOrder = requestIndex++;
                collection.Requests.Add(request);
            }
            else report.AddWarning(string.Format(Localization.DefaultStrings.Get("Str.Postman.SkippedItem"), item.Name));
            // Neither "item" nor "request" present - a degenerate entry a hand-edited or
            // partially-exported file might contain. Best-effort: skip it rather than failing the
            // whole import over one entry, matching 4g4 OpenAPI's stated best-effort posture.
        }
    }

    private static RequestItem ImportRequest(string name, PostmanRequestDto dto, PostmanImportReport report)
    {
        var request = new RequestItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Imported request" : name,
            Method = new HttpMethod(string.IsNullOrWhiteSpace(dto.Method) ? "GET" : dto.Method.ToUpperInvariant()),
        };
        request.Url = PostmanPathVariables.Inline(dto.Url?.Raw ?? string.Empty, dto.Url?.Variable, request.Name, report);

        foreach (var header in dto.Header)
        {
            request.Headers.Add(new KeyValueItem { IsEnabled = !header.Disabled, Key = header.Key, Value = header.Value });
        }

        ImportBody(request, dto.Body);
        request.Auth = PostmanAuthMapper.Import(dto.Auth, request.Name, report);
        if (dto.Body?.Mode is { } mode && mode is not ("raw" or "file" or "formdata" or "urlencoded"))
            report.AddWarning(string.Format(Localization.DefaultStrings.Get("Str.Postman.BodyMode"), mode, request.Name));

        return request;
    }

    private static void ImportBody(RequestItem request, PostmanBodyDto? body)
    {
        switch (body?.Mode)
        {
            case "raw":
                request.BodyMode = RequestBodyMode.Raw;
                request.Body = body.Raw ?? string.Empty;
                request.BodyContentType = body.Options?.Raw?.Language switch
                {
                    "json" => "application/json",
                    "xml" => "application/xml",
                    "html" => "text/html",
                    _ => "text/plain"
                };
                break;
            case "urlencoded":
                request.BodyMode = RequestBodyMode.UrlEncoded;
                foreach (var pair in body.Urlencoded ?? [])
                {
                    request.FormData.Add(new KeyValueItem { IsEnabled = !pair.Disabled, Key = pair.Key, Value = pair.Value });
                }
                break;
            case "formdata":
                request.BodyMode = RequestBodyMode.FormData;
                foreach (var pair in body.Formdata ?? [])
                {
                    request.FormData.Add(new KeyValueItem { IsEnabled = !pair.Disabled, Key = pair.Key, Value = pair.Value,
                        FieldType = pair.Type == "file" ? FormDataFieldType.File : FormDataFieldType.Text, FilePath = pair.Src ?? string.Empty });
                }
                break;
            case "file":
                request.BodyMode = RequestBodyMode.Binary;
                request.BodyFilePath = body.File?.Src;
                break;
            default:
                request.BodyMode = RequestBodyMode.None;
                break;
        }
    }

    // ── Export ──────────────────────────────────────────────────────────────

    private static void ReportEvents(List<PostmanEventDto>? events, string name, PostmanImportReport report)
    {
        foreach (var entry in events ?? [])
            report.AddWarning(string.Format(Localization.DefaultStrings.Get("Str.Postman.DroppedScript"), entry.Listen, name));
    }

    private static List<PostmanEventDto>? ExportEvents(RequestItem request)
    {
        var events = new List<PostmanEventDto>();
        foreach (var (listen, source) in new[] { ("prerequest", request.PreRequestScript), ("test", request.TestScript) })
            if (!string.IsNullOrWhiteSpace(source)) events.Add(new() { Listen = listen,
                Script = JsonSerializer.SerializeToElement(new { type = "text/javascript", exec = source.Split('\n') }) });
        return events.Count == 0 ? null : events;
    }

    private static List<PostmanItemDto> ExportItems(RequestCollection collection, Guid? parentFolderId, bool includeSecrets)
    {
        var items = new List<PostmanItemDto>();

        // ApiConnectTool tracks folder order and request order as independent per-parent sequences (4b);
        // Postman's single ordered item[] array has no equivalent split, so folders are listed
        // before requests at each level rather than claiming an interleaved order ApiConnectTool doesn't
        // actually track.
        foreach (var folder in collection.Folders.Where(f => f.ParentFolderId == parentFolderId).OrderBy(f => f.SortOrder))
        {
            items.Add(new PostmanItemDto { Name = folder.Name, Auth = PostmanAuthMapper.Export(folder.Auth, includeSecrets), Item = ExportItems(collection, folder.Id, includeSecrets) });
        }

        foreach (var request in collection.Requests.Where(r => r.FolderId == parentFolderId).OrderBy(r => r.SortOrder))
        {
            items.Add(new PostmanItemDto { Name = request.Name, Request = ExportRequest(request, includeSecrets), Event = ExportEvents(request) });
        }

        return items;
    }

    private static PostmanRequestDto ExportRequest(RequestItem request, bool includeSecrets)
    {
        var dto = new PostmanRequestDto
        {
            Method = request.Method.Method,
            Header = request.Headers.Select(h => new PostmanHeaderDto { Key = h.Key, Value = h.Value, Disabled = !h.IsEnabled }).ToList(),
            Url = new PostmanUrlDto
            {
                Raw = BuildUrlWithQuery(request),
                Query = request.QueryParams.Select(q => new PostmanQueryParamDto { Key = q.Key, Value = q.Value, Disabled = !q.IsEnabled }).ToList()
            }
        };

        ExportBody(request, dto);
        dto.Auth = PostmanAuthMapper.Export(request.Auth, includeSecrets);

        return dto;
    }

    /// <summary>
    /// Folds enabled QueryParams into the URL, same as CurlGenerator's BuildUrl (4f) - including
    /// the same limitation: a {{templated}} URL isn't a parseable absolute URI, so it's emitted
    /// as-is without merging in query params (which real curl/Postman commands rarely need since
    /// the base host is usually the only templated part, with query params on the literal path).
    /// </summary>
    private static string BuildUrlWithQuery(RequestItem request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return request.Url;
        }

        return RequestQueryString.AppendToUri(uri, request.QueryParams).AbsoluteUri;
    }

    private static void ExportBody(RequestItem request, PostmanRequestDto dto)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.BodyMode == RequestBodyMode.None)
        {
            return;
        }

        dto.Body = request.BodyMode switch
        {
            RequestBodyMode.Raw => new PostmanBodyDto
            {
                Mode = "raw",
                Raw = request.Body,
                Options = new PostmanBodyOptionsDto { Raw = new PostmanRawOptionsDto { Language = GuessLanguage(request.BodyContentType) } }
            },
            RequestBodyMode.UrlEncoded => new PostmanBodyDto
            {
                Mode = "urlencoded",
                Urlencoded = request.FormData.Where(f => !f.IsFile).Select(f => new PostmanFormParamDto { Key = f.Key, Value = f.Value, Disabled = !f.IsEnabled }).ToList()
            },
            RequestBodyMode.FormData => new PostmanBodyDto
            {
                Mode = "formdata",
                Formdata = request.FormData.Select(f => new PostmanFormParamDto { Key = f.Key, Value = f.IsFile ? string.Empty : f.Value,
                    Disabled = !f.IsEnabled, Type = f.IsFile ? "file" : "text", Src = f.IsFile ? f.FilePath : null }).ToList()
            },
            RequestBodyMode.Binary => new PostmanBodyDto { Mode = "file", File = new PostmanFileDto { Src = request.BodyFilePath } },
            _ => null
        };
    }

    private static string GuessLanguage(string contentType) =>
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ? "json" :
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ? "xml" :
        contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? "html" :
        "text";

}
