using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Models
{
    public partial class KeyValueItem : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private string _key = string.Empty;
        [ObservableProperty] private string _value = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        // URL-derived query rows retain their literal spelling; legacy grid rows are encoded at send.
        [ObservableProperty] private bool _useRawQueryText;
        [ObservableProperty] private bool _hasQueryEquals = true;
        [ObservableProperty] private FormDataFieldType _fieldType;
        [ObservableProperty] private string _filePath = string.Empty;
        [JsonIgnore] public bool IsFile => FieldType == FormDataFieldType.File;
        [JsonIgnore] public int SelectedFieldTypeIndex { get => (int)FieldType; set => FieldType = (FormDataFieldType)value; }
        partial void OnFieldTypeChanged(FormDataFieldType value)
        {
            OnPropertyChanged(nameof(IsFile));
            OnPropertyChanged(nameof(SelectedFieldTypeIndex));
        }
    }
}
