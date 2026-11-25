using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

internal static class MultipartFileContent
{
    public static void Validate(RequestItem request)
    {
        if (request.BodyMode != RequestBodyMode.FormData || request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) return;
        foreach (var row in request.FormData.Where(row => row.IsEnabled && row.IsFile && !string.IsNullOrWhiteSpace(row.Key)))
        {
            using var stream = OpenFile(row);
        }
    }

    public static HttpContent Create(IEnumerable<KeyValueItem> rows)
    {
        var content = new MultipartFormDataContent();
        try
        {
            foreach (var row in rows.Where(row => row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key)))
            {
                if (!row.IsFile) content.Add(new StringContent(row.Value ?? string.Empty, Encoding.UTF8), row.Key);
                else
                {
                    var part = new StreamContent(OpenFile(row));
                    try
                    {
                        part.Headers.ContentType = new MediaTypeHeaderValue(ContentType(row.FilePath));
                        content.Add(part, row.Key, Path.GetFileName(row.FilePath));
                    }
                    catch { part.Dispose(); throw; }
                }
            }
            return content;
        }
        catch { content.Dispose(); throw; }
    }

    private static FileStream OpenFile(KeyValueItem row)
    {
        try { return File.OpenRead(row.FilePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(string.Format(DefaultStrings.Get("Str.FormData.FileError"), row.Key), error);
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json", ".xml" => "application/xml", ".txt" => "text/plain",
        ".csv" => "text/csv", ".html" or ".htm" => "text/html", ".pdf" => "application/pdf",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
        ".svg" => "image/svg+xml", ".webp" => "image/webp", ".zip" => "application/zip",
        _ => "application/octet-stream"
    };
}
