using System.Collections.ObjectModel;
using System.Globalization;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

public enum InspectorFieldKind
{
    Text,
    Number,
    Boolean,
    Enum,
}

public sealed record InspectorOption(string Value, string Label);

public sealed class InspectorFieldViewModel : ObservableObject
{
    private string _value = string.Empty;
    private bool _booleanValue;
    private string? _selectedOption;
    private bool _isEnabled = true;
    private bool _isValid = true;

    internal InspectorFieldViewModel(string key, string label, InspectorFieldKind kind)
    {
        Key = key;
        Label = label;
        Kind = kind;
    }

    public string Key { get; }

    public string Label { get; }

    public InspectorFieldKind Kind { get; }

    public bool IsTextLike => Kind is InspectorFieldKind.Text or InspectorFieldKind.Number;

    public bool IsBoolean => Kind == InspectorFieldKind.Boolean;

    public bool IsEnum => Kind == InspectorFieldKind.Enum;

    public double Minimum { get; init; } = double.MinValue;
    public double Maximum { get; init; } = double.MaxValue;
    public double Step { get; init; } = 1d;
    public string Unit { get; init; } = string.Empty;

    public ObservableCollection<InspectorOption> Options { get; } = [];

    public bool IsMultiline { get; init; }

    public string? HelpText { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public string ValidationMessage => IsValid ? string.Empty : "请输入有效数字";

    public double NumericValue
    {
        get => double.TryParse(_value, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) && double.IsFinite(n) ? n : 0;
        set { if (Kind == InspectorFieldKind.Number) Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                IsValid = Kind != InspectorFieldKind.Number || (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && double.IsFinite(number));
                OnPropertyChanged(nameof(NumericValue));
                OnPropertyChanged(nameof(ValidationMessage));
                Changed?.Invoke(this);
            }
        }
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set
        {
            if (SetProperty(ref _booleanValue, value))
                Changed?.Invoke(this);
        }
    }

    public string? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (SetProperty(ref _selectedOption, value))
                Changed?.Invoke(this);
        }
    }

    internal event Action<InspectorFieldViewModel>? Changed;

    internal void SetText(string value, bool notify = false)
    {
        if (notify)
        {
            Value = value;
            return;
        }

        _value = value;
        OnPropertyChanged(nameof(Value));
    }

    internal void SetBoolean(bool value, bool notify = false)
    {
        if (notify)
        {
            BooleanValue = value;
            return;
        }

        _booleanValue = value;
        OnPropertyChanged(nameof(BooleanValue));
    }

    internal void SetOptions(IEnumerable<InspectorOption> options, string? selectedOption)
    {
        Options.Clear();
        foreach (InspectorOption option in options)
            Options.Add(option);

        _selectedOption = selectedOption;
        OnPropertyChanged(nameof(SelectedOption));
    }
}

public sealed class InspectorSectionViewModel
{
    private readonly Action<string, bool>? _saveState;
    private bool _isExpanded;
    internal InspectorSectionViewModel(string title, string schemaKey, Func<string, bool?>? loadState, Action<string, bool>? saveState)
    {
        Title = title;
        SchemaKey = schemaKey;
        _saveState = saveState;
        _isExpanded = loadState?.Invoke(schemaKey) ?? (!title.Contains("高级", StringComparison.OrdinalIgnoreCase)
            && !title.Contains("调试", StringComparison.OrdinalIgnoreCase)
            && !title.Contains("advanced", StringComparison.OrdinalIgnoreCase)
            && !title.Contains("debug", StringComparison.OrdinalIgnoreCase));
    }

    public string Title { get; }
    public string SchemaKey { get; }
    public bool IsExpanded { get => _isExpanded; set { if (_isExpanded == value) return; _isExpanded = value; _saveState?.Invoke(SchemaKey, value); OnPropertyChanged(); } }
    public string DisplayTitle => Title;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name)); }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InspectorFieldViewModel> Fields { get; } = [];
}

public sealed class InspectorViewModel
{
    public ObservableCollection<InspectorSectionViewModel> Sections { get; } = [];

    public NodeBaseViewModel? Node { get; private set; }
    private string _schemaKey = "default";
    private Func<string, bool?>? _loadSectionState;
    private Action<string, bool>? _saveSectionState;
    private Func<NodeBaseViewModel, string>? _schemaKeyResolver;

    public bool IsStructured { get; private set; }

    internal void ConfigureSectionStateStore(Func<string, bool?>? loadState, Action<string, bool>? saveState)
    {
        _loadSectionState = loadState;
        _saveSectionState = saveState;
    }

    internal void ConfigureSchemaKeyResolver(Func<NodeBaseViewModel, string>? resolver) => _schemaKeyResolver = resolver;

    internal event Action<InspectorFieldViewModel>? FieldChanged;

    internal void Reset(NodeBaseViewModel? node)
    {
        foreach (InspectorSectionViewModel section in Sections)
        {
            foreach (InspectorFieldViewModel field in section.Fields)
                field.Changed -= OnFieldChanged;
        }

        Sections.Clear();
        Node = node;
        _schemaKey = node is null ? "default" : _schemaKeyResolver?.Invoke(node) ?? node.GetType().FullName ?? "default";
        IsStructured = false;
    }

    internal InspectorSectionViewModel AddSection(string title)
    {
        var key = $"{_schemaKey}:{title}";
        var section = new InspectorSectionViewModel(title, key, _loadSectionState, _saveSectionState);
        Sections.Add(section);
        return section;
    }

    internal InspectorFieldViewModel AddField(
        InspectorSectionViewModel section,
        string key,
        string label,
        InspectorFieldKind kind,
        string value = "",
        bool booleanValue = false,
        bool isEnabled = true,
        bool multiline = false,
        string? helpText = null)
    {
        var field = new InspectorFieldViewModel(key, label, kind)
        {
            IsEnabled = isEnabled,
            IsMultiline = multiline,
            HelpText = helpText,
        };
        field.SetText(value);
        field.SetBoolean(booleanValue);
        field.Changed += OnFieldChanged;
        section.Fields.Add(field);
        IsStructured = true;
        return field;
    }

    internal InspectorFieldViewModel AddEnumField(
        InspectorSectionViewModel section,
        string key,
        string label,
        IEnumerable<InspectorOption> options,
        string? selectedOption,
        bool isEnabled = true,
        string? helpText = null)
    {
        InspectorFieldViewModel field = AddField(
            section,
            key,
            label,
            InspectorFieldKind.Enum,
            isEnabled: isEnabled,
            helpText: helpText);
        field.SetOptions(options, selectedOption);
        return field;
    }

    internal InspectorFieldViewModel? Find(string key) =>
        Sections.SelectMany(section => section.Fields)
            .FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.Ordinal));

    private void OnFieldChanged(InspectorFieldViewModel field)
    {
        if (!field.IsValid)
        {
            var section = Sections.FirstOrDefault(item => item.Fields.Contains(field));
            if (section is not null)
                section.IsExpanded = true;
        }
        FieldChanged?.Invoke(field);
    }
}

public interface INodeInspectorProvider
{
    bool CanHandle(NodeBaseViewModel node);

    void Load(NodeBaseViewModel node, InspectorViewModel inspector);

    void Apply(NodeBaseViewModel node, InspectorViewModel inspector);
}
