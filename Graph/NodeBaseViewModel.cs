using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

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
        NodeKind.MouseLeftClick => new SolidColorBrush(Color.FromRgb(148, 90, 40)),
        _ => new SolidColorBrush(Color.FromRgb(70, 70, 70)),
    };

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(255, 215, 96))
        : new SolidColorBrush(Color.FromRgb(66, 74, 88));

    public abstract void RefreshDescription();

    public Point GetPinAnchor(PinViewModel pin)
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
        return InputPins.Concat(OutputPins).FirstOrDefault(pin => pin.Name == pinName);
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
