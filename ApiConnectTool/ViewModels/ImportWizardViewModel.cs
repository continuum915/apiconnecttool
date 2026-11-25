using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public enum ImportWizardStep { Format, Source, Options, Review, Import }

public sealed record ImportFormatChoice(ImportFormat Format, string Name, string Extensions);

/// <summary>One entry of the wizard's step list with its state.</summary>
public sealed record ImportStepItem(ImportWizardStep Step, string Name, bool IsCurrent, bool IsDone);

/// <summary>One row of the Review step: a folder, request or collection environment and what
/// the import will do with it.</summary>
public sealed partial class ImportReviewItem : ObservableObject
{
    [ObservableProperty] private bool _isIncluded = true;
    [ObservableProperty] private ImportAction _action = ImportAction.New;

    public ImportReviewItem(Guid id, ImportItemKind kind, string name, string? method, int depth, Guid? parentFolderId)
    {
        Id = id; Kind = kind; Name = name; Method = method; Depth = depth; ParentFolderId = parentFolderId;
    }

    public Guid Id { get; }
    public ImportItemKind Kind { get; }
    public string Name { get; }
    public string? Method { get; }
    public int Depth { get; }
    public Guid? ParentFolderId { get; }
    public string KindText => AppLocalization.Get("Str.ImportWizard.Kind." + Kind);
    public string ActionText => AppLocalization.Get("Str.ImportWizard.Action." + Action);
    public double Indent => Depth * 16;
    partial void OnActionChanged(ImportAction value) => OnPropertyChanged(nameof(ActionText));
}

/// <summary>The five-step import wizard: Format, Source, Options, Review, Import. Nothing is
/// written until Start on the Review step; the workspace is then saved once.</summary>
public sealed partial class ImportWizardViewModel : ObservableObject
{
    private readonly CollectionsViewModel _panel;
    private readonly CollectionImport _import;
    private ImportPreview? _preview;

    [ObservableProperty] private ImportWizardStep _step;
    [ObservableProperty] private ImportFormatChoice _selectedFormat;
    [ObservableProperty] private bool _isFromFile = true;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _pastedText = string.Empty;
    [ObservableProperty] private string? _detectionNote;
    [ObservableProperty] private string? _sourceError;
    [ObservableProperty] private string _collectionName = string.Empty;
    [ObservableProperty] private bool _importEnvironments = true;
    [ObservableProperty] private bool _importIntoExisting;
    [ObservableProperty] private RequestCollection? _existingCollection;
    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _finishText;
    [ObservableProperty] private bool _isFinished;
    private string? _fileText;

    public ImportWizardViewModel(CollectionsViewModel panel, ImportFormat? preselected = null, CollectionImport? import = null)
    {
        _panel = panel;
        _import = import ?? new CollectionImport();
        Formats =
        [
            new(ImportFormat.ApiConnectTool, Text("Str.ImportWizard.Format.ApiConnectTool"), Text("Str.ImportWizard.Extensions.ApiConnectTool")),
            new(ImportFormat.Postman, Text("Str.ImportWizard.Format.Postman"), Text("Str.ImportWizard.Extensions.Postman")),
            new(ImportFormat.OpenApi, Text("Str.ImportWizard.Format.OpenApi"), Text("Str.ImportWizard.Extensions.OpenApi")),
            new(ImportFormat.Curl, Text("Str.ImportWizard.Format.Curl"), Text("Str.ImportWizard.Extensions.Curl"))
        ];
        _selectedFormat = Formats.First(choice => choice.Format == (preselected ?? ImportFormat.ApiConnectTool));
        if (preselected is ImportFormat.Curl) _isFromFile = false;
        _step = preselected is null ? ImportWizardStep.Format : ImportWizardStep.Source;
    }

    /// <summary>Raised when the wizard should close (Cancel, Close or Done).</summary>
    public event Action? CloseRequested;

