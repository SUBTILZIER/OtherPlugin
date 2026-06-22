using System.Collections.ObjectModel;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace AutomationStudioWpf.Graph;

public abstract class NodeBaseViewModel : ObservableObject
{
    public const double NodeWidth = 280;
    public const double HeaderHeight = 30;
    public const double PinAreaTopPadding = 8;
    public const double PinAnchorInset = 14;

    private static readonly Brush StartHeaderBrush = FrozenBrush(122, 24, 31);
    private static readonly Brush CustomEventHeaderBrush = FrozenBrush(150, 36, 44);
    private static readonly Brush CustomEventCallHeaderBrush = FrozenBrush(114, 38, 48);
    private static readonly Brush FindImageHeaderBrush = FrozenBrush(34, 102, 143);
    private static readonly Brush MouseClickHeaderBrush = FrozenBrush(148, 90, 40);
    private static readonly Brush DelayHeaderBrush = FrozenBrush(94, 58, 153);
    private static readonly Brush MouseMoveHeaderBrush = FrozenBrush(35, 120, 91);
    private static readonly Brush KeyboardHeaderBrush = FrozenBrush(200, 130, 60);
    private static readonly Brush ScrollWheelHeaderBrush = FrozenBrush(160, 110, 180);
    private static readonly Brush RerouteHeaderBrush = FrozenBrush(244, 244, 244);
    private static readonly Brush IfHeaderBrush = FrozenBrush(50, 140, 80);
    private static readonly Brush ForLoopHeaderBrush = FrozenBrush(180, 120, 40);
    private static readonly Brush WhileLoopHeaderBrush = FrozenBrush(160, 80, 120);
    private static readonly Brush ToDoHeaderBrush = FrozenBrush(205, 112, 42);
    private static readonly Brush MultiThreadHeaderBrush = FrozenBrush(38, 132, 150);
    private static readonly Brush StartProgramHeaderBrush = FrozenBrush(45, 130, 180);
    private static readonly Brush PrintLogHeaderBrush = FrozenBrush(60, 170, 100);
    private static readonly Brush SelectWindowHeaderBrush = FrozenBrush(80, 120, 200);
    private static readonly Brush FunctionHeaderBrush = FrozenBrush(92, 92, 255);
    private static readonly Brush DefaultHeaderBrush = FrozenBrush(70, 70, 70);
    private static readonly Brush HeaderForeground = FrozenBrush(255, 255, 255);
    private static readonly Brush SelectedBorderBrush = FrozenBrush(255, 215, 96);
    private static readonly Brush DefaultBorderBrush = FrozenBrush(66, 74, 88);

    private string _title = string.Empty;
    private double _x;
    private double _y;
    private bool _isSelected;
    private string _description = string.Empty;
    private string _nodeNumber = string.Empty;

    protected NodeBaseViewModel(string id, string title)
    {
        Id = id;
        _title = title;
    }

    public string Id { get; init; }

    public string NodeNumber
    {
        get => _nodeNumber;
        set
        {
            if (SetProperty(ref _nodeNumber, value))
            {
                OnPropertyChanged(nameof(HasNodeNumber));
            }
        }
    }

    public bool HasNodeNumber => !string.IsNullOrWhiteSpace(NodeNumber);

    public virtual bool CanAddDynamicPin => CanAddVariadicInput;

    public virtual bool CanRemoveDynamicPin => CanRemoveVariadicInput;

    public virtual string AddDynamicPinToolTip => "添加输入";

    public virtual string RemoveDynamicPinToolTip => "删除最后一个输入";

    public virtual bool CanAddVariadicInput => false;

    public virtual bool CanRemoveVariadicInput => false;

    public abstract NodeKind NodeKind { get; }

    public abstract string NodeTypeKey { get; }

    public virtual bool CanDelete => true;

    public virtual double Width => NodeWidth;

    public virtual double Height
    {
        get
        {
            int rows = Math.Max(InputPins.Count, OutputPins.Count);
            return HeaderHeight + PinAreaTopPadding + rows * PinViewModel.PinRowHeight + 16;
        }
    }

    public ObservableCollection<PinViewModel> InputPins { get; } = [];

