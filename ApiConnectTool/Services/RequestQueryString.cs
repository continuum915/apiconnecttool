using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public static class RequestQueryString
{
    public static (string BaseUrl, IReadOnlyList<KeyValueItem> Rows) Split(string url)
    {
        var fragment = url.IndexOf('#');
        var query = url.IndexOf('?');
        if (query < 0 || (fragment >= 0 && query > fragment)) return (url, []);
        var end = fragment < 0 ? url.Length : fragment;
        var rows = url[(query + 1)..end].Split('&').Select(part =>
        {
            var equals = part.IndexOf('=');
            return new KeyValueItem { Key = equals < 0 ? part : part[..equals],
                Value = equals < 0 ? string.Empty : part[(equals + 1)..],
                UseRawQueryText = true, HasQueryEquals = equals >= 0 };
        }).ToArray();
        return (url[..query] + url[end..], rows);
    }

    public static bool Normalize(RequestItem request)
    {
        var (baseUrl, rows) = Split(request.Url);
        if (rows.Count == 0) return false;
        request.Url = baseUrl;
        for (var i = rows.Count - 1; i >= 0; i--) request.QueryParams.Insert(0, rows[i]);
        return true;
    }

    public static string Compose(string baseUrl, IEnumerable<KeyValueItem> rows)
    {
        var values = rows.Where(IsIncluded).Select(row => Format(row, encode: false)).ToArray();
        if (values.Length == 0) return baseUrl;
        var fragment = baseUrl.IndexOf('#');
        var end = fragment < 0 ? baseUrl.Length : fragment;
        return baseUrl[..end] + "?" + string.Join("&", values) + baseUrl[end..];
    }

    public static Uri AppendToUri(Uri uri, IEnumerable<KeyValueItem> rows)
    {
        var values = rows.Where(IsIncluded).Select(row => Format(row, encode: !row.UseRawQueryText)).ToArray();
        if (values.Length == 0) return uri;
        var builder = new UriBuilder(uri);
        var query = (string.IsNullOrEmpty(builder.Query) ? "" : builder.Query[1..] + "&") + string.Join("&", values);
        builder.Query = query.Length == 0 ? "?" : query;
        return builder.Uri;
    }

    private static bool IsIncluded(KeyValueItem row) => row.IsEnabled && (row.UseRawQueryText || !string.IsNullOrWhiteSpace(row.Key));
    private static string Format(KeyValueItem row, bool encode)
    {
        var key = encode ? Uri.EscapeDataString(row.Key) : row.Key;
        var value = encode ? Uri.EscapeDataString(row.Value) : row.Value;
        return row.HasQueryEquals || row.Value.Length > 0 ? key + "=" + value : key;
    }
}
