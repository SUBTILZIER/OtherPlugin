using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Structured inspector for simple common nodes. Complex nodes keep legacy controls.
/// </summary>
internal sealed class CommonNodeInspectorProvider : INodeInspectorProvider
{
    public bool CanHandle(NodeBaseViewModel node) => node is CommonNodeViewModel common &&
        common.NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr or NodeKind.BooleanNot or
            NodeKind.StringConcat or NodeKind.Compare or NodeKind.ShowMessage;

    public void Load(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        if (node is not CommonNodeViewModel common)
            return;

        inspector.Reset(node);
        InspectorSectionViewModel values = inspector.AddSection("参数");
        switch (common.NodeKind)
        {
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
                for (int i = 1; i <= common.VariadicInputCount; i++)
                {
                    string pinName = CommonNodeViewModel.VariadicInputName(i);
                    bool connected = HasConnection(common, pinName);
                    inspector.AddField(
                        values,
                        pinName,
                        common.VariadicInputLabel(i),
                        InspectorFieldKind.Boolean,
                        booleanValue: bool.TryParse(common.GetVariadicInputDefault(pinName), out bool flag) && flag,
                        isEnabled: !connected,
                        helpText: connected ? "前置输入" : "未连接时使用此默认值");
                }
                break;

            case NodeKind.BooleanNot:
                inspector.AddField(
                    values,
                    "value",
                    "输入值",
                    InspectorFieldKind.Boolean,
                    booleanValue: common.Flag,
                    isEnabled: !HasConnection(common, "value"),
                    helpText: HasConnection(common, "value") ? "前置输入" : "未连接时使用此默认值");
                break;

            case NodeKind.StringConcat:
                for (int i = 1; i <= common.VariadicInputCount; i++)
                {
                    string pinName = CommonNodeViewModel.VariadicInputName(i);
                    bool connected = HasConnection(common, pinName);
                    inspector.AddField(
                        values,
                        pinName,
                        common.VariadicInputLabel(i),
                        InspectorFieldKind.Text,
                        value: common.GetVariadicInputDefault(pinName),
                        isEnabled: !connected,
                        multiline: !connected,
                        helpText: connected ? "前置输入" : "未连接时使用此默认值");
                }
                break;

            case NodeKind.Compare:
                AddTextField(inspector, values, common, "left", "左值", common.Text);
                AddTextField(inspector, values, common, "right", "右值", common.Text2);
                AddTextField(inspector, values, common, "operator", "操作", common.Text3);
                break;

            case NodeKind.ShowMessage:
                AddTextField(inspector, values, common, "text", "消息内容", common.Text, multiline: true);
                AddTextField(inspector, values, common, "title", "窗口标题", common.Text2);
                break;
        }
    }

    public void Apply(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        if (node is not CommonNodeViewModel common)
            return;

        switch (common.NodeKind)
        {
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
            case NodeKind.StringConcat:
                for (int i = 1; i <= common.VariadicInputCount; i++)
                {
                    string pinName = CommonNodeViewModel.VariadicInputName(i);
                    if (HasConnection(common, pinName) || inspector.Find(pinName) is not { } field)
                        continue;

                    common.SetVariadicInputDefault(
                        pinName,
                        field.IsBoolean ? field.BooleanValue.ToString() : field.Value);
                }
                break;

            case NodeKind.BooleanNot:
                if (!HasConnection(common, "value") && inspector.Find("value") is { } boolField)
                    common.Flag = boolField.BooleanValue;
                break;

            case NodeKind.Compare:
                ApplyText(common, inspector, "left", value => common.Text = value);
                ApplyText(common, inspector, "right", value => common.Text2 = value);
                ApplyText(common, inspector, "operator", value => common.Text3 = value);
                break;

            case NodeKind.ShowMessage:
                ApplyText(common, inspector, "text", value => common.Text = value);
                ApplyText(common, inspector, "title", value => common.Text2 = value);
                break;
        }
    }

    private static void AddTextField(
        InspectorViewModel inspector,
        InspectorSectionViewModel section,
        CommonNodeViewModel node,
        string key,
        string label,
        string value,
        bool multiline = false)
    {
        inspector.AddField(
            section,
            key,
            label,
            InspectorFieldKind.Text,
            value,
            isEnabled: !HasConnection(node, key),
            multiline: multiline,
            helpText: HasConnection(node, key) ? "前置输入" : null);
    }

    private static void ApplyText(CommonNodeViewModel node, InspectorViewModel inspector, string key, Action<string> apply)
    {
        if (!HasConnection(node, key) && inspector.Find(key) is { } field)
            apply(field.Value);
    }

    private static bool HasConnection(CommonNodeViewModel node, string pinName) =>
        node.InputPins.FirstOrDefault(pin => pin.Name == pinName)?.HasConnection == true;
}
