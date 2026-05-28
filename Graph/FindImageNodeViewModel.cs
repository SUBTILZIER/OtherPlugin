using System.IO;

namespace AutomationStudioWpf.Graph;

/// <summary>
/// Blueprint-style node for image matching.
/// Returns execution flow, success state, and the matched center point.
/// </summary>
public sealed class FindImageNodeViewModel : NodeBaseViewModel
{
    private string _imagePath = string.Empty;
    private int _similarityThresholdPercent = 80;

    public FindImageNodeViewModel(string id) : base(id, "找图")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("success", "成功", PinKind.Boolean);
        AddOutput("center", "中心点", PinKind.Vector2D);
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
            {
                RefreshDescription();
            }
        }
    }

    public int SimilarityThresholdPercent
    {
        get => _similarityThresholdPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _similarityThresholdPercent, clamped))
            {
                RefreshDescription();
            }
        }
    }

    public override void RefreshDescription()
    {
        string fileName = string.IsNullOrWhiteSpace(ImagePath) ? "未设置" : Path.GetFileName(ImagePath);
        Description = $"图像：{fileName}\n相似度阈值：{SimilarityThresholdPercent}%\n输出：bool + Vector2D";
    }
}
