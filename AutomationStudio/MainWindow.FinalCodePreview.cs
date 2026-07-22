using System.Windows;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void ShowFinalCodePreview()
    {
        CommitInspectorAndSnapshotAllSessions();

        var session = GetOperationEditorSession();
        var controller = session is null ? null : GetSessionActiveAssetController(session);
        if (session is null || controller?.ActiveItem is null)
        {
            const string message = "没有可预览的当前图表。";
            SetStatus(message);
            ThemedDialog.Show(this, message, "显示最终代码", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GraphWorkspaceReadModel readModel = BuildGraphWorkspaceReadModel();
        FinalCodePreviewResult result = _finalCodePreviewService.Generate(
            readModel,
            session.ContentAsset.Id,
            controller.ActiveItem.Id);
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            SetStatus($"最终代码生成失败：{result.ErrorMessage}");
            ThemedDialog.Show(
                this,
                result.ErrorMessage,
                "显示最终代码",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (_finalCodePreviewWindow is null || _finalCodePreviewWindow.IsClosed)
        {
            _finalCodePreviewWindow = new FinalCodePreviewWindow(this);
            _finalCodePreviewWindow.Closed += (_, _) => _finalCodePreviewWindow = null;
        }

        _finalCodePreviewWindow.SetPreview(result.Text, null);
        _finalCodePreviewWindow.ActivateWindow();
    }
}
