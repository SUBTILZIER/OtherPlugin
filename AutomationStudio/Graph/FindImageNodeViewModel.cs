using System.IO;

namespace AutomationStudioWpf.Graph;

public sealed class FindImageNodeViewModel : NodeBaseViewModel
{
    private string _imagePath = string.Empty;
    private string _sourceImagePath = string.Empty;
    private ImageSearchSourceMode _sourceMode = ImageSearchSourceMode.RealtimeScreenshot;
    private int _similarityThresholdPercent = 80;
    private bool _useRegion;
    private double _regionX;
    private double _regionY;
    private double _regionWidth;
    private double _regionHeight;

    public FindImageNodeViewModel(string id) : base(id, "找图")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddInput("image_path", "查找目标", PinKind.String);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("result", "结果", PinKind.Boolean);
        AddOutput("center", "中心点", PinKind.Vector2D);
        SyncSourceInputPin();
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.FindImage;
    public override string NodeTypeKey => "find_image";

    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetProperty(ref _imagePath, value))
                RefreshDescription();
        }
    }

    public int SimilarityThresholdPercent
    {
        get => _similarityThresholdPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _similarityThresholdPercent, clamped))
                RefreshDescription();
        }
    }

    public string SourceImagePath
    {
        get => _sourceImagePath;
        set
        {
            if (SetProperty(ref _sourceImagePath, value))
                RefreshDescription();
        }
    }

    public ImageSearchSourceMode SourceMode
    {
        get => _sourceMode;
        set
        {
            if (SetProperty(ref _sourceMode, value))
            {
                SyncSourceInputPin();
                RefreshDescription();
            }
        }
    }

    public bool UseRegion
    {
        get => _useRegion;
        set
        {
            if (SetProperty(ref _useRegion, value))
                RefreshDescription();
        }
    }

    public double RegionX
    {
        get => _regionX;
        set
        {
            if (SetProperty(ref _regionX, value))
                RefreshDescription();
        }
    }

    public double RegionY
    {
        get => _regionY;
        set
        {
            if (SetProperty(ref _regionY, value))
                RefreshDescription();
        }
    }

    public double RegionWidth
    {
        get => _regionWidth;
        set
        {
            if (SetProperty(ref _regionWidth, value))
                RefreshDescription();
        }
    }

    public double RegionHeight
    {
        get => _regionHeight;
        set
        {
            if (SetProperty(ref _regionHeight, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string target = InputPins.FirstOrDefault(p => p.Name == "image_path")?.HasConnection == true
            ? "前置输入"
            : (string.IsNullOrWhiteSpace(ImagePath) ? "未设置目标" : Path.GetFileName(ImagePath));
        string source = SourceMode == ImageSearchSourceMode.RealtimeScreenshot
            ? "实时截屏"
            : (InputPins.FirstOrDefault(p => p.Name == "source_image_path")?.HasConnection == true
                ? "前置输入"
                : (string.IsNullOrWhiteSpace(SourceImagePath) ? "未设置源" : Path.GetFileName(SourceImagePath)));
        string region = UseRegion
            ? $"区域 ({RegionX:0},{RegionY:0},{RegionWidth:0},{RegionHeight:0})"
            : "区域 全屏";
        Description = $"源：{source}\n目标：{target}\n阈值 {SimilarityThresholdPercent}%\n{region}";
    }

    public void SyncSourceInputPin()
    {
        bool shouldShow = SourceMode == ImageSearchSourceMode.ManualImage;
        bool exists = InputPins.Any(pin => pin.Name == "source_image_path");
        if (shouldShow && !exists)
        {
            InsertInput(1, "source_image_path", "查找源", PinKind.String);
        }
        else if (!shouldShow && exists)
        {
            RemoveInput("source_image_path");
        }
    }
}
