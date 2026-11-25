using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

internal sealed class RequestQueryEditor
{
    private readonly ObservableCollection<KeyValueItem> _rows;
    private readonly Action<string> _updateUrl;
    private readonly Action _markDirty;
    private readonly HashSet<KeyValueItem> _observed = new();
    private string _baseUrl = string.Empty;
    private bool _synchronizing;
    public string BaseUrl => _baseUrl;

    public RequestQueryEditor(ObservableCollection<KeyValueItem> rows, Action<string> updateUrl, Action markDirty)
    {
        _rows = rows; _updateUrl = updateUrl; _markDirty = markDirty;
        rows.CollectionChanged += (_, _) =>
        {
            foreach (var row in _observed.Where(row => !rows.Contains(row)).ToArray())
            { row.PropertyChanged -= RowChanged; _observed.Remove(row); }
            foreach (var row in rows) if (_observed.Add(row)) row.PropertyChanged += RowChanged;
            UpdateFromRows();
        };
    }

    public void SetUrl(string url)
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            var (baseUrl, rows) = RequestQueryString.Split(url);
            var previousUrl = RequestQueryString.Compose(_baseUrl, _rows);
            var previousRows = RequestQueryString.Split(previousUrl).Rows;
            var sameQuery = rows.Count == previousRows.Count && rows.Zip(previousRows).All(pair =>
                pair.First.Key == pair.Second.Key && pair.First.Value == pair.Second.Value && pair.First.HasQueryEquals == pair.Second.HasQueryEquals);
            // Editing only the base must not reinterpret legacy grid values as raw URL text.
            // A '#' inside a grid value is also distinct from the base URL's real fragment.
            if (sameQuery && Fragment(url) == Fragment(previousUrl))
            {
                var fragment = baseUrl.IndexOf('#');
                baseUrl = baseUrl[..(fragment < 0 ? baseUrl.Length : fragment)] + Fragment(_baseUrl);
            }
            _baseUrl = baseUrl;
            if (!sameQuery)
            {
                var disabled = _rows.Where(row => !row.IsEnabled).ToArray();
                _rows.Clear();
                foreach (var row in rows) _rows.Add(row);
                foreach (var row in disabled) _rows.Add(row);
            }
            EnsureEmptyRow();
        }
        finally { _synchronizing = false; }
        _markDirty();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs args) => UpdateFromRows();
    private static string Fragment(string url) => url.IndexOf('#') is var index && index >= 0 ? url[index..] : string.Empty;
    private void UpdateFromRows()
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try { EnsureEmptyRow(); _updateUrl(RequestQueryString.Compose(_baseUrl, _rows)); }
        finally { _synchronizing = false; }
        _markDirty();
    }

    private void EnsureEmptyRow()
    {
        var empty = _rows.FirstOrDefault(row => !row.UseRawQueryText && string.IsNullOrWhiteSpace(row.Key) && string.IsNullOrWhiteSpace(row.Value) && string.IsNullOrWhiteSpace(row.Description));
        if (empty is null) _rows.Add(new KeyValueItem());
        else if (_rows.IndexOf(empty) != _rows.Count - 1) _rows.Move(_rows.IndexOf(empty), _rows.Count - 1);
    }
}
