using System.Collections.ObjectModel;
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

    public ObservableCollection<InspectorOption> Options { get; } = [];

    public bool IsMultiline { get; init; }

    public string? HelpText { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
                Changed?.Invoke(this);
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
    internal InspectorSectionViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public ObservableCollection<InspectorFieldViewModel> Fields { get; } = [];
}

public sealed class InspectorViewModel
{
    public ObservableCollection<InspectorSectionViewModel> Sections { get; } = [];

    public NodeBaseViewModel? Node { get; private set; }

    public bool IsStructured { get; private set; }

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
        IsStructured = false;
    }

    internal InspectorSectionViewModel AddSection(string title)
    {
        var section = new InspectorSectionViewModel(title);
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

    private void OnFieldChanged(InspectorFieldViewModel field) => FieldChanged?.Invoke(field);
}

public interface INodeInspectorProvider
{
    bool CanHandle(NodeBaseViewModel node);

    void Load(NodeBaseViewModel node, InspectorViewModel inspector);

    void Apply(NodeBaseViewModel node, InspectorViewModel inspector);
}