    public IReadOnlyList<ImportFormatChoice> Formats { get; }
    public ObservableCollection<ImportReviewItem> ReviewItems { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public ImportPreview? Preview => _preview;

    /// <summary>The collections an import can merge into.</summary>
    public IReadOnlyList<RequestCollection> ExistingCollections => _panel.Collections;
    public bool HasExistingCollections => _panel.Collections.Count > 0;
    public bool IsNewCollection => !ImportIntoExisting;

    public IReadOnlyList<ImportStepItem> Steps => Enum.GetValues<ImportWizardStep>()
        .Select(step => new ImportStepItem(step, Text("Str.ImportWizard.Step." + step), step == Step, step < Step)).ToArray();
    public string StepTitle => Text("Str.ImportWizard.Step." + Step);
    public string StepDescription => Text("Str.ImportWizard.StepDescription." + Step);
    public string StepCounterText => string.Format(CultureInfo.CurrentCulture, Text("Str.ImportWizard.StepCounter"), (int)Step + 1, Steps.Count);
    public bool IsFormatStep => Step == ImportWizardStep.Format;
    public bool IsSourceStep => Step == ImportWizardStep.Source;
    public bool IsOptionsStep => Step == ImportWizardStep.Options;
    public bool IsReviewStep => Step == ImportWizardStep.Review;
    public bool IsImportStep => Step == ImportWizardStep.Import;
    public bool IsPaste => !IsFromFile;
    public string NextText => Text(Step switch
    {
        ImportWizardStep.Review => "Str.ImportWizard.Start",
        ImportWizardStep.Import => "Str.ImportWizard.Done",
        _ => "Str.ImportWizard.Next"
    });
    public string CancelText => Text(Step == ImportWizardStep.Import ? "Str.ImportWizard.Close" : "Str.Dialog.Cancel");
    public bool CanImportEnvironments => SelectedFormat.Format == ImportFormat.ApiConnectTool && _preview?.EnvironmentCount > 0;
    public int IncludedCount => ReviewItems.Count(item => item.Action != ImportAction.Skip);
    public string ReviewSummary => string.Format(CultureInfo.CurrentCulture, Text("Str.ImportWizard.ReviewSummary"),
        ReviewItems.Count(item => item.Action == ImportAction.New), ReviewItems.Count(item => item.Action == ImportAction.Merge),
        ReviewItems.Count(item => item.Action == ImportAction.Overwrite), ReviewItems.Count(item => item.Action == ImportAction.Skip));

    partial void OnStepChanged(ImportWizardStep value)
    {
        foreach (var name in new[] { nameof(Steps), nameof(StepTitle), nameof(StepDescription), nameof(StepCounterText), nameof(IsFormatStep),
                     nameof(IsSourceStep), nameof(IsOptionsStep), nameof(IsReviewStep), nameof(IsImportStep), nameof(NextText), nameof(CancelText) })
            OnPropertyChanged(name);
        RefreshCommands();
    }

    partial void OnSelectedFormatChanged(ImportFormatChoice value) => ResetSource();
    partial void OnIsFromFileChanged(bool value) { OnPropertyChanged(nameof(IsPaste)); ResetSource(); }
    partial void OnPastedTextChanged(string value) { if (IsPaste) ResetSource(); }
    partial void OnCollectionNameChanged(string value) => RefreshCommands();
    partial void OnImportIntoExistingChanged(bool value)
    {
        if (value && ExistingCollection is null) ExistingCollection = _panel.Collections.FirstOrDefault();
        OnPropertyChanged(nameof(IsNewCollection));
        RefreshCommands();
    }
    partial void OnExistingCollectionChanged(RequestCollection? value) => RefreshCommands();
    partial void OnOverwriteExistingChanged(bool value) => UpdateActions();

    /// <summary>Sets the chosen file's path and content (the window reads the file).</summary>
    public void LoadFile(string path, string text)
    {
        FilePath = path;
        _fileText = text;
        Detect();
    }

    private void ResetSource()
    {
        _preview = null;
        DetectionNote = null;
        SourceError = null;
        if (IsFromFile && _fileText is not null && SelectedFormat is not null) Detect();
        else if (IsPaste && !string.IsNullOrWhiteSpace(PastedText)) Detect();
        RefreshCommands();
    }

    /// <summary>Reads the source with the chosen format and shows what it holds, or the
    /// importer's error. Writes nothing.</summary>
    public void Detect()
    {
        var text = IsFromFile ? _fileText : PastedText;
        _preview = null;
        DetectionNote = null;
        SourceError = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshCommands();
            return;
        }
        try
        {
            _preview = _import.Read(SelectedFormat.Format, text);
            DetectionNote = _preview.Notes is null ? _preview.DetectionSummary : _preview.DetectionSummary + " " + _preview.Notes;
            CollectionName = DefaultName();
        }
        catch (Exception error) when (error is InvalidDataException or PostmanImportException or OpenApiImportException or CurlImportException)
        {
            SourceError = error.Message;
        }
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanImportEnvironments));
        RefreshCommands();
    }

    private string DefaultName()
    {
        if (!string.IsNullOrWhiteSpace(_preview?.Collection.Name)) return _preview!.Collection.Name;
        return FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : Text("Str.ImportWizard.CurlCollectionName");
    }

    private bool CanGoBack() => Step is > ImportWizardStep.Format and < ImportWizardStep.Import;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoNext() => Step switch
    {
        ImportWizardStep.Source => _preview is not null,
        ImportWizardStep.Options => ImportIntoExisting ? ExistingCollection is not null : !string.IsNullOrWhiteSpace(CollectionName),
        ImportWizardStep.Review => IncludedCount > 0,
        _ => true
    };

    /// <summary>Next; Start on the Review step; Done on the Import step.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async System.Threading.Tasks.Task NextAsync()
    {
        switch (Step)
        {
            case ImportWizardStep.Options:
                BuildReview();
                Step = ImportWizardStep.Review;
                break;
            case ImportWizardStep.Review:
                Step = ImportWizardStep.Import;
                if (ImportIntoExisting) await MergeAsync();
                else Start();
                break;
            case ImportWizardStep.Import:
                CloseRequested?.Invoke();
                break;
            default:
                Step++;
                break;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private void BuildReview()
    {
        foreach (var item in ReviewItems) item.PropertyChanged -= OnReviewItemChanged;
        ReviewItems.Clear();
        if (_preview is null) return;
        var collection = _preview.Collection;
        void AddFolder(CollectionFolder folder, int depth)
        {
            Add(new(folder.Id, ImportItemKind.Folder, folder.Name, null, depth, folder.ParentFolderId));
            foreach (var request in collection.Requests.Where(request => request.FolderId == folder.Id).OrderBy(request => request.SortOrder))
                Add(new(request.Id, ImportItemKind.Request, request.Name, request.Method.Method, depth + 1, folder.Id));
            foreach (var child in collection.Folders.Where(child => child.ParentFolderId == folder.Id).OrderBy(child => child.SortOrder))
                AddFolder(child, depth + 1);
        }
        foreach (var folder in collection.Folders.Where(folder => folder.ParentFolderId is null).OrderBy(folder => folder.SortOrder))
            AddFolder(folder, 0);
        foreach (var request in collection.Requests.Where(request => request.FolderId is null).OrderBy(request => request.SortOrder))
            Add(new(request.Id, ImportItemKind.Request, request.Name, request.Method.Method, 0, null));
        if (ImportIntoExisting)
            foreach (var variable in collection.Variables)
                Add(new(variable.Id, ImportItemKind.Variable, variable.Key, null, 0, null));
        if (CanImportEnvironments && ImportEnvironments)
            foreach (var environment in collection.Environments)
                Add(new(environment.Id, ImportItemKind.Environment, environment.Name, null, 0, null));
        UpdateActions();

        void Add(ImportReviewItem item)
        {
            item.PropertyChanged += OnReviewItemChanged;
            ReviewItems.Add(item);
        }
    }

    private void OnReviewItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportReviewItem.IsIncluded)) UpdateActions();
    }

    /// <summary>Included rows are New, or for an existing collection New, Merge, Skip or
    /// Overwrite by the merge rules; unticked rows, and rows inside an unticked folder, Skip.</summary>
    private void UpdateActions()
    {
        if (_preview is null) return;
        var included = IncludedIds();
        var plan = ImportIntoExisting && ExistingCollection is { } target
            ? CollectionMerge.Plan(_preview.Collection, target, OverwriteExisting)
            : null;
        foreach (var item in ReviewItems)
        {
            var parentIncluded = item.ParentFolderId is not { } parent || CollectionImport.IsIncluded(parent, _preview.Collection, included);
            item.Action = !item.IsIncluded || !parentIncluded ? ImportAction.Skip
                : plan is not null && plan.TryGetValue(item.Id, out var planned) ? planned : ImportAction.New;
        }
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(ReviewSummary));
        RefreshCommands();
    }

    private HashSet<Guid> IncludedIds() => ReviewItems.Where(item => item.IsIncluded).Select(item => item.Id).ToHashSet();

    private void Start()
    {
        if (_preview is null) return;
        var importEnvironments = CanImportEnvironments && ImportEnvironments;
        var collection = CollectionImport.Commit(_preview, CollectionName.Trim(), importEnvironments, IncludedIds());
        if (importEnvironments)
            collection.Environments = new(collection.Environments.Where(environment => IncludedIds().Contains(environment.Id)));
        _panel.AddImportedCollection(collection);

        var redactor = new SecretRedactor();
        var secrets = SecretsOf(collection);
        string Redact(string text) => redactor.Redact(text, secrets);
        foreach (var folder in collection.Folders) Log.Add(Redact(Format("Str.ImportWizard.Log.Folder", folder.Name)));
        foreach (var request in collection.Requests) Log.Add(Redact(Format("Str.ImportWizard.Log.Request", $"{request.Method.Method} {request.Name}")));
        foreach (var environment in collection.Environments) Log.Add(Redact(Format("Str.ImportWizard.Log.Environment", environment.Name)));
        Progress = 1;
        FinishText = Redact(string.Format(CultureInfo.CurrentCulture, Text("Str.ImportWizard.Finished"), collection.Folders.Count,
            collection.Requests.Count, collection.Environments.Count, collection.Name));
        IsFinished = true;
        _panel.ImportExportMessage = FinishText;
        RefreshCommands();
    }

    private async System.Threading.Tasks.Task MergeAsync()
    {
        if (_preview is null || ExistingCollection is not { } target) return;
        var importEnvironments = CanImportEnvironments && ImportEnvironments;
        var result = await _panel.MergeImportAsync(_preview.Collection, target, IncludedIds(), OverwriteExisting, importEnvironments);
        var redactor = new SecretRedactor();
        var secrets = SecretsOf(_preview.Collection);
        foreach (var line in result.Log) Log.Add(redactor.Redact(line, secrets));
        Progress = 1;
        FinishText = redactor.Redact(string.Format(CultureInfo.CurrentCulture, Text("Str.ImportWizard.MergeFinished"),
            result.Created, result.Merged, result.Overwritten, result.Skipped, target.Name), secrets);
        IsFinished = true;
        _panel.ImportExportMessage = FinishText;
        RefreshCommands();
    }

    /// <summary>Known secret values in the imported collection, so the log never shows them.</summary>
    private static string[] SecretsOf(RequestCollection collection) =>
        collection.Variables.Concat(collection.Environments.SelectMany(environment => environment.Variables))
            .Where(variable => variable.IsSecret)
            .SelectMany(variable => new[] { variable.InitialValue.Value, variable.CurrentValue.Value })
            .Concat(collection.Requests.SelectMany(request => new[] { request.Auth.Token.Value, request.Auth.Password.Value, request.Auth.ClientSecret.Value }))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();

    private void RefreshCommands()
    {
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private static string Text(string key) => AppLocalization.Get(key);
    private static string Format(string key, string value) => string.Format(CultureInfo.CurrentCulture, Text(key), value);
}
