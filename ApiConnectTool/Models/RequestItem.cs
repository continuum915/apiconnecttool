using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiConnectTool.Models;

public partial class RequestItem : ObservableObject, IRenamable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty, JsonIgnore] private bool _isEditing;
    [JsonIgnore] public string? RenameOriginalName { get; set; }
    public HttpMethod Method { get; set; } = HttpMethod.Get;
    public string Url { get; set; } = string.Empty;
    public ObservableCollection<KeyValueItem> QueryParams { get; set; } = new();
    public ObservableCollection<KeyValueItem> Headers { get; set; } = new();
    public ObservableCollection<Assertion> Assertions { get; set; } = new();
    public string? PreRequestScript { get; set; }
    public string? TestScript { get; set; }
    public RequestBodyMode BodyMode { get; set; } = RequestBodyMode.Raw;
    public string BodyContentType { get; set; } = "application/json";
    public string Body { get; set; } = string.Empty;
    public ObservableCollection<KeyValueItem> FormData { get; set; } = new();
    public string? BodyFilePath { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool FollowRedirects { get; set; } = true;
    public string ProxyUrl { get; set; } = string.Empty;
    public bool IgnoreCertificateErrors { get; set; }

    // Retained only to deserialize pre-Phase-1 workspaces. New code uses Auth.
    public EncryptedString AuthToken { get; set; } = new(string.Empty);

    public RequestAuth Auth { get; set; } = new() { Type = AuthType.Inherit };

    // null = collection root. List position alone can't survive drag-drop reorder across a
    // folder tree, so an explicit order field is needed - the same reasoning as
    // stable IDs over display-name identity.
    public Guid? FolderId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Deep copy with a fresh <see cref="Id"/> - used by duplicate/import flows. Caller sets
    /// <see cref="Name"/>, <see cref="FolderId"/>, and <see cref="SortOrder"/> to their own
    /// disambiguated/positioned values afterward.
    /// </summary>
    public RequestItem Clone(bool newAssertionIds = false) => Copy(false, newAssertionIds);

    /// <summary>Detached editing snapshot of the same request, retaining request and auth identity.</summary>
    public RequestItem CloneForEditing() => Copy(true, false);

    private RequestItem Copy(bool preserveIdentity, bool newAssertionIds) => new()
    {
        Id = preserveIdentity ? Id : Guid.NewGuid(),
        Name = Name,
        Assertions = new ObservableCollection<Assertion>(Assertions.Select(assertion => assertion.Clone(newAssertionIds))),
        PreRequestScript = PreRequestScript,
        TestScript = TestScript,
        Method = Method,
        Url = Url,
        QueryParams = new ObservableCollection<KeyValueItem>(QueryParams.Select(CloneRow)),
        Headers = new ObservableCollection<KeyValueItem>(Headers.Select(CloneRow)),
        BodyMode = BodyMode,
        BodyContentType = BodyContentType,
        Body = Body,
        FormData = new ObservableCollection<KeyValueItem>(FormData.Select(CloneRow)),
        BodyFilePath = BodyFilePath,
        TimeoutSeconds = TimeoutSeconds,
        FollowRedirects = FollowRedirects,
        ProxyUrl = ProxyUrl,
        IgnoreCertificateErrors = IgnoreCertificateErrors,
        Auth = Auth.Clone(preserveCacheKey: preserveIdentity),
        FolderId = FolderId,
        SortOrder = SortOrder
    };

    private static KeyValueItem CloneRow(KeyValueItem row) => new()
    {
        UseRawQueryText = row.UseRawQueryText,
        HasQueryEquals = row.HasQueryEquals,
        FieldType = row.FieldType,
        FilePath = row.FilePath,
        IsEnabled = row.IsEnabled,
        Key = row.Key,
        Value = row.Value,
        Description = row.Description
    };
}
