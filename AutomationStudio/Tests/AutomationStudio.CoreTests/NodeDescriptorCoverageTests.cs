using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class NodeDescriptorCoverageTests
{
    [TestMethod]
    public void EveryNodeKindHasCoreAndPresentationDescriptors()
    {
        NodeKind[] kinds = Enum.GetValues<NodeKind>();

        CollectionAssert.AreEquivalent(kinds, NodeDescriptorCatalog.All.Select(item => item.Kind).ToArray());
        CollectionAssert.AreEquivalent(kinds, NodePresentationCatalog.All.Select(item => item.Kind).ToArray());
        Assert.AreEqual(kinds.Length, NodeDescriptorCatalog.All.Select(item => item.TypeKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void RuntimeCapabilitiesMatchRegisteredImplementations()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        foreach (NodeDescriptor descriptor in NodeDescriptorCatalog.All)
        {
            Assert.IsTrue(registry.TryGetDefinition(descriptor.Kind, out var definition), $"缺少节点定义：{descriptor.Kind}");
            Assert.AreEqual(descriptor.CanCreate, definition.CanCreate, descriptor.Kind.ToString());
            Assert.AreEqual(descriptor.RuntimeSupport, definition.RuntimeSupport, descriptor.Kind.ToString());

            if (descriptor.RuntimeSupport == NodeRuntimeSupport.RegistryExecutor)
                Assert.IsTrue(registry.TryGetExecutor(descriptor.Kind, out _), $"缺少 executor：{descriptor.Kind}");
            if (descriptor.RuntimeSupport == NodeRuntimeSupport.PureEvaluator)
                Assert.IsTrue(NodeTraits.IsPure(descriptor.Kind), $"纯节点 trait 不一致：{descriptor.Kind}");
        }
    }

    [TestMethod]
    public void EveryCreatableNodeUsesCanonicalMetadataAndRoundTrips()
    {
        var factory = new NodeFactory();
        foreach (NodeDescriptor descriptor in NodeDescriptorCatalog.All.Where(item => item.CanCreate))
        {
            NodeBaseViewModel node = factory.CreateNode(descriptor.Kind, 10, 20);
            Assert.AreEqual(descriptor.TypeKey, node.NodeTypeKey, descriptor.Kind.ToString());
            Assert.AreEqual(NodePresentationCatalog.Get(descriptor.Kind).DisplayName, node.Title, descriptor.Kind.ToString());
            Assert.AreEqual(descriptor.CanDelete, node.CanDelete, descriptor.Kind.ToString());

            NodeFileModel file = NodeSerializer.ToFileModel(node);
            NodeBaseViewModel? restored = NodeSerializer.FromFileModel(file);
            Assert.IsNotNull(restored, $"节点无法反序列化：{descriptor.Kind}");
            Assert.AreEqual(descriptor.Kind, restored.NodeKind, descriptor.Kind.ToString());
            Assert.AreEqual(descriptor.TypeKey, restored.NodeTypeKey, descriptor.Kind.ToString());
        }
    }

    [TestMethod]
    public void StructuralNodesUseDeclaredDeleteAndSerializationRules()
    {
        var factory = new NodeFactory();
        NodeBaseViewModel[] nodes =
        [
            factory.CreateStartNode(),
            factory.CreateRerouteNode(PinKind.Execution, 0, 0),
            new FunctionEntryNodeViewModel("entry"),
            new FunctionReturnNodeViewModel("return"),
            factory.CreateFunctionCallNode("function", "函数", [], [], 0, 0),
            factory.CreateCustomEventCallNode("event", "事件", [], 0, 0),
        ];

        foreach (NodeBaseViewModel node in nodes)
        {
            NodeDescriptor descriptor = NodeDescriptorCatalog.Get(node.NodeKind);
            Assert.AreEqual(descriptor.CanDelete, node.CanDelete, node.NodeKind.ToString());
            Assert.IsTrue(descriptor.HasSerializer, node.NodeKind.ToString());
            Assert.AreEqual(node.NodeKind, NodeSerializer.FromFileModel(NodeSerializer.ToFileModel(node))?.NodeKind, node.NodeKind.ToString());
        }

        Assert.IsFalse(NodeDescriptorCatalog.Get(NodeKind.Comment).HasSerializer);
        Assert.IsNull(NodeSerializer.FromFileModel(new NodeFileModel { Id = "comment", NodeTypeKey = "comment" }));
    }

    [TestMethod]
    public void LegacyTypeKeysResolveWithoutBecomingCanonicalWrites()
    {
        Assert.AreEqual(NodeKind.MouseClick, NodeDescriptorCatalog.FromTypeKey("mouse_left_click"));
        Assert.AreEqual(NodeKind.MouseClick, NodeDescriptorCatalog.FromTypeKey("mouse_double_click"));
        Assert.AreEqual("mouse_click", NodeDescriptorCatalog.Get(NodeKind.MouseClick).TypeKey);
    }
}
