namespace AutomationStudioWpf.Graph;

public sealed class GraphParameterDefinition : ObservableObject
{
    private string _name = "NewParam";
    private GraphParameterType _type = GraphParameterType.Boolean;
    private string _defaultValue = DefaultValueForType(GraphParameterType.Boolean);

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "NewParam" : value.Trim());
    }

    public GraphParameterType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public string DefaultValue
    {
        get => _defaultValue;
        set => SetProperty(ref _defaultValue, value.Trim());
    }

    public PinKind ToPinKind() => Type switch
    {
        GraphParameterType.Boolean => PinKind.Boolean,
        GraphParameterType.Vector2D => PinKind.Vector2D,
        _ => PinKind.String,
    };

    public GraphParameterDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        DefaultValue = DefaultValue,
    };

    public static string DefaultValueForType(GraphParameterType type) => type switch
    {
        GraphParameterType.Boolean => "False",
        GraphParameterType.Vector2D => "0,0",
        GraphParameterType.Float => "0.0",
        GraphParameterType.Vector3D => "0,0,0",
        GraphParameterType.Vector4D => "0,0,0,0",
        _ => string.Empty,
    };
}