    public ObservableCollection<PinViewModel> OutputPins { get; } = [];

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BorderBrush));
            }
        }
    }

    public string Description
    {
        get => _description;
        protected set => SetProperty(ref _description, value);
    }

    public Brush HeaderBrush => NodeKind switch
    {
        NodeKind.Start => StartHeaderBrush,
        NodeKind.CustomEvent => CustomEventHeaderBrush,
        NodeKind.CustomEventCall => CustomEventCallHeaderBrush,
        NodeKind.FindImage => FindImageHeaderBrush,
        NodeKind.MouseClick => MouseClickHeaderBrush,
        NodeKind.Delay => DelayHeaderBrush,
        NodeKind.MouseMove => MouseMoveHeaderBrush,
        NodeKind.Keyboard => KeyboardHeaderBrush,
        NodeKind.ScrollWheel => ScrollWheelHeaderBrush,
        NodeKind.Reroute => RerouteHeaderBrush,
        NodeKind.If => IfHeaderBrush,
        NodeKind.ForLoop => ForLoopHeaderBrush,
        NodeKind.WhileLoop => WhileLoopHeaderBrush,
        NodeKind.ToDo => ToDoHeaderBrush,
        NodeKind.MultiThread => MultiThreadHeaderBrush,
        NodeKind.StartProgram => StartProgramHeaderBrush,
        NodeKind.PrintLog => PrintLogHeaderBrush,
        NodeKind.SelectWindow => SelectWindowHeaderBrush,
        NodeKind.FunctionEntry or NodeKind.FunctionReturn or NodeKind.FunctionCall => FunctionHeaderBrush,
        _ => DefaultHeaderBrush,
    };

    public Brush HeaderForegroundBrush => HeaderForeground;

    public Brush BorderBrush => IsSelected
        ? SelectedBorderBrush
        : DefaultBorderBrush;

    public abstract void RefreshDescription();

    public virtual bool AddVariadicInput() => false;

    public virtual bool RemoveLastVariadicInput() => false;

    public virtual bool AddDynamicPin() => AddVariadicInput();

    public virtual string? GetLastDynamicPinName() => null;

    public virtual bool RemoveLastDynamicPin() => RemoveLastVariadicInput();

    public virtual Point GetPinAnchor(PinViewModel pin)
    {
        if (pin.AnchorPoint != default)
        {
            return pin.AnchorPoint;
        }

        var pinList = pin.Direction == PinDirection.Input ? InputPins : OutputPins;
        int verticalIndex = 0;
        foreach (var p in pinList)
        {
            if (p == pin) break;
            verticalIndex++;
        }

        double y = HeaderHeight + PinAreaTopPadding + verticalIndex * PinViewModel.PinRowHeight + PinViewModel.PinRowHeight / 2.0;
        double x = pin.Direction == PinDirection.Input ? PinAnchorInset : Width - PinAnchorInset;
        return new Point(x, y);
    }

    public PinViewModel? FindPin(string pinName)
    {
        return OutputPins.FirstOrDefault(pin => pin.Name == pinName)
            ?? InputPins.FirstOrDefault(pin => pin.Name == pinName);
    }

    protected PinViewModel AddInput(string name, string displayName, PinKind kind)
    {
        PinViewModel pin = new(this, name, displayName, PinDirection.Input, kind);
        InputPins.Add(pin);
        return pin;
    }

    protected PinViewModel InsertInput(int index, string name, string displayName, PinKind kind)
    {
        PinViewModel pin = new(this, name, displayName, PinDirection.Input, kind);
        InputPins.Insert(Math.Clamp(index, 0, InputPins.Count), pin);
        return pin;
    }

    protected void RemoveInput(string name)
    {
        var pin = InputPins.FirstOrDefault(p => p.Name == name);
        if (pin is not null)
            InputPins.Remove(pin);
    }

    protected PinViewModel AddOutput(string name, string displayName, PinKind kind)
    {
        PinViewModel pin = new(this, name, displayName, PinDirection.Output, kind);
        OutputPins.Add(pin);
        return pin;
    }

    protected PinViewModel AddOutput(string name, string displayName, PinKind kind, ExecutionPinRole executionRole)
    {
        PinViewModel pin = new(this, name, displayName, PinDirection.Output, kind, executionRole);
        OutputPins.Add(pin);
        return pin;
    }

    protected void RemoveOutput(string name)
    {
        var pin = OutputPins.FirstOrDefault(p => p.Name == name);
        if (pin is not null)
            OutputPins.Remove(pin);
    }

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
