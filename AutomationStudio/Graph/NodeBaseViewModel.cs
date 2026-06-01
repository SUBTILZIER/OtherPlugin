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

    private string _title = string.Empty;
    private double _x;
    private double _y;
    private bool _isSelected;
    private string _description = string.Empty;

    protected NodeBaseViewModel(string id, string title)
    {
        Id = id;
        _title = title;
    }

    public string Id { get; init; }

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
        NodeKind.Start => new SolidColorBrush(Color.FromRgb(122, 24, 31)),
        NodeKind.FindImage => new SolidColorBrush(Color.FromRgb(34, 102, 143)),
        NodeKind.MouseClick => new SolidColorBrush(Color.FromRgb(148, 90, 40)),
        NodeKind.Delay => new SolidColorBrush(Color.FromRgb(94, 58, 153)),
        NodeKind.MouseMove => new SolidColorBrush(Color.FromRgb(35, 120, 91)),
        NodeKind.Keyboard => new SolidColorBrush(Color.FromRgb(200, 130, 60)),
        NodeKind.ScrollWheel => new SolidColorBrush(Color.FromRgb(160, 110, 180)),
        NodeKind.Reroute => new SolidColorBrush(Color.FromRgb(244, 244, 244)),
        NodeKind.If => new SolidColorBrush(Color.FromRgb(50, 140, 80)),
        NodeKind.ForLoop => new SolidColorBrush(Color.FromRgb(180, 120, 40)),
        NodeKind.WhileLoop => new SolidColorBrush(Color.FromRgb(160, 80, 120)),
        NodeKind.StartProgram => new SolidColorBrush(Color.FromRgb(45, 130, 180)),
        NodeKind.PrintLog => new SolidColorBrush(Color.FromRgb(60, 170, 100)),
        NodeKind.SelectWindow => new SolidColorBrush(Color.FromRgb(80, 120, 200)),
        NodeKind.FindText => new SolidColorBrush(Color.FromRgb(120, 80, 180)),
        _ => new SolidColorBrush(Color.FromRgb(70, 70, 70)),
    };

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(255, 215, 96))
        : new SolidColorBrush(Color.FromRgb(66, 74, 88));

    public abstract void RefreshDescription();

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

    protected PinViewModel AddOutput(string name, string displayName, PinKind kind)
    {
        PinViewModel pin = new(this, name, displayName, PinDirection.Output, kind);
        OutputPins.Add(pin);
        return pin;
    }
}
